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
    public static class StandardAtaProbe
    {
        #region Fields

        private const byte IdentifyDeviceCommand = 0xEC;

        private const byte TargetMaster = 0xA0;

        private const byte TargetSlave = 0xB0;

        private const ushort AtaFlagsDataIn = 0x02;

        private const int IdentifyBufferLength = 512;

        private static readonly byte[] ScsiSignature = Encoding.ASCII.GetBytes("SCSIDISK");

        #endregion

        #region Public

        public static bool TryPopulateIdentifyData(StorageDevice device, IStorageIoControl ioControl)
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
                byte[] identifyData = null;
                if (TryIdentifyViaAtaPassThrough(ioControl, handle, TargetMaster, out identifyData)
                    || TryIdentifyViaAtaPassThrough(ioControl, handle, TargetSlave, out identifyData)
                    || TryIdentifyViaIdePassThrough(ioControl, handle, TargetMaster, out identifyData)
                    || TryIdentifyViaIdePassThrough(ioControl, handle, TargetSlave, out identifyData)
                    || TryIdentifyViaDfp(ioControl, handle, TargetMaster, out identifyData)
                    || TryIdentifyViaDfp(ioControl, handle, TargetSlave, out identifyData)
                    || TryIdentifyViaScsiMiniport(ioControl, handle, out identifyData))
                {
                    ApplyAtaIdentify(device, identifyData);
                    return true;
                }
            }

            return false;
        }

        public static void ApplyAtaIdentify(StorageDevice device, byte[] identifyData)
        {
            if (identifyData == null || identifyData.Length < IdentifyBufferLength)
            {
                return;
            }

            var serial   = AtaStringDecoder.ReadWordSwappedString(identifyData, 10, 10);
            var firmware = AtaStringDecoder.ReadWordSwappedString(identifyData, 23,  4);
            var model    = AtaStringDecoder.ReadWordSwappedString(identifyData, 27, 20);

            if (!string.IsNullOrWhiteSpace(model))
            {
                device.ProductName = model;
                if (string.IsNullOrWhiteSpace(device.DisplayName) || device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase))
                {
                    device.DisplayName = model;
                }
            }

            if (!string.IsNullOrWhiteSpace(serial))
            {
                device.SerialNumber = serial;
            }

            if (!string.IsNullOrWhiteSpace(firmware))
            {
                device.ProductRevision = firmware;
            }
        }

        #endregion

        #region Private

        private static bool TryIdentifyViaAtaPassThrough(IStorageIoControl ioControl, SafeFileHandle handle, byte target, out byte[] identifyData)
        {
            identifyData = null;

            var request = ATA_PASS_THROUGH_EX_WITH_BUFFERS.CreateDefault();
            request.Apt.Length = (ushort)Marshal.SizeOf<ATA_PASS_THROUGH_EX>();
            request.Apt.TimeOutValue = 2;
            request.Apt.AtaFlags = AtaFlagsDataIn;
            request.Apt.DataTransferLength = IdentifyBufferLength;
            request.Apt.DataBufferOffset = (ulong)Marshal.OffsetOf<ATA_PASS_THROUGH_EX_WITH_BUFFERS>(nameof(ATA_PASS_THROUGH_EX_WITH_BUFFERS.Buf)).ToInt64();
            request.Apt.CurrentTaskFile.bCommandReg = IdentifyDeviceCommand;
            request.Apt.CurrentTaskFile.bDriveHeadReg = target;

            var requestBuffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryAtaPassThrough(handle, requestBuffer, requestBuffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<ATA_PASS_THROUGH_EX_WITH_BUFFERS>(requestBuffer);

            identifyData = new byte[IdentifyBufferLength];
            Buffer.BlockCopy(response.Buf, 0, identifyData, 0, identifyData.Length);

            return true;
        }

        private static bool TryIdentifyViaIdePassThrough(IStorageIoControl ioControl, SafeFileHandle handle, byte target, out byte[] identifyData)
        {
            identifyData = null;

            var request = ATA_PASS_THROUGH_EX_WITH_BUFFERS.CreateDefault();
            request.Apt.Length = (ushort)Marshal.SizeOf<ATA_PASS_THROUGH_EX>();
            request.Apt.TimeOutValue = 2;
            request.Apt.AtaFlags = AtaFlagsDataIn;
            request.Apt.DataTransferLength = IdentifyBufferLength;
            request.Apt.DataBufferOffset = (ulong)Marshal.OffsetOf<ATA_PASS_THROUGH_EX_WITH_BUFFERS>(nameof(ATA_PASS_THROUGH_EX_WITH_BUFFERS.Buf)).ToInt64();
            request.Apt.CurrentTaskFile.bCommandReg = IdentifyDeviceCommand;
            request.Apt.CurrentTaskFile.bDriveHeadReg = target;

            var requestBuffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryIdePassThrough(handle, requestBuffer, requestBuffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<ATA_PASS_THROUGH_EX_WITH_BUFFERS>(requestBuffer);

            identifyData = new byte[IdentifyBufferLength];
            Buffer.BlockCopy(response.Buf, 0, identifyData, 0, identifyData.Length);

            return true;
        }

        private static bool TryIdentifyViaDfp(IStorageIoControl ioControl, SafeFileHandle handle, byte target, out byte[] identifyData)
        {
            identifyData = null;

            var input = SENDCMDINPARAMS.CreateDefault();
            input.cBufferSize = IdentifyBufferLength;
            input.irDriveRegs.bCommandReg = IdentifyDeviceCommand;
            input.irDriveRegs.bSectorCountReg = 1;
            input.irDriveRegs.bSectorNumberReg = 1;
            input.irDriveRegs.bDriveHeadReg = target;

            var inputBuffer = StructureHelper.GetBytes(input);
            var outputBuffer = new byte[Marshal.SizeOf<SENDCMDOUTPARAMS>() + IdentifyBufferLength - 1];

            if (!ioControl.TrySmartReceiveDriveData(handle, inputBuffer, outputBuffer, out var bytesReturned))
            {
                return false;
            }

            int dataOffset = Marshal.SizeOf<SENDCMDOUTPARAMS>() - 1;
            if (dataOffset + IdentifyBufferLength > outputBuffer.Length)
            {
                return false;
            }

            identifyData = new byte[IdentifyBufferLength];
            Buffer.BlockCopy(outputBuffer, dataOffset, identifyData, 0, identifyData.Length);

            return true;
        }

        private static bool TryIdentifyViaScsiMiniport(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] identifyData)
        {
            identifyData = null;

            if (!ioControl.TryGetScsiAddress(handle, out var scsiAddress))
            {
                return false;
            }

            var srb = new SRB_IO_CONTROL();
            srb.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            srb.Signature = new byte[8];

            Array.Copy(ScsiSignature, srb.Signature, Math.Min(ScsiSignature.Length, srb.Signature.Length));

            srb.Timeout = 2;
            srb.ControlCode = IoControlCodes.IOCTL_SCSI_MINIPORT_IDENTIFY;
            srb.Length = (uint)(Marshal.SizeOf<SENDCMDOUTPARAMS>() + IdentifyBufferLength);

            var input = SENDCMDINPARAMS.CreateDefault();
            input.irDriveRegs.bCommandReg = IdentifyDeviceCommand;
            input.bDriveNumber = scsiAddress.TargetID;

            var srbBytes = StructureHelper.GetBytes(srb);
            var inputBytes = StructureHelper.GetBytes(input);
            var buffer = new byte[Marshal.SizeOf<SRB_IO_CONTROL>() + Marshal.SizeOf<SENDCMDOUTPARAMS>() + IdentifyBufferLength];

            Buffer.BlockCopy(srbBytes, 0, buffer, 0, srbBytes.Length);
            Buffer.BlockCopy(inputBytes, 0, buffer, srbBytes.Length, inputBytes.Length);

            if (!ioControl.TryScsiMiniport(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            int outputOffset = Marshal.SizeOf<SRB_IO_CONTROL>();
            int dataOffset = outputOffset + Marshal.SizeOf<SENDCMDOUTPARAMS>() - 1;
            if (dataOffset + IdentifyBufferLength > buffer.Length)
            {
                return false;
            }

            identifyData = new byte[IdentifyBufferLength];
            Buffer.BlockCopy(buffer, dataOffset, identifyData, 0, identifyData.Length);
            return true;
        }

        #endregion
    }
}
