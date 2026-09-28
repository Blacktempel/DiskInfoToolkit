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
    public static class ScsiMiniportPortProbe
    {
        #region Fields

        private const byte IdentifyDeviceCommand = 0xEC;

        private const byte SmartCommand = 0xB0;

        private const byte SmartReadDataSubcommand = 0xD0;

        private const byte SmartReadThresholdSubcommand = 0xD1;

        private const byte SmartEnableOperationsSubcommand = 0xD8;

        private const byte SmartCylinderLow = 0x4F;

        private const byte SmartCylinderHigh = 0xC2;

        private const int SectorBytes = 512;

        private static readonly byte[] ScsiSignature = Encoding.ASCII.GetBytes("SCSIDISK");

        #endregion

        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || !device.Scsi.PortNumber.HasValue)
            {
                return false;
            }

            string scsiPortPath = StoragePathBuilder.BuildScsiPortPath(device.Scsi.PortNumber.Value);
            SafeFileHandle handle = ioControl.OpenDevice(
                scsiPortPath,
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
                return TryPopulateDataFromHandle(device, ioControl, handle, scsiPortPath);
            }
        }

        public static bool TryPopulateDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle, string scsiPortPath)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            bool changed = false;
            if (TryIdentify(ioControl, handle, device, out var identifyData))
            {
                StandardAtaProbe.ApplyAtaIdentify(device, identifyData);
                device.AlternateDevicePath = scsiPortPath;
                changed = true;
            }

            byte[] smartData = null;
            byte[] smartThresholds = null;

            bool smartOk =
                (TryReadSmart(ioControl, handle, device, IoControlCodes.IOCTL_SCSI_MINIPORT_READ_SMART_ATTRIBS, out smartData)
                    && TryReadSmart(ioControl, handle, device, IoControlCodes.IOCTL_SCSI_MINIPORT_READ_SMART_THRESHOLDS, out smartThresholds));

            if (!smartOk)
            {
                if (TryEnableSmart(ioControl, handle, device))
                {
                    smartOk =
                        (TryReadSmart(ioControl, handle, device, IoControlCodes.IOCTL_SCSI_MINIPORT_READ_SMART_ATTRIBS, out smartData)
                            && TryReadSmart(ioControl, handle, device, IoControlCodes.IOCTL_SCSI_MINIPORT_READ_SMART_THRESHOLDS, out smartThresholds));
                }
            }

            if (smartOk)
            {
                device.SupportsSmart = true;
                device.SmartAttributes = SmartProbe.ParseSmartPages(smartData, smartThresholds);
                device.AlternateDevicePath = scsiPortPath;
                changed = true;
            }

            if (changed && string.IsNullOrWhiteSpace(device.Controller.Kind))
            {
                device.Controller.Kind = ControllerKindNames.ScsiMiniport;
            }

            return changed;
        }

        #endregion

        #region Private

        private static bool TryIdentify(IStorageIoControl ioControl, SafeFileHandle handle, StorageDevice device, out byte[] identifyData)
        {
            var srb = CreateSrb(IoControlCodes.IOCTL_SCSI_MINIPORT_IDENTIFY, Marshal.SizeOf<SENDCMDOUTPARAMS>() + SectorBytes);

            var input = SENDCMDINPARAMS.CreateDefault();
            input.irDriveRegs.bCommandReg = IdentifyDeviceCommand;
            input.bDriveNumber = device.Scsi.TargetID.HasValue ? device.Scsi.TargetID.Value : (byte)0;

            return ExecuteMiniport(ioControl, handle, srb, input, out identifyData);
        }

        private static bool TryReadSmart(IStorageIoControl ioControl, SafeFileHandle handle, StorageDevice device, uint controlCode, out byte[] data)
        {
            var srb = CreateSrb(controlCode, Marshal.SizeOf<SENDCMDOUTPARAMS>() + SectorBytes);

            var input = SENDCMDINPARAMS.CreateDefault();
            input.irDriveRegs.bFeaturesReg = controlCode == IoControlCodes.IOCTL_SCSI_MINIPORT_READ_SMART_THRESHOLDS ? SmartReadThresholdSubcommand : SmartReadDataSubcommand;
            input.irDriveRegs.bSectorCountReg = 1;
            input.irDriveRegs.bSectorNumberReg = 1;
            input.irDriveRegs.bCylLowReg = SmartCylinderLow;
            input.irDriveRegs.bCylHighReg = SmartCylinderHigh;
            input.irDriveRegs.bCommandReg = SmartCommand;
            input.bDriveNumber = device.Scsi.TargetID.HasValue ? device.Scsi.TargetID.Value : (byte)0;

            return ExecuteMiniport(ioControl, handle, srb, input, out data);
        }

        private static bool TryEnableSmart(IStorageIoControl ioControl, SafeFileHandle handle, StorageDevice device)
        {
            var srb = CreateSrb(IoControlCodes.IOCTL_SCSI_MINIPORT_ENABLE_SMART, Marshal.SizeOf<SENDCMDOUTPARAMS>());

            var input = SENDCMDINPARAMS.CreateDefault();
            input.irDriveRegs.bFeaturesReg = SmartEnableOperationsSubcommand;
            input.irDriveRegs.bSectorCountReg = 1;
            input.irDriveRegs.bSectorNumberReg = 1;
            input.irDriveRegs.bCylLowReg = SmartCylinderLow;
            input.irDriveRegs.bCylHighReg = SmartCylinderHigh;
            input.irDriveRegs.bCommandReg = SmartCommand;
            input.bDriveNumber = device.Scsi.TargetID.HasValue ? device.Scsi.TargetID.Value : (byte)0;

            return ExecuteMiniport(ioControl, handle, srb, input, out _);
        }

        private static bool ExecuteMiniport(IStorageIoControl ioControl, SafeFileHandle handle, SRB_IO_CONTROL srb, SENDCMDINPARAMS input, out byte[] data)
        {
            data = null;

            var srbBytes = StructureHelper.GetBytes(srb);
            var inputBytes = StructureHelper.GetBytes(input);

            var buffer = new byte[Marshal.SizeOf<SRB_IO_CONTROL>() + Marshal.SizeOf<SENDCMDOUTPARAMS>() + SectorBytes];

            Buffer.BlockCopy(srbBytes, 0, buffer, 0, srbBytes.Length);
            Buffer.BlockCopy(inputBytes, 0, buffer, srbBytes.Length, inputBytes.Length);

            if (!ioControl.TryScsiMiniport(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            int outputOffset = Marshal.SizeOf<SRB_IO_CONTROL>();
            int dataOffset = outputOffset + Marshal.SizeOf<SENDCMDOUTPARAMS>() - 1;

            if (dataOffset + SectorBytes > buffer.Length)
            {
                return false;
            }

            data = new byte[SectorBytes];
            Buffer.BlockCopy(buffer, dataOffset, data, 0, data.Length);

            return true;
        }

        private static SRB_IO_CONTROL CreateSrb(uint controlCode, int payloadLength)
        {
            var srb = new SRB_IO_CONTROL();
            srb.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            srb.Signature = new byte[8];

            Array.Copy(ScsiSignature, srb.Signature, Math.Min(ScsiSignature.Length, srb.Signature.Length));

            srb.Timeout = 2;
            srb.ControlCode = controlCode;
            srb.Length = (uint)payloadLength;

            return srb;
        }

        #endregion
    }
}
