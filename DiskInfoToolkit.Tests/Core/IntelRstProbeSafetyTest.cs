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
using DiskInfoToolkit.Models;
using DiskInfoToolkit.Probes;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.Tests.Core
{
    [TestClass]
    public class IntelRstProbeSafetyTest
    {
        #region Public

        [TestMethod]
        public void IntelRstDeviceNeverSendsStorageDeviceProtocolSpecificProperty()
        {
            var raidDevice = CreateIntelRstDevice(ProbeStrategy.RaidProbe, StorageControllerFamily.IntelRst);
            var raidIo = new RecordingStorageIoControl();

            StorageProbeDispatcher.Probe(raidDevice, raidIo);

            Assert.IsFalse(raidIo.QueriedPropertyIds.Contains(50));
            Assert.IsTrue(raidIo.QueriedPropertyIds.Contains(49));
            Assert.IsTrue(raidDevice.ProbeTrace.Any(e => e.Contains("skipping StorageDeviceProtocolSpecificProperty (50)", StringComparison.Ordinal)));

            var nvmeDevice = CreateIntelRstDevice(ProbeStrategy.PciNvmeProbe, StorageControllerFamily.IntelVroc);
            var nvmeIo = new RecordingStorageIoControl();

            StorageProbeDispatcher.Probe(nvmeDevice, nvmeIo);

            Assert.IsFalse(nvmeIo.QueriedPropertyIds.Contains(50));
            Assert.IsTrue(nvmeIo.QueriedPropertyIds.Contains(49));

            var refreshIo = new RecordingStorageIoControl();
            bool refreshed = NvmeProbe.TryRefreshStandardNvmeSmartLog(raidDevice, refreshIo);

            Assert.IsFalse(refreshed);
            Assert.IsFalse(refreshIo.QueriedPropertyIds.Contains(50));
            Assert.IsTrue(refreshIo.QueriedPropertyIds.Contains(49));
        }

        [TestMethod]
        public void IntelRstServiceWithoutClassifiedFamilyStillBlocksProperty50()
        {
            var device = CreateIntelRstDevice(ProbeStrategy.PciNvmeProbe, StorageControllerFamily.Unknown);
            var io = new RecordingStorageIoControl();

            bool populated = NvmeProbe.TryPopulateStandardNvmeData(device, io);

            Assert.IsFalse(populated);
            Assert.IsFalse(io.QueriedPropertyIds.Contains(50));
            Assert.IsTrue(io.QueriedPropertyIds.Contains(49));
        }

        [TestMethod]
        public void StandardNvmeDeviceStillUsesStorageDeviceProtocolSpecificPropertyFallback()
        {
            var device = new StorageDevice
            {
                DevicePath = @"\\.\PhysicalDrive1",
                DisplayName = "NVMe Standard SSD",
                BusType = StorageBusType.Nvme,
                ProbeStrategy = ProbeStrategy.PciNvmeProbe
            };
            device.Controller.Service = ControllerServiceNames.StorNvme;
            device.Controller.Family = StorageControllerFamily.StorNvme;

            var io = new RecordingStorageIoControl
            {
                RespondToDeviceProtocolProperty = true
            };

            StorageProbeDispatcher.Probe(device, io);

            Assert.IsTrue(io.QueriedPropertyIds.Contains(49));
            Assert.IsTrue(io.QueriedPropertyIds.Contains(50));
            Assert.IsTrue(device.SupportsSmart);
            Assert.AreEqual(StorageTransportKind.Nvme, device.TransportKind);
        }

        [TestMethod]
        public void IntelNvmeProbeSetsCorrectSignatureControlCodeAndParameterLength()
        {
            var device = CreateIntelRstDevice(ProbeStrategy.RaidProbe, StorageControllerFamily.IntelRst);
            device.Scsi.PathID = 0;
            device.Scsi.TargetID = 1;
            device.Scsi.Lun = 0;

            var io = new RecordingStorageIoControl
            {
                RespondToIntelNvmePassThrough = true
            };

            bool ok = IntelNvmeProbe.TryPopulateIntelNvmeData(device, io);

            Assert.IsTrue(ok);
            Assert.IsGreaterThanOrEqualTo(1, io.CapturedIntelNvmeRequests.Count);

            INTEL_NVME_PASS_THROUGH request = io.CapturedIntelNvmeRequests[0];
            string signature = Encoding.ASCII.GetString(request.Srb.Signature).TrimEnd('\0');
            uint expectedParameterLength = (uint)(Marshal.SizeOf<INTEL_NVME_PAYLOAD>() + Marshal.SizeOf<SRB_IO_CONTROL>());

            Assert.AreEqual("IntelNvm", signature);
            Assert.AreEqual(IoControlCodes.IOCTL_INTEL_NVME_PASS_THROUGH, request.Srb.ControlCode);
            Assert.AreEqual(expectedParameterLength, request.Payload.ParameterBufferLength);
            Assert.AreEqual(4096u, request.Payload.ReturnBufferLength);
            Assert.IsTrue(device.SupportsSmart);
        }

        [TestMethod]
        public void SevereDeviceIoErrorStopsIntelRaidAndNvmeFallbacksImmediately()
        {
            var intelDevice = CreateIntelRstDevice(ProbeStrategy.RaidProbe, StorageControllerFamily.IntelRst);
            var severeRaidIo = new RecordingStorageIoControl
            {
                TriggerSevereErrorAfterFirstIo = true
            };

            StorageProbeDispatcher.Probe(intelDevice, severeRaidIo);

            Assert.AreEqual(1, severeRaidIo.ScsiMiniportCount);
            Assert.IsEmpty(severeRaidIo.QueriedPropertyIds);
            Assert.AreEqual(0, severeRaidIo.ScsiPassThroughCount);
            Assert.IsTrue(intelDevice.ProbeTrace.Any(e => e.Contains("stopping further probing after severe device I/O error", StringComparison.Ordinal)));

            var nvmeDevice = new StorageDevice
            {
                DevicePath = @"\\.\PhysicalDrive1",
                ProbeStrategy = ProbeStrategy.PciNvmeProbe
            };
            nvmeDevice.Controller.Service = ControllerServiceNames.StorNvme;
            nvmeDevice.Controller.Family = StorageControllerFamily.StorNvme;

            var severeNvmeIo = new RecordingStorageIoControl
            {
                TriggerSevereErrorAfterFirstIo = true
            };

            bool nvmeOk = NvmeProbe.TryPopulateStandardNvmeData(nvmeDevice, severeNvmeIo);

            Assert.IsFalse(nvmeOk);
            Assert.HasCount(1, severeNvmeIo.QueriedPropertyIds);
            Assert.AreEqual(49, severeNvmeIo.QueriedPropertyIds[0]);
            Assert.IsTrue(nvmeDevice.ProbeTrace.Any(e => e.Contains("stopping further probing after severe device I/O error", StringComparison.Ordinal)));
        }

        #endregion

        #region Private

        private static StorageDevice CreateIntelRstDevice(ProbeStrategy strategy, StorageControllerFamily family)
        {
            var device = new StorageDevice
            {
                DevicePath = @"\\.\PhysicalDrive0",
                DisplayName = "NVMe CT1000T500SSD8",
                ProbeStrategy = strategy
            };
            device.Controller.Service = ControllerServiceNames.IaStorVD;
            device.Controller.Family = family;
            device.Scsi.PortNumber = 0;

            return device;
        }

        private sealed class RecordingStorageIoControl : IStorageIoControl, IStorageIoControlErrorState
        {
            public List<int> QueriedPropertyIds { get; } = new();

            public List<INTEL_NVME_PASS_THROUGH> CapturedIntelNvmeRequests { get; } = new();

            public int ScsiMiniportCount { get; private set; }

            public int ScsiPassThroughCount { get; private set; }

            public bool RespondToDeviceProtocolProperty { get; set; }

            public bool RespondToIntelNvmePassThrough { get; set; }

            public bool TriggerSevereErrorAfterFirstIo { get; set; }

            public uint LastIoControlCode { get; private set; }

            public int LastIoControlError { get; private set; }

            public bool LastOpenDeviceTimedOut { get; private set; }

            public bool LastIoControlWasSevereDeviceError { get; private set; }

            public SafeFileHandle OpenDevice(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes)
            {
                return new SafeFileHandle(new IntPtr(1), false);
            }

            public bool SendRawIoControl(SafeFileHandle handle, uint ioControlCode, byte[] inBuffer, byte[] outBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                LastIoControlCode = ioControlCode;

                if (ioControlCode == IoControlCodes.IOCTL_STORAGE_QUERY_PROPERTY && inBuffer != null && inBuffer.Length >= 4)
                {
                    int propertyId = BitConverter.ToInt32(inBuffer, 0);
                    QueriedPropertyIds.Add(propertyId);

                    if (RespondToDeviceProtocolProperty && propertyId == 50 && outBuffer != null)
                    {
                        int payloadOffset = Marshal.SizeOf<STORAGE_PROPERTY_QUERY_NVME>() + Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();
                        if (outBuffer.Length > payloadOffset + 4)
                        {
                            outBuffer[payloadOffset] = 0x01;
                            outBuffer[payloadOffset + 1] = 0x36;
                            outBuffer[payloadOffset + 2] = 0x01;
                            bytesReturned = outBuffer.Length;
                            return true;
                        }
                    }
                }

                if (TriggerSevereErrorAfterFirstIo)
                {
                    LastIoControlError = 1117;
                    LastIoControlWasSevereDeviceError = true;
                }

                return false;
            }

            public bool TryGetStorageDeviceDescriptor(SafeFileHandle handle, out StorageDeviceDescriptorInfo descriptor)
            {
                descriptor = new StorageDeviceDescriptorInfo();
                return false;
            }

            public bool TryGetStorageAdapterDescriptor(SafeFileHandle handle, out StorageAdapterDescriptorInfo descriptor)
            {
                descriptor = new StorageAdapterDescriptorInfo();
                return false;
            }

            public bool TryGetDriveLayout(SafeFileHandle handle, out byte[] rawLayout)
            {
                rawLayout = null;
                return false;
            }

            public bool TryGetScsiAddress(SafeFileHandle handle, out ScsiAddressInfo scsiAddress)
            {
                scsiAddress = new ScsiAddressInfo();
                return false;
            }

            public bool TryGetStorageDeviceNumber(SafeFileHandle handle, out StorageDeviceNumberInfo info)
            {
                info = new StorageDeviceNumberInfo();
                return false;
            }

            public bool TryGetDriveGeometryEx(SafeFileHandle handle, out DiskGeometryInfo info)
            {
                info = new DiskGeometryInfo();
                return false;
            }

            public bool TryGetPredictFailure(SafeFileHandle handle, out PredictFailureInfo info)
            {
                info = new PredictFailureInfo();
                return false;
            }

            public bool TryGetSffDiskDeviceProtocol(SafeFileHandle handle, out StorageProtocolType protocolType)
            {
                protocolType = StorageProtocolType.Unknown;
                return false;
            }

            public bool TryGetSmartVersion(SafeFileHandle handle, out SmartVersionInfo info)
            {
                info = new SmartVersionInfo();
                return false;
            }

            public bool TryScsiPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++ScsiPassThroughCount;
                bytesReturned = 0;
                return false;
            }

            public bool TryScsiMiniport(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++ScsiMiniportCount;
                bytesReturned = 0;
                LastIoControlCode = IoControlCodes.IOCTL_SCSI_MINIPORT;

                if (TriggerSevereErrorAfterFirstIo)
                {
                    LastIoControlError = 1117;
                    LastIoControlWasSevereDeviceError = true;
                }

                return false;
            }

            public bool TryAtaPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TryIdePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TrySmartReceiveDriveData(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TrySmartSendDriveCommand(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TryIntelNvmePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                LastIoControlCode = IoControlCodes.IOCTL_INTEL_NVME_PASS_THROUGH;

                if (requestBuffer != null && requestBuffer.Length >= Marshal.SizeOf<INTEL_NVME_PASS_THROUGH>())
                {
                    INTEL_NVME_PASS_THROUGH parsed = StructureHelper.FromBytes<INTEL_NVME_PASS_THROUGH>(requestBuffer);
                    CapturedIntelNvmeRequests.Add(parsed);

                    if (RespondToIntelNvmePassThrough && responseBuffer != null && responseBuffer.Length >= requestBuffer.Length)
                    {
                        parsed.DataBuffer[0] = 0x01;
                        parsed.DataBuffer[1] = 0x36;
                        parsed.DataBuffer[2] = 0x01;
                        byte[] encoded = StructureHelper.GetBytes(parsed);
                        Buffer.BlockCopy(encoded, 0, responseBuffer, 0, encoded.Length);
                        bytesReturned = encoded.Length;
                        return true;
                    }
                }

                return false;
            }
        }

        #endregion
    }
}
