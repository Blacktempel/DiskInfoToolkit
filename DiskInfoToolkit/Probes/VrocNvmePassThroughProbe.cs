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
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Probes
{
    public static class VrocNvmePassThroughProbe
    {
        #region Fields

        private const int BufferLength = 4096;

        private const int SmartLogLength = 512;

        private const uint NvmePassThroughSrbIoCode = 0xE0002000;

        private const uint NvmeFromDeviceToHost = 2;

        private const uint NvmePassThroughTimeoutSeconds = 40;

        private const byte NvmeAdminIdentifyOpcode = 0x06;

        private const byte NvmeAdminGetLogPageOpcode = 0x02;

        private const uint NvmeNamespaceIdController = 0;

        private const uint NvmeNamespaceIdOne = 1;

        private const uint NvmeNamespaceIdAll = 0xFFFFFFFF;

        private const uint NvmeIdentifyNamespaceCns = 0;

        private const uint NvmeIdentifyControllerCns = 1;

        private const uint NvmeSmartLogCdw10 = 0x007F0002;

        #endregion

        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || device.Controller.Family != StorageControllerFamily.IntelVroc || !device.Scsi.PortNumber.HasValue)
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
                bool any = false;

                if (TryReadNamespaceIdentify(ioControl, handle, device, out var namespaceData))
                {
                    device.Nvme.IdentifyNamespaceData = namespaceData;
                    NvmeNamespaceParser.ApplyNamespaceData(device, namespaceData);

                    any = true;
                }

                if (TryReadControllerIdentify(ioControl, handle, device, out var controllerData))
                {
                    device.Nvme.IdentifyControllerData = controllerData;
                    IntelNvmeProbeUtil.ApplyIdentifyControllerStrings(device, controllerData);

                    any = true;
                }

                if (TryReadSmartLog(ioControl, handle, device, out var smartLogData))
                {
                    device.Nvme.SmartLogData = smartLogData;
                    device.Nvme.IntelSmartLogData = smartLogData;

                    NvmeSmartLogParser.ApplySmartLog(device, smartLogData);

                    device.SupportsSmart = true;
                    any = true;
                }

                if (any)
                {
                    device.TransportKind = StorageTransportKind.Nvme;
                    device.BusType = StorageBusType.Nvme;
                    device.AlternateDevicePath = scsiPortPath;

                    if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                    {
                        device.Controller.Kind = "NVMe RAID";
                    }

                    ProbeTraceRecorder.Add(device, "VROC path: NVMe pass-through succeeded with NvmeRAID request layout.");
                }

                return any;
            }
        }

        public static bool TryRefreshSmartLog(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || device.Controller.Family != StorageControllerFamily.IntelVroc || !device.Scsi.PortNumber.HasValue)
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
                if (!TryReadSmartLog(ioControl, handle, device, out var smartLogData))
                {
                    return false;
                }

                device.Nvme.SmartLogData = smartLogData;
                device.Nvme.IntelSmartLogData = smartLogData;

                NvmeSmartLogParser.ApplySmartLog(device, smartLogData);

                device.SupportsSmart = true;
                device.TransportKind = StorageTransportKind.Nvme;
                device.BusType = StorageBusType.Nvme;
                device.AlternateDevicePath = scsiPortPath;

                return true;
            }
        }

        #endregion

        #region Private

        private static bool TryReadControllerIdentify(IStorageIoControl ioControl, SafeFileHandle handle, StorageDevice device, out byte[] controllerData)
        {
            controllerData = null;

            var request = CreateBaseRequest(device);

            request.NVMeCmd[0] = NvmeAdminIdentifyOpcode;
            request.NVMeCmd[1] = NvmeNamespaceIdController;
            request.NVMeCmd[10] = NvmeIdentifyControllerCns;
            request.DataBuffer[0] = 1;

            return Execute(ioControl, handle, request, BufferLength, out controllerData);
        }

        private static bool TryReadNamespaceIdentify(IStorageIoControl ioControl, SafeFileHandle handle, StorageDevice device, out byte[] namespaceData)
        {
            namespaceData = null;

            var request = CreateBaseRequest(device);

            request.NVMeCmd[0] = NvmeAdminIdentifyOpcode;
            request.NVMeCmd[1] = NvmeNamespaceIdOne;
            request.NVMeCmd[10] = NvmeIdentifyNamespaceCns;
            request.DataBuffer[0] = 1;

            return Execute(ioControl, handle, request, BufferLength, out namespaceData);
        }

        private static bool TryReadSmartLog(IStorageIoControl ioControl, SafeFileHandle handle, StorageDevice device, out byte[] smartLogData)
        {
            smartLogData = null;

            var request = CreateBaseRequest(device);

            request.NVMeCmd[0] = NvmeAdminGetLogPageOpcode;
            request.NVMeCmd[1] = NvmeNamespaceIdAll;
            request.NVMeCmd[10] = NvmeSmartLogCdw10;
            request.DataBuffer[0] = 1;

            return Execute(ioControl, handle, request, SmartLogLength, out smartLogData);
        }

        private static NVME_PASS_THROUGH_IOCTL CreateBaseRequest(StorageDevice device)
        {
            var request = NVME_PASS_THROUGH_IOCTL.CreateDefault();

            request.SrbIoCtrl.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            request.SrbIoCtrl.Signature = IntelRaidMiniportConstants.CreateNvmeRaidSignature();
            request.SrbIoCtrl.Timeout = NvmePassThroughTimeoutSeconds;
            request.SrbIoCtrl.ControlCode = NvmePassThroughSrbIoCode;
            request.SrbIoCtrl.Length = (uint)(Marshal.SizeOf<NVME_PASS_THROUGH_IOCTL>() - Marshal.SizeOf<SRB_IO_CONTROL>());
            request.SrbIoCtrl.ReturnCode = BuildReturnCode(device);

            request.Direction = NvmeFromDeviceToHost;
            request.QueueID = 0;
            request.MetaDataLen = 0;
            request.DataBufferLen = (uint)request.DataBuffer.Length;
            request.ReturnBufferLen = (uint)Marshal.SizeOf<NVME_PASS_THROUGH_IOCTL>();

            return request;
        }

        private static uint BuildReturnCode(StorageDevice device)
        {
            byte path = device.Scsi.PathID.GetValueOrDefault();
            byte target = device.Scsi.TargetID.GetValueOrDefault();
            byte lun = device.Scsi.Lun.GetValueOrDefault();

            return 0x86000000u + ((uint)path << 16) + ((uint)target << 8) + lun;
        }

        private static bool Execute(IStorageIoControl ioControl, SafeFileHandle handle, NVME_PASS_THROUGH_IOCTL request, int resultLength, out byte[] data)
        {
            data = null;
            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiMiniport(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<NVME_PASS_THROUGH_IOCTL>(buffer);
            if (!NvmeProbeUtil.HasAnyNonZeroByte(response.DataBuffer))
            {
                return false;
            }

            int copyLength = Math.Min(resultLength, response.DataBuffer.Length);

            data = new byte[copyLength];
            Buffer.BlockCopy(response.DataBuffer, 0, data, 0, copyLength);

            return true;
        }

        #endregion
    }
}
