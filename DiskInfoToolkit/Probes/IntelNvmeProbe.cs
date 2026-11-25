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
    public static class IntelNvmeProbe
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

        public static bool TryPopulateIntelNvmeData(StorageDevice device, IStorageIoControl ioControl)
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
                byte[] namespaceData = null;
                byte[] smartLogData = null;

                bool identifyOk = TryIdentify(ioControl, handle, device, out identifyData);
                if (!identifyOk && ControllerServiceProbeRules.ShouldStopAfterSevereIoError(device, ioControl, "Intel NVMe path", "controller identify", stopOnOpenTimeout: true))
                {
                    return false;
                }

                bool namespaceOk = TryIdentifyNamespace(ioControl, handle, device, out namespaceData);
                bool abortedAfterNamespace = !namespaceOk && ControllerServiceProbeRules.ShouldStopAfterSevereIoError(device, ioControl, "Intel NVMe path", "namespace identify", stopOnOpenTimeout: true);
                if (abortedAfterNamespace && !identifyOk)
                {
                    return false;
                }

                bool smartOk = !abortedAfterNamespace && TrySmartLog(ioControl, handle, device, out smartLogData);

                if (identifyOk)
                {
                    device.Nvme.IntelIdentifyControllerData = identifyData;
                    device.Nvme.IdentifyControllerData = identifyData;

                    IntelNvmeProbeUtil.ApplyIdentifyControllerStrings(device, identifyData);

                    device.BusType = StorageBusType.Nvme;
                    device.TransportKind = StorageTransportKind.Nvme;

                    if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                    {
                        device.Controller.Kind = ControllerKindNames.NvmePci;
                    }
                }

                if (namespaceOk)
                {
                    device.Nvme.IdentifyNamespaceData = namespaceData;

                    NvmeNamespaceParser.ApplyNamespaceData(device, namespaceData);

                    device.BusType = StorageBusType.Nvme;
                    device.TransportKind = StorageTransportKind.Nvme;
                }

                if (smartOk)
                {
                    device.Nvme.IntelSmartLogData = smartLogData;
                    device.Nvme.SmartLogData = smartLogData;

                    NvmeSmartLogParser.ApplySmartLog(device, smartLogData);

                    device.SupportsSmart = true;
                    device.BusType = StorageBusType.Nvme;
                    device.TransportKind = StorageTransportKind.Nvme;
                }

                return identifyOk || namespaceOk || smartOk;
            }
        }

        public static bool TryRefreshIntelNvmeSmartLog(StorageDevice device, IStorageIoControl ioControl)
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
                if (!TrySmartLog(ioControl, handle, device, out var smartLogData))
                {
                    return false;
                }

                device.Nvme.IntelSmartLogData = smartLogData;
                device.Nvme.SmartLogData = smartLogData;

                NvmeSmartLogParser.ApplySmartLog(device, smartLogData);

                device.SupportsSmart = true;
                device.BusType = StorageBusType.Nvme;
                device.TransportKind = StorageTransportKind.Nvme;

                return true;
            }
        }

        #endregion

        #region Private

        private static bool TryIdentify(IStorageIoControl ioControl, SafeFileHandle handle, StorageDevice device, out byte[] controllerData)
        {
            return TryExecutePassThroughCommand(
                ioControl,
                handle,
                device,
                IntelNvmeConstants.MakeCdw0(NvmeAdminIdentifyOpcode, 0),
                0,
                NvmeIdentifyControllerCns,
                out controllerData);
        }

        private static bool TryIdentifyNamespace(IStorageIoControl ioControl, SafeFileHandle handle, StorageDevice device, out byte[] namespaceData)
        {
            return TryExecutePassThroughCommand(
                ioControl,
                handle,
                device,
                IntelNvmeConstants.MakeCdw0(NvmeAdminIdentifyOpcode, 0),
                1,
                NvmeIdentifyNamespaceCns,
                out namespaceData);
        }

        private static bool TrySmartLog(IStorageIoControl ioControl, SafeFileHandle handle, StorageDevice device, out byte[] smartLogData)
        {
            return TryExecutePassThroughCommand(
                ioControl,
                handle,
                device,
                IntelNvmeConstants.MakeCdw0(NvmeAdminGetLogPageOpcode, 0),
                0xFFFFFFFF,
                IntelNvmeConstants.MakeGetLogPageCdw10(NvmeSmartHealthLogId, BufferLength),
                out smartLogData);
        }

        private static bool TryExecutePassThroughCommand(
            IStorageIoControl ioControl,
            SafeFileHandle handle,
            StorageDevice device,
            uint cdw0,
            uint nsid,
            uint cdw10,
            out byte[] dataBuffer)
        {
            dataBuffer = null;

            var request = CreateBaseRequest(device);
            request.Payload.Cmd.CDW0 = cdw0;
            request.Payload.Cmd.NSID = nsid;
            request.Payload.Cmd.CDW10 = cdw10;

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryIntelNvmePassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<INTEL_NVME_PASS_THROUGH>(buffer);

            dataBuffer = new byte[BufferLength];
            Buffer.BlockCopy(response.DataBuffer, 0, dataBuffer, 0, dataBuffer.Length);

            return NvmeProbeUtil.HasAnyNonZeroByte(dataBuffer);
        }

        private static INTEL_NVME_PASS_THROUGH CreateBaseRequest(StorageDevice device)
        {
            var request = INTEL_NVME_PASS_THROUGH.CreateDefault();
            request.Srb.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            request.Srb.Signature = IntelNvmeConstants.CreateIntelSignature();
            request.Srb.Timeout = 10;
            request.Srb.ControlCode = IoControlCodes.IOCTL_INTEL_NVME_PASS_THROUGH;
            request.Srb.Length = (uint)(Marshal.SizeOf<INTEL_NVME_PAYLOAD>() + BufferLength);
            request.Payload.Version = 1;
            request.Payload.PathID = device.Scsi.PathID.HasValue ? device.Scsi.PathID.Value : (byte)0;
            request.Payload.TargetID = device.Scsi.TargetID.HasValue ? device.Scsi.TargetID.Value : (byte)0;
            request.Payload.Lun = device.Scsi.Lun.HasValue ? device.Scsi.Lun.Value : (byte)0;
            request.Payload.ParameterBufferLength = (uint)(Marshal.SizeOf<INTEL_NVME_PAYLOAD>() + Marshal.SizeOf<SRB_IO_CONTROL>());
            request.Payload.ReturnBufferLength = BufferLength;

            return request;
        }

        #endregion
    }
}
