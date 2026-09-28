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

namespace DiskInfoToolkit.Probes
{
    public static class ScsiCapacityProbe
    {
        #region Fields

        private const int ReadCapacity10Length = 8;

        private const int ReadCapacity16Length = 32;

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
            if (TryReadCapacity16(ioControl, handle, out var lastLba, out var blockLength)
                || TryReadCapacity10(ioControl, handle, out lastLba, out blockLength))
            {
                device.Scsi.LastLogicalBlockAddress = lastLba;
                device.Scsi.LogicalBlockLength = blockLength;

                if (!device.DiskSizeBytes.HasValue && blockLength != 0)
                {
                    try
                    {
                        checked
                        {
                            device.DiskSizeBytes = (lastLba + 1UL) * blockLength;
                        }
                    }
                    catch (OverflowException)
                    {
                    }
                }

                if (string.IsNullOrWhiteSpace(device.CapacitySource))
                {
                    device.CapacitySource = "SCSI Read Capacity";
                }

                return true;
            }

            return false;
        }

        #endregion

        #region Private

        private static bool TryReadCapacity10(IStorageIoControl ioControl, SafeFileHandle handle, out ulong lastLba, out uint blockLength)
        {
            lastLba = 0;
            blockLength = 0;

            var request = SCSI_PASS_THROUGH_WITH_BUFFERS.CreateDefault();
            request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            request.Spt.CdbLength = 10;
            request.Spt.SenseInfoLength = (byte)request.SenseBuf.Length;
            request.Spt.DataIn = 1;
            request.Spt.DataTransferLength = ReadCapacity10Length;
            request.Spt.TimeOutValue = 2;
            request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.DataBuf)).ToInt32();
            request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.SenseBuf)).ToInt32();
            request.Spt.Cdb[0] = 0x25;

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<SCSI_PASS_THROUGH_WITH_BUFFERS>(buffer);
            if (response.DataBuf == null || response.DataBuf.Length < ReadCapacity10Length)
            {
                return false;
            }

            lastLba = ReadUInt32BigEndian(response.DataBuf, 0);
            blockLength = ReadUInt32BigEndian(response.DataBuf, 4);

            return blockLength != 0;
        }

        private static bool TryReadCapacity16(IStorageIoControl ioControl, SafeFileHandle handle, out ulong lastLba, out uint blockLength)
        {
            lastLba = 0;
            blockLength = 0;

            var request = SCSI_PASS_THROUGH_WITH_BUFFERS_EX.CreateDefault();
            request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            request.Spt.CdbLength = 16;
            request.Spt.SenseInfoLength = (byte)request.SenseBuf.Length;
            request.Spt.DataIn = 1;
            request.Spt.DataTransferLength = ReadCapacity16Length;
            request.Spt.TimeOutValue = 2;
            request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS_EX>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS_EX.DataBuf)).ToInt32();
            request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS_EX>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS_EX.SenseBuf)).ToInt32();
            request.Spt.Cdb[0] = 0x9E;
            request.Spt.Cdb[1] = 0x10;
            request.Spt.Cdb[13] = ReadCapacity16Length;

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<SCSI_PASS_THROUGH_WITH_BUFFERS_EX>(buffer);
            if (response.DataBuf == null || response.DataBuf.Length < ReadCapacity16Length)
            {
                return false;
            }

            lastLba = ReadUInt64BigEndian(response.DataBuf, 0);
            blockLength = ReadUInt32BigEndian(response.DataBuf, 8);

            return blockLength != 0;
        }

        private static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24)
                | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8)
                | data[offset + 3];
        }

        private static ulong ReadUInt64BigEndian(byte[] data, int offset)
        {
            ulong value = 0;

            for (int i = 0; i < 8; ++i)
            {
                value = (value << 8) | data[offset + i];
            }

            return value;
        }

        #endregion
    }
}
