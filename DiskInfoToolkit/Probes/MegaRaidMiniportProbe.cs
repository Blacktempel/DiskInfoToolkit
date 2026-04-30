/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Models;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Probes
{
    internal static class MegaRaidMiniportProbe
    {
        #region Fields

        private const byte MfiCmdPdScsiIo = 0x04;

        private const byte MfiCmdDcmd = 0x05;

        private const byte MfiStatOk = 0x00;

        private const ushort MfiFrameDirNone = 0x0000;

        private const ushort MfiFrameDirRead = 0x0010;

        private const uint MfiDcmdPdGetList = 0x02010000;

        private const byte AtaPassThrough12 = 0xA1;

        private const byte AtaPassThrough12ProtocolPioDataIn = 0x08;

        private const byte AtaPassThrough12TransferFlags = 0x0E;

        private const byte IdentifyDeviceCommand = 0xEC;

        private const byte SmartCommand = 0xB0;

        private const byte SmartReadDataSubcommand = 0xD0;

        private const byte SmartReadThresholdSubcommand = 0xD1;

        private const byte SmartSectorNumber = 0x00;

        private const byte SmartCylinderLow = 0x4F;

        private const byte SmartCylinderHigh = 0xC2;

        private const byte SmartDriveHead = 0x00;

        private const byte MegaRaidPassThroughInitialStatus = 0xFF;

        private const int IdentifyBufferLength = 512;

        private const int SmartBufferLength = 512;

        private const int InitialPhysicalDriveListLength = 1024;

        private static readonly string[] MiniportSignatures =
        {
            "LSILOGIC",
            "MEGARAID"
        };

        #endregion

        #region Public

        public static bool TryPopulate(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null)
            {
                return false;
            }

            if (!TryResolvePortAndTarget(device, ioControl, out byte portNumber, out byte targetId))
            {
                return false;
            }

            SafeFileHandle handle = ioControl.OpenDevice(
                StoragePathBuilder.BuildScsiPortPath(portNumber),
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
                bool identityOk = TryReadAtaIdentifyFromHandle(ioControl, handle, targetId, out byte[] identifyData);
                bool smartOk = TryReadSmartFromHandle(ioControl, handle, targetId, out byte[] smartData, out byte[] smartThresholds);

                if (!identityOk && !smartOk)
                {
                    return false;
                }

                device.TransportKind = StorageTransportKind.Raid;
                device.BusType = StorageBusType.RAID;
                device.Controller.Family = StorageControllerFamily.MegaRaid;
                device.Controller.Kind = ControllerKindNames.MegaRaid;

                if (string.IsNullOrWhiteSpace(device.Controller.Class))
                {
                    device.Controller.Class = ControllerClassNames.ScsiAdapter;
                }

                if (identityOk)
                {
                    StandardAtaProbe.ApplyAtaIdentify(device, identifyData);
                }

                if (smartOk)
                {
                    var attributes = SmartProbe.ParseSmartPages(smartData, smartThresholds);
                    if (attributes.Count > 0)
                    {
                        device.SupportsSmart = true;
                        device.SmartAttributes = attributes;
                    }
                }

                device.ProbeTrace.Add($"MegaRAID miniport pass-through succeeded: port = {portNumber}, target = {targetId}.");
                return true;
            }
        }

        public static bool TryGetPhysicalDriveList(IStorageIoControl ioControl, byte portNumber, out List<MegaRaidPhysicalDriveAddress> addresses)
        {
            addresses = new List<MegaRaidPhysicalDriveAddress>();

            if (ioControl == null)
            {
                return false;
            }

            SafeFileHandle handle = ioControl.OpenDevice(
                StoragePathBuilder.BuildScsiPortPath(portNumber),
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
                int listLength = InitialPhysicalDriveListLength;
                for (int attempt = 0; attempt < 4; ++attempt)
                {
                    if (!SendDcmd(ioControl, handle, MfiDcmdPdGetList, listLength, out byte[] listData))
                    {
                        return false;
                    }

                    int headerLength = Marshal.SizeOf<MEGARAID_PHYSICAL_DRIVE_LIST_HEADER>();
                    if (listData.Length < headerLength)
                    {
                        return false;
                    }

                    var header = FromBytes<MEGARAID_PHYSICAL_DRIVE_LIST_HEADER>(listData, 0);
                    if (header.Size > listLength && header.Size <= MegaRaidMiniportConstants.DataBufferLength)
                    {
                        listLength = (int)header.Size;
                        continue;
                    }

                    int addressLength = Marshal.SizeOf<MEGARAID_PHYSICAL_DRIVE_ADDRESS>();
                    int availableCount = Math.Min((int)header.Count, Math.Max(0, (listData.Length - headerLength) / addressLength));

                    for (int index = 0; index < availableCount; ++index)
                    {
                        int offset = headerLength + index * addressLength;
                        var nativeAddress = FromBytes<MEGARAID_PHYSICAL_DRIVE_ADDRESS>(listData, offset);

                        if (nativeAddress.ScsiDevType > 0) //Skips controller as controller is also a device
                        {
                            continue;
                        }

                        var address = new MegaRaidPhysicalDriveAddress
                        {
                            DeviceId = nativeAddress.DeviceId,
                            EnclosureDeviceId = nativeAddress.EnclDeviceId,
                            EnclosureIndex = nativeAddress.EnclIndex,
                            Slot = nativeAddress.SlotNumber,
                            ScsiDeviceType = nativeAddress.ScsiDevType,
                            ConnectPortBitmap = nativeAddress.ConnectPortBitmap,
                            SasAddress0 = nativeAddress.SasAddr != null && nativeAddress.SasAddr.Length > 0 ? nativeAddress.SasAddr[0] : 0,
                            SasAddress1 = nativeAddress.SasAddr != null && nativeAddress.SasAddr.Length > 1 ? nativeAddress.SasAddr[1] : 0
                        };

                        addresses.Add(address);
                    }

                    return addresses.Count > 0;
                }
            }

            return false;
        }

        public static bool TryReadAtaIdentify(IStorageIoControl ioControl, byte portNumber, byte targetId, out byte[] identifyData)
        {
            identifyData = null;

            if (ioControl == null)
            {
                return false;
            }

            SafeFileHandle handle = ioControl.OpenDevice(
                StoragePathBuilder.BuildScsiPortPath(portNumber),
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
                return TryReadAtaIdentifyFromHandle(ioControl, handle, targetId, out identifyData);
            }
        }

        #endregion

        #region Private

        private static bool TryResolvePortAndTarget(StorageDevice device, IStorageIoControl ioControl, out byte portNumber, out byte targetId)
        {
            portNumber = 0;
            targetId = 0;

            if (device.Scsi.PortNumber.HasValue && device.Scsi.TargetID.HasValue)
            {
                portNumber = device.Scsi.PortNumber.Value;
                targetId = device.Scsi.TargetID.Value;

                return true;
            }

            if (!string.IsNullOrWhiteSpace(device.DevicePath))
            {
                SafeFileHandle handle = ioControl.OpenDevice(
                    device.DevicePath,
                    IoAccess.GenericRead | IoAccess.GenericWrite,
                    IoShare.ReadWrite,
                    IoCreation.OpenExisting,
                    IoFlags.Normal);

                if (handle != null && !handle.IsInvalid)
                {
                    using (handle)
                    {
                        if (ioControl.TryGetScsiAddress(handle, out var scsiAddress))
                        {
                            portNumber = scsiAddress.PortNumber;
                            targetId = scsiAddress.TargetID;
                            device.Scsi.PortNumber = portNumber;
                            device.Scsi.PathID = scsiAddress.PathID;
                            device.Scsi.TargetID = targetId;
                            device.Scsi.Lun = scsiAddress.Lun;
                            return true;
                        }
                    }
                }
            }

            if (!device.Scsi.PortNumber.HasValue)
            {
                return false;
            }

            if (!TryGetPhysicalDriveList(ioControl, device.Scsi.PortNumber.Value, out var addresses) || addresses.Count != 1)
            {
                return false;
            }

            if (addresses[0].DeviceId > byte.MaxValue)
            {
                return false;
            }

            portNumber = device.Scsi.PortNumber.Value;
            targetId = (byte)addresses[0].DeviceId;
            device.Scsi.TargetID = targetId;
            return true;
        }

        private static bool TryReadAtaIdentifyFromHandle(IStorageIoControl ioControl, SafeFileHandle handle, byte targetId, out byte[] identifyData)
        {
            identifyData = null;

            var cdb = CreateAtaPassThrough12Cdb
            (
                0x00,
                0x01,
                0x00,
                0x00,
                0x00,
                0x00,
                IdentifyDeviceCommand
            );

            if (!SendAtaPassThrough(ioControl, handle, targetId, cdb, 12, IdentifyBufferLength, out byte[] data))
            {
                return false;
            }

            identifyData = data;
            return true;
        }

        private static bool TryReadSmartFromHandle(IStorageIoControl ioControl, SafeFileHandle handle, byte targetId, out byte[] smartData, out byte[] smartThresholds)
        {
            smartData = null;
            smartThresholds = null;

            return TryReadSmartPageFromHandle(ioControl, handle, targetId, SmartReadDataSubcommand, out smartData)
                && TryReadSmartPageFromHandle(ioControl, handle, targetId, SmartReadThresholdSubcommand, out smartThresholds);
        }

        private static bool TryReadSmartPageFromHandle(IStorageIoControl ioControl, SafeFileHandle handle, byte targetId, byte subcommand, out byte[] data)
        {
            data = null;

            var cdb = CreateAtaPassThrough12Cdb
            (
                subcommand,
                0x01,
                SmartSectorNumber,
                SmartCylinderLow,
                SmartCylinderHigh,
                SmartDriveHead,
                SmartCommand
            );

            if (!SendAtaPassThrough(ioControl, handle, targetId, cdb, 12, SmartBufferLength, out byte[] page))
            {
                return false;
            }

            data = page;
            return true;
        }

        private static bool SendDcmd(IStorageIoControl ioControl, SafeFileHandle handle, uint opcode, int dataLength, out byte[] data)
        {
            data = null;

            if (dataLength <= 0 || dataLength > MegaRaidMiniportConstants.DataBufferLength)
            {
                return false;
            }

            foreach (string signature in MiniportSignatures)
            {
                var request = MEGARAID_DCOMD_IOCTL.Create(signature, (uint)dataLength);
                request.Mpt.Cmd = MfiCmdDcmd;
                request.Mpt.Flags = MfiFrameDirNone;
                request.Mpt.TimeOutValue = 0;
                request.Mpt.DataTransferLength = (uint)dataLength;
                request.Mpt.Opcode = opcode;

                var requestBuffer = StructureHelper.GetBytes(request);
                var responseBuffer = new byte[requestBuffer.Length];

                if (!ioControl.TryScsiMiniport(handle, requestBuffer, responseBuffer, out var bytesReturned))
                {
                    continue;
                }

                int minimumResponseLength = Marshal.SizeOf<SRB_IO_CONTROL>() + Marshal.SizeOf<MEGARAID_DCOMD>();
                if (bytesReturned < minimumResponseLength)
                {
                    continue;
                }

                var response = StructureHelper.FromBytes<MEGARAID_DCOMD_IOCTL>(responseBuffer);
                if (response.Mpt.CmdStatus != MfiStatOk)
                {
                    continue;
                }

                int dataOffset = GetDcmdDataOffset();
                int transferLength = GetValidTransferLength(response.Mpt.DataTransferLength, dataLength);

                if (bytesReturned - dataOffset < transferLength)
                {
                    continue;
                }

                data = CopyDataBuffer(response.DataBuf, transferLength);
                return true;
            }

            return false;
        }

        private static bool SendAtaPassThrough(IStorageIoControl ioControl, SafeFileHandle handle, byte targetId, byte[] cdb, byte cdbLength, int dataLength, out byte[] data)
        {
            data = null;

            if (cdb == null || cdbLength == 0 || cdbLength > 16 || cdb.Length < cdbLength || dataLength <= 0 || dataLength > MegaRaidMiniportConstants.DataBufferLength)
            {
                return false;
            }

            foreach (string signature in MiniportSignatures)
            {
                var request = MEGARAID_PASS_THROUGH_IOCTL.Create(signature, (uint)dataLength);
                request.Mpt.Cmd = MfiCmdPdScsiIo;
                request.Mpt.CmdStatus = MegaRaidPassThroughInitialStatus;
                request.Mpt.ScsiStatus = 0x00;
                request.Mpt.TargetId = targetId;
                request.Mpt.Lun = 0;
                request.Mpt.CdbLength = cdbLength;
                request.Mpt.TimeOutValue = 0;
                request.Mpt.Flags = MfiFrameDirRead;
                request.Mpt.DataTransferLength = (uint)dataLength;
                Buffer.BlockCopy(cdb, 0, request.Mpt.Cdb, 0, cdbLength);

                var requestBuffer = StructureHelper.GetBytes(request);
                var responseBuffer = new byte[requestBuffer.Length];

                if (!ioControl.TryScsiMiniport(handle, requestBuffer, responseBuffer, out var bytesReturned))
                {
                    continue;
                }

                int minimumResponseLength = Marshal.SizeOf<SRB_IO_CONTROL>() + Marshal.SizeOf<MEGARAID_PASS_THROUGH>();
                if (bytesReturned < minimumResponseLength)
                {
                    continue;
                }

                var response = StructureHelper.FromBytes<MEGARAID_PASS_THROUGH_IOCTL>(responseBuffer);
                if (response.Mpt.CmdStatus != MfiStatOk)
                {
                    continue;
                }

                int dataOffset = GetPassThroughDataOffset();
                int transferLength = GetValidTransferLength(response.Mpt.DataTransferLength, dataLength);

                if (bytesReturned - dataOffset < transferLength)
                {
                    continue;
                }

                data = CopyDataBuffer(response.DataBuf, transferLength);
                return true;
            }

            return false;
        }

        private static byte[] CreateAtaPassThrough12Cdb(byte features, byte sectorCount, byte lbaLow, byte lbaMid, byte lbaHigh, byte device, byte command)
        {
            var cdb = new byte[16];

            cdb[0] = AtaPassThrough12;
            cdb[1] = AtaPassThrough12ProtocolPioDataIn;
            cdb[2] = AtaPassThrough12TransferFlags;
            cdb[3] = features;
            cdb[4] = sectorCount;
            cdb[5] = lbaLow;
            cdb[6] = lbaMid;
            cdb[7] = lbaHigh;
            cdb[8] = device;
            cdb[9] = command;

            return cdb;
        }

        private static int GetValidTransferLength(uint reportedTransferLength, int requestedLength)
        {
            if (reportedTransferLength == 0 || reportedTransferLength > MegaRaidMiniportConstants.DataBufferLength)
            {
                return requestedLength;
            }

            return Math.Min((int)reportedTransferLength, requestedLength);
        }

        private static byte[] CopyDataBuffer(byte[] dataBuffer, int length)
        {
            if (dataBuffer == null || length <= 0)
            {
                return null;
            }

            int count = Math.Min(length, dataBuffer.Length);
            var data = new byte[count];

            Buffer.BlockCopy(dataBuffer, 0, data, 0, count);

            return data;
        }

        private static int GetDcmdDataOffset()
        {
            return Marshal.SizeOf<MEGARAID_DCOMD_IOCTL>() - MegaRaidMiniportConstants.DataBufferLength;
        }

        private static int GetPassThroughDataOffset()
        {
            return Marshal.SizeOf<MEGARAID_PASS_THROUGH_IOCTL>() - MegaRaidMiniportConstants.DataBufferLength;
        }

        private static T FromBytes<T>(byte[] buffer, int offset)
            where T : struct
        {
            int size = Marshal.SizeOf<T>();
            var ptr = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.Copy(buffer, offset, ptr, size);
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        #endregion
    }
}
