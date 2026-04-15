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
    public static class IntelRaidMiniportProbe
    {
        #region Fields

        private const int BufferLength = 4096;

        private const byte NvmeAdminIdentifyOpcode = 0x06;

        private const byte NvmeAdminGetLogPageOpcode = 0x02;

        private const byte NvmeIdentifyControllerCns = 0x01;

        private const byte NvmeIdentifyNamespaceCns = 0x00;

        private const byte NvmeSmartHealthLogId = 0x02;

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
                var primarySignature = GetPrimarySignature(device.Controller.Service);
                return TryPopulateDataWithSignature(device, ioControl, handle, scsiPortPath, primarySignature);
            }
        }

        public static bool TryPopulateDataWithSignatureSweep(StorageDevice device, IStorageIoControl ioControl)
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
                var signatures = GetSignatureAttemptOrder(device.Controller.Service);

                bool any = false;
                for (int i = 0; i < signatures.Count; ++i)
                {
                    if (TryPopulateDataWithSignature(device, ioControl, handle, scsiPortPath, signatures[i]))
                    {
                        any = true;
                        if (device.Nvme.IntelIdentifyControllerData != null && device.Nvme.IntelSmartLogData != null && device.Nvme.IdentifyNamespaceData != null)
                        {
                            break;
                        }
                    }
                }

                return any;
            }
        }

        public static bool TryPopulateDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle, string scsiPortPath)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            var primarySignature = GetPrimarySignature(device.Controller.Service);

            return TryPopulateDataWithSignature(device, ioControl, handle, scsiPortPath, primarySignature);
        }

        public static bool TryPopulateDataWithSignatureSweepFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle, string scsiPortPath)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            var signatures = GetSignatureAttemptOrder(device.Controller.Service);

            bool any = false;
            for (int i = 0; i < signatures.Count; ++i)
            {
                if (TryPopulateDataWithSignature(device, ioControl, handle, scsiPortPath, signatures[i]))
                {
                    any = true;
                    if (device.Nvme.IntelIdentifyControllerData != null && device.Nvme.IntelSmartLogData != null && device.Nvme.IdentifyNamespaceData != null)
                    {
                        break;
                    }
                }
            }

            return any;
        }

        #endregion

        #region Private

        private static bool TryPopulateDataWithSignature(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle, string scsiPortPath, byte[] signature)
        {
            bool identifyOk  = TryMiniportIdentify         (ioControl, handle, signature, device, out var identifyData);
            bool namespaceOk = TryMiniportIdentifyNamespace(ioControl, handle, signature, device, out var namespaceData);
            bool smartOk     = TryMiniportSmart            (ioControl, handle, signature, device, out var smartLogData);

            if (identifyOk)
            {
                device.Nvme.IntelIdentifyControllerData = identifyData;
                device.Nvme.IdentifyControllerData = identifyData;

                IntelNvmeProbeUtil.ApplyIdentifyControllerStrings(device, identifyData);

                device.BusType = StorageBusType.Nvme;
                device.AlternateDevicePath = scsiPortPath;
            }

            if (namespaceOk)
            {
                device.Nvme.IdentifyNamespaceData = namespaceData;

                NvmeNamespaceParser.ApplyNamespaceData(device, namespaceData);

                device.BusType = StorageBusType.Nvme;
                device.AlternateDevicePath = scsiPortPath;
            }

            if (smartOk)
            {
                device.Nvme.IntelSmartLogData = smartLogData;
                device.Nvme.SmartLogData = smartLogData;

                NvmeSmartLogParser.ApplySmartLog(device, smartLogData);

                device.SupportsSmart = true;
                device.BusType = StorageBusType.Nvme;
                device.AlternateDevicePath = scsiPortPath;
            }

            if (identifyOk || namespaceOk || smartOk)
            {
                device.Controller.Class = string.IsNullOrWhiteSpace(device.Controller.Class) ? "SCSIAdapter" : device.Controller.Class;
                if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                {
                    device.Controller.Kind = device.Controller.Family == StorageControllerFamily.IntelVroc ? "NVMe RAID" : "RAID";
                }
            }

            return identifyOk || namespaceOk || smartOk;
        }

        private static bool TryMiniportIdentify(IStorageIoControl ioControl, SafeFileHandle handle, byte[] signature, StorageDevice device, out byte[] controllerData)
        {
            controllerData = null;

            var request = INTEL_NVME_PASS_THROUGH.CreateDefault();
            request.Srb.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            request.Srb.Signature = signature;
            request.Srb.Timeout = 10;
            request.Srb.ControlCode = IoControlCodes.IOCTL_INTEL_NVME_PASS_THROUGH;
            request.Srb.Length = (uint)(Marshal.SizeOf<INTEL_NVME_PAYLOAD>() + BufferLength);

            request.Payload.Version = 1;
            request.Payload.PathID = device.Scsi.PathID.HasValue ? device.Scsi.PathID.Value : (byte)0;
            request.Payload.TargetID = device.Scsi.TargetID.HasValue ? device.Scsi.TargetID.Value : (byte)0;
            request.Payload.Lun = device.Scsi.Lun.HasValue ? device.Scsi.Lun.Value : (byte)0;
            request.Payload.ParameterBufferLength = (uint)(Marshal.SizeOf<INTEL_NVME_PAYLOAD>() + Marshal.SizeOf<SRB_IO_CONTROL>());
            request.Payload.ReturnBufferLength = BufferLength;
            request.Payload.Cmd.CDW0 = IntelNvmeConstants.MakeCdw0(NvmeAdminIdentifyOpcode, 0);
            request.Payload.Cmd.NSID = 0;
            request.Payload.Cmd.CDW10 = NvmeIdentifyControllerCns;

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiMiniport(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<INTEL_NVME_PASS_THROUGH>(buffer);

            controllerData = new byte[BufferLength];
            Buffer.BlockCopy(response.DataBuffer, 0, controllerData, 0, controllerData.Length);

            return NvmeProbeUtil.HasAnyNonZeroByte(controllerData);
        }

        private static bool TryMiniportIdentifyNamespace(IStorageIoControl ioControl, SafeFileHandle handle, byte[] signature, StorageDevice device, out byte[] namespaceData)
        {
            namespaceData = null;

            var request = INTEL_NVME_PASS_THROUGH.CreateDefault();
            request.Srb.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            request.Srb.Signature = signature;
            request.Srb.Timeout = 10;
            request.Srb.ControlCode = IoControlCodes.IOCTL_INTEL_NVME_PASS_THROUGH;
            request.Srb.Length = (uint)(Marshal.SizeOf<INTEL_NVME_PAYLOAD>() + BufferLength);

            request.Payload.Version = 1;
            request.Payload.PathID = device.Scsi.PathID.HasValue ? device.Scsi.PathID.Value : (byte)0;
            request.Payload.TargetID = device.Scsi.TargetID.HasValue ? device.Scsi.TargetID.Value : (byte)0;
            request.Payload.Lun = device.Scsi.Lun.HasValue ? device.Scsi.Lun.Value : (byte)0;
            request.Payload.ParameterBufferLength = (uint)(Marshal.SizeOf<INTEL_NVME_PAYLOAD>() + Marshal.SizeOf<SRB_IO_CONTROL>());
            request.Payload.ReturnBufferLength = BufferLength;
            request.Payload.Cmd.CDW0 = IntelNvmeConstants.MakeCdw0(NvmeAdminIdentifyOpcode, 0);
            request.Payload.Cmd.NSID = 1;
            request.Payload.Cmd.CDW10 = NvmeIdentifyNamespaceCns;

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiMiniport(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<INTEL_NVME_PASS_THROUGH>(buffer);

            namespaceData = new byte[BufferLength];
            Buffer.BlockCopy(response.DataBuffer, 0, namespaceData, 0, namespaceData.Length);

            return NvmeProbeUtil.HasAnyNonZeroByte(namespaceData);
        }

        private static bool TryMiniportSmart(IStorageIoControl ioControl, SafeFileHandle handle, byte[] signature, StorageDevice device, out byte[] smartLogData)
        {
            smartLogData = null;

            var request = INTEL_NVME_PASS_THROUGH.CreateDefault();
            request.Srb.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            request.Srb.Signature = signature;
            request.Srb.Timeout = 10;
            request.Srb.ControlCode = IoControlCodes.IOCTL_INTEL_NVME_PASS_THROUGH;
            request.Srb.Length = (uint)(Marshal.SizeOf<INTEL_NVME_PAYLOAD>() + BufferLength);

            request.Payload.Version = 1;
            request.Payload.PathID = device.Scsi.PathID.HasValue ? device.Scsi.PathID.Value : (byte)0;
            request.Payload.TargetID = device.Scsi.TargetID.HasValue ? device.Scsi.TargetID.Value : (byte)0;
            request.Payload.Lun = device.Scsi.Lun.HasValue ? device.Scsi.Lun.Value : (byte)0;
            request.Payload.ParameterBufferLength = (uint)(Marshal.SizeOf<INTEL_NVME_PAYLOAD>() + Marshal.SizeOf<SRB_IO_CONTROL>());
            request.Payload.ReturnBufferLength = BufferLength;
            request.Payload.Cmd.CDW0 = IntelNvmeConstants.MakeCdw0(NvmeAdminGetLogPageOpcode, 0);
            request.Payload.Cmd.NSID = 0xFFFFFFFF;
            request.Payload.Cmd.CDW10 = IntelNvmeConstants.MakeGetLogPageCdw10(NvmeSmartHealthLogId, BufferLength);

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiMiniport(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<INTEL_NVME_PASS_THROUGH>(buffer);

            smartLogData = new byte[BufferLength];
            Buffer.BlockCopy(response.DataBuffer, 0, smartLogData, 0, smartLogData.Length);

            return NvmeProbeUtil.HasAnyNonZeroByte(smartLogData);
        }

        private static byte[] GetPrimarySignature(string controllerService)
        {
            if (string.Equals(controllerService, ControllerServiceNames.IaVroc, StringComparison.OrdinalIgnoreCase))
            {
                return IntelRaidMiniportConstants.CreateNvmeRaidSignature();
            }

            if (!string.IsNullOrWhiteSpace(controllerService) && controllerService.StartsWith(ControllerServiceNames.IaStorPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return IntelRaidMiniportConstants.CreateIntelMiniSignature();
            }

            return IntelRaidMiniportConstants.CreateNvmeMiniSignature();
        }

        private static List<byte[]> GetSignatureAttemptOrder(string controllerService)
        {
            var order = new List<byte[]>();

            AddSignature(order, GetPrimarySignature(controllerService));
            AddSignature(order, IntelRaidMiniportConstants.CreateIntelMiniSignature());
            AddSignature(order, IntelRaidMiniportConstants.CreateNvmeMiniSignature());
            AddSignature(order, IntelRaidMiniportConstants.CreateNvmeRaidSignature());

            return order;
        }

        private static void AddSignature(List<byte[]> list, byte[] signature)
        {
            if (signature == null)
            {
                return;
            }

            for (int i = 0; i < list.Count; ++i)
            {
                if (SignaturesEqual(list[i], signature))
                {
                    return;
                }
            }

            list.Add(signature);
        }

        private static bool SignaturesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; ++i)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
