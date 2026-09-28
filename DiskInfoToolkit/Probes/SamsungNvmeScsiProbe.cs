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
using Microsoft.Win32.SafeHandles;

namespace DiskInfoToolkit.Probes
{
    public static class SamsungNvmeScsiProbe
    {
        #region Fields

        private const int BufferLength = 0x1050;

        private const int TransferLength = 4096;

        private const int DataOffset = 80;

        private const int SenseOffset = 56;

        private const int IdentifyLength = 4096;

        private const int SmartLength = 512;

        #endregion

        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
        {
            if (!LooksLikeSamsungNvme(device) || ioControl == null || string.IsNullOrWhiteSpace(device.DevicePath))
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
                bool identifyOk = TryReadIdentify(ioControl, handle, out var identifyData);
                if (identifyOk)
                {
                    device.Nvme.IdentifyControllerData = identifyData;

                    IntelNvmeProbeUtil.ApplyIdentifyControllerStrings(device, identifyData);

                    device.TransportKind = StorageTransportKind.Nvme;
                    device.BusType = StorageBusType.Nvme;

                    if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                    {
                        device.Controller.Kind = ControllerKindNames.NvmePci;
                    }
                }

                bool smartOk = TryReadSmartLog(ioControl, handle, out var smartLogData);
                if (smartOk)
                {
                    device.Nvme.SmartLogData = smartLogData;

                    NvmeSmartLogParser.ApplySmartLog(device, smartLogData);

                    device.SupportsSmart = true;
                    device.TransportKind = StorageTransportKind.Nvme;
                    device.BusType = StorageBusType.Nvme;
                }

                return identifyOk || smartOk;
            }
        }

        #endregion

        #region Private

        private static bool LooksLikeSamsungNvme(StorageDevice device)
        {
            string text = (device.ProductName ?? string.Empty) + " " + (device.DisplayName ?? string.Empty) + " " + (device.VendorName ?? string.Empty) + " " + (device.Controller.HardwareID ?? string.Empty);
            return text.IndexOf(UsbBridgeFamilyNames.Samsung, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReadIdentify(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] identifyData)
        {
            identifyData = null;
            var buffer = CreateRequest(0xA2, 0xFE, 0x00, 0x06, 0x00, 0x00, 0x01, 0x00);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            if (buffer.Length > 2 && buffer[2] != 0)
            {
                return false;
            }

            identifyData = new byte[IdentifyLength];
            Buffer.BlockCopy(buffer, DataOffset, identifyData, 0, identifyData.Length);

            return NvmeProbeUtil.HasAnyNonZeroByte(identifyData);
        }

        private static bool TryReadSmartLog(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] smartLogData)
        {
            smartLogData = null;

            var buffer = CreateRequest(0xA2, 0xFE, 0x00, 0x02, 0x00, 0x00, 0x7F, 0x00);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            if (buffer.Length > 2 && buffer[2] != 0)
            {
                return false;
            }

            smartLogData = new byte[SmartLength];
            Buffer.BlockCopy(buffer, DataOffset, smartLogData, 0, smartLogData.Length);

            return NvmeProbeUtil.HasAnyNonZeroByte(smartLogData);
        }

        private static byte[] CreateRequest(byte cdb0, byte cdb1, byte cdb2, byte cdb3, byte cdb4, byte cdb5, byte cdb8, byte cdb9)
        {
            var buffer = new byte[BufferLength];

            BuildScsiPassThroughHeader(buffer, 16, 24, true, TransferLength, 2, DataOffset, SenseOffset);
            buffer[36] = cdb0;
            buffer[37] = cdb1;
            buffer[38] = cdb2;
            buffer[39] = cdb3;
            buffer[40] = cdb4;
            buffer[41] = cdb5;
            buffer[44] = cdb8;
            buffer[45] = cdb9;

            return buffer;
        }

        private static void BuildScsiPassThroughHeader(byte[] buffer, byte cdbLength, byte senseLength, bool dataIn, int transferLength, uint timeoutSeconds, int dataOffset, int senseOffset)
        {
            Array.Clear(buffer, 0, Math.Min(buffer.Length, dataOffset));

            WriteUInt16(buffer, 0, 56);
            buffer[6] = cdbLength;
            buffer[7] = senseLength;
            buffer[8] = dataIn ? (byte)1 : (byte)0;
            WriteUInt32(buffer, 12, (uint)transferLength);
            WriteUInt32(buffer, 16, timeoutSeconds);
            WriteUInt64(buffer, 24, (ulong)dataOffset);
            WriteUInt32(buffer, 32, (uint)senseOffset);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            var tmp = BitConverter.GetBytes(value);
            Buffer.BlockCopy(tmp, 0, buffer, offset, 2);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            var tmp = BitConverter.GetBytes(value);
            Buffer.BlockCopy(tmp, 0, buffer, offset, 4);
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            var tmp = BitConverter.GetBytes(value);
            Buffer.BlockCopy(tmp, 0, buffer, offset, 8);
        }

        #endregion
    }
}
