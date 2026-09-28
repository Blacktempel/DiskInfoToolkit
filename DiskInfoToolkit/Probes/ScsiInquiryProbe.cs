/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.Probes
{
    public static class ScsiInquiryProbe
    {
        #region Fields

        private const int InquiryAllocationLength = 96;

        private const int VpdSerialAllocationLength = 252;

        #endregion

        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || string.IsNullOrWhiteSpace(device.DevicePath))
            {
                return false;
            }

            SafeFileHandle handle = ioControl.OpenDevice(
                device.DevicePath,
                IoAccess.GenericRead | IoAccess.GenericWrite,
                IoShare.ReadWrite,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            if (handle == null || handle.IsInvalid)
            {
                return false;
            }

            using (handle)
            {
                return TryPopulateDataFromHandle(device, ioControl, handle);
            }
        }

        public static bool TryPopulateDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle)
        {
            if (!TryReadStandardInquiry(ioControl, handle, out var inquiryData))
            {
                return false;
            }

            ApplyInquiryStrings(device, inquiryData);
            UsbBridgeClassifier.ApplyInquiryHeuristics(device);

            if (TryReadUnitSerialPage(ioControl, handle, out var serialPage))
            {
                ApplyUnitSerial(device, serialPage);
            }

            if (TryReadDeviceIdPage(ioControl, handle, out var deviceIdPage))
            {
                ApplyDeviceIdentifier(device, deviceIdPage);
            }

            return true;
        }

        public static void ApplyInquiryStrings(StorageDevice device, byte[] inquiryData)
        {
            if (inquiryData == null || inquiryData.Length < 36)
            {
                return;
            }

            device.Scsi.PeripheralDeviceType = (byte)(inquiryData[0] & 0x1F);
            device.Scsi.RemovableMedia = (inquiryData[1] & 0x80) != 0;

            var vendor   = ReadAsciiTrimmed(inquiryData,  8,  8);
            var product  = ReadAsciiTrimmed(inquiryData, 16, 16);
            var revision = ReadAsciiTrimmed(inquiryData, 32,  4);

            if (!string.IsNullOrWhiteSpace(vendor) && string.IsNullOrWhiteSpace(device.Scsi.InquiryVendorID))
            {
                device.Scsi.InquiryVendorID = vendor;
            }

            if (!string.IsNullOrWhiteSpace(product) && string.IsNullOrWhiteSpace(device.Scsi.InquiryProductID))
            {
                device.Scsi.InquiryProductID = product;
            }

            if (!string.IsNullOrWhiteSpace(revision) && string.IsNullOrWhiteSpace(device.Scsi.InquiryProductRevision))
            {
                device.Scsi.InquiryProductRevision = revision;
            }

            if (!string.IsNullOrWhiteSpace(vendor) && string.IsNullOrWhiteSpace(device.VendorName))
            {
                device.VendorName = vendor;
            }

            if (!string.IsNullOrWhiteSpace(product))
            {
                if (string.IsNullOrWhiteSpace(device.ProductName))
                {
                    device.ProductName = product;
                }

                if (string.IsNullOrWhiteSpace(device.DisplayName) || device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase))
                {
                    device.DisplayName = product;
                }
            }

            if (!string.IsNullOrWhiteSpace(revision) && string.IsNullOrWhiteSpace(device.ProductRevision))
            {
                device.ProductRevision = revision;
            }
        }

        public static void ApplyUnitSerial(StorageDevice device, byte[] serialPage)
        {
            if (serialPage == null || serialPage.Length < 4)
            {
                return;
            }

            int payloadLength = (serialPage[2] << 8) | serialPage[3];
            if (payloadLength <= 0)
            {
                return;
            }

            int available = Math.Min(payloadLength, serialPage.Length - 4);

            var serial = ReadAsciiTrimmed(serialPage, 4, available);
            if (!string.IsNullOrWhiteSpace(serial) && string.IsNullOrWhiteSpace(device.SerialNumber))
            {
                device.SerialNumber = serial;
            }
        }

        public static void ApplyDeviceIdentifier(StorageDevice device, byte[] deviceIdPage)
        {
            if (device == null || deviceIdPage == null || deviceIdPage.Length < 8)
            {
                return;
            }

            int pageLength = (deviceIdPage[2] << 8) | deviceIdPage[3];
            int offset = 4;
            int pageEnd = Math.Min(deviceIdPage.Length, 4 + pageLength);

            while (offset + 4 <= pageEnd)
            {
                int descriptorLength = deviceIdPage[offset + 3];
                int descriptorEnd = offset + 4 + descriptorLength;
                if (descriptorEnd > pageEnd)
                {
                    break;
                }

                byte codeSet = (byte)(deviceIdPage[offset] & 0x0F);
                byte identifierType = (byte)(deviceIdPage[offset + 1] & 0x0F);
                if ((identifierType == 3 || identifierType == 8 || identifierType == 2) && descriptorLength > 0)
                {
                    string value;
                    if (codeSet == 1)
                    {
                        value = ReadAsciiTrimmed(deviceIdPage, offset + 4, descriptorLength);
                    }
                    else
                    {
                        value = BitConverter.ToString(deviceIdPage, offset + 4, descriptorLength).Replace("-", string.Empty);
                    }

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        device.Scsi.DeviceIdentifier = value;
                        return;
                    }
                }

                offset = descriptorEnd;
            }
        }

        #endregion

        #region Private

        private static bool TryReadStandardInquiry(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] inquiryData)
        {
            inquiryData = null;

            var request = SCSI_PASS_THROUGH_WITH_BUFFERS.CreateDefault();
            request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            request.Spt.CdbLength = 6;
            request.Spt.SenseInfoLength = 24;
            request.Spt.DataIn = 1;
            request.Spt.DataTransferLength = InquiryAllocationLength;
            request.Spt.TimeOutValue = 2;
            request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.DataBuf)).ToInt32();
            request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.SenseBuf)).ToInt32();
            request.Spt.Cdb = new byte[16];
            request.Spt.Cdb[0] = 0x12;
            request.Spt.Cdb[4] = (byte)InquiryAllocationLength;

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<SCSI_PASS_THROUGH_WITH_BUFFERS>(buffer);

            inquiryData = new byte[InquiryAllocationLength];
            Buffer.BlockCopy(response.DataBuf, 0, inquiryData, 0, inquiryData.Length);

            return HasInquiryData(inquiryData);
        }

        private static bool TryReadUnitSerialPage(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] serialPage)
        {
            serialPage = null;

            var request = SCSI_PASS_THROUGH_WITH_BUFFERS.CreateDefault();
            request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            request.Spt.CdbLength = 6;
            request.Spt.SenseInfoLength = 24;
            request.Spt.DataIn = 1;
            request.Spt.DataTransferLength = VpdSerialAllocationLength;
            request.Spt.TimeOutValue = 2;
            request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.DataBuf)).ToInt32();
            request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.SenseBuf)).ToInt32();
            request.Spt.Cdb = new byte[16];
            request.Spt.Cdb[0] = 0x12;
            request.Spt.Cdb[1] = 0x01;
            request.Spt.Cdb[2] = 0x80;
            request.Spt.Cdb[4] = (byte)VpdSerialAllocationLength;

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<SCSI_PASS_THROUGH_WITH_BUFFERS>(buffer);

            serialPage = new byte[VpdSerialAllocationLength];
            Buffer.BlockCopy(response.DataBuf, 0, serialPage, 0, serialPage.Length);

            return serialPage.Length >= 4 && serialPage[1] == 0x80;
        }

        private static bool TryReadDeviceIdPage(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] deviceIdPage)
        {
            deviceIdPage = null;

            var request = SCSI_PASS_THROUGH_WITH_BUFFERS.CreateDefault();
            request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            request.Spt.CdbLength = 6;
            request.Spt.SenseInfoLength = 24;
            request.Spt.DataIn = 1;
            request.Spt.DataTransferLength = VpdSerialAllocationLength;
            request.Spt.TimeOutValue = 2;
            request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.DataBuf)).ToInt32();
            request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.SenseBuf)).ToInt32();
            request.Spt.Cdb = new byte[16];
            request.Spt.Cdb[0] = 0x12;
            request.Spt.Cdb[1] = 0x01;
            request.Spt.Cdb[2] = 0x83;
            request.Spt.Cdb[4] = (byte)VpdSerialAllocationLength;

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<SCSI_PASS_THROUGH_WITH_BUFFERS>(buffer);

            deviceIdPage = new byte[VpdSerialAllocationLength];
            Buffer.BlockCopy(response.DataBuf, 0, deviceIdPage, 0, deviceIdPage.Length);

            return deviceIdPage.Length >= 4 && deviceIdPage[1] == 0x83;
        }

        private static bool HasInquiryData(byte[] inquiryData)
        {
            if (inquiryData == null || inquiryData.Length < 36)
            {
                return false;
            }

            for (int i = 8; i < 36 && i < inquiryData.Length; ++i)
            {
                if (inquiryData[i] != 0 && inquiryData[i] != 0x20)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadAsciiTrimmed(byte[] buffer, int offset, int length)
        {
            if (buffer == null || offset < 0 || length <= 0 || offset + length > buffer.Length)
            {
                return string.Empty;
            }

            return StringUtil.TrimStorageString(Encoding.ASCII.GetString(buffer, offset, length));
        }

        #endregion
    }
}
