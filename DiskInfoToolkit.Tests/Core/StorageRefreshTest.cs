/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Core;
using DiskInfoToolkit.Models;
using Microsoft.Win32.SafeHandles;

namespace DiskInfoToolkit.Tests.Core
{
    [TestClass]
    public class StorageRefreshTest
    {
        #region Public

        [TestMethod]
        public void RefreshApiContainsSmartDataOverloads()
        {
            Assert.IsNotNull(typeof(Storage).GetMethod(nameof(Storage.Refresh), new[]
            {
                typeof(StorageDevice),
                typeof(bool)
            }));

            Assert.IsNotNull(typeof(Storage).GetMethod(nameof(Storage.Refresh), new[]
            {
                typeof(StorageDevice),
                typeof(bool),
                typeof(bool),
                typeof(bool)
            }));
        }

        [TestMethod]
        public void StandardPropertyRefreshSkipsSmartOperationsWhenDisabled()
        {
            var device = new StorageDevice
            {
                DevicePath = @"\\?\PhysicalDrive42",
                SupportsSmart = true,
                SmartVersionRaw = 0x1234,
                PredictsFailure = false,
                PredictFailureVendorData = new byte[] { 1, 2, 3 }
            };

            var ioControl = new CountingStorageIoControl();

            StorageDetectionEngine.AttachStandardStorageProperties(device, ioControl, false);

            Assert.AreEqual(1, ioControl.OpenDeviceCount);
            Assert.AreEqual(1, ioControl.StorageDeviceDescriptorCount);
            Assert.AreEqual(0, ioControl.SmartVersionCount);
            Assert.AreEqual(0, ioControl.PredictFailureCount);
            Assert.AreEqual("Updated Product", device.ProductName);
            Assert.AreEqual((uint)42, device.StorageDeviceNumber.GetValueOrDefault());
            Assert.AreEqual((ulong)4096, device.DiskSizeBytes.GetValueOrDefault());

            Assert.IsTrue(device.SupportsSmart);

            Assert.AreEqual((uint)0x1234, device.SmartVersionRaw);

            Assert.IsFalse(device.PredictsFailure.GetValueOrDefault());

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, device.PredictFailureVendorData);
        }

        [TestMethod]
        public void StandardPropertyRefreshIncludesSmartOperationsByDefault()
        {
            var device = new StorageDevice
            {
                DevicePath = @"\\?\PhysicalDrive42"
            };

            var ioControl = new CountingStorageIoControl();

            StorageDetectionEngine.AttachStandardStorageProperties(device, ioControl);

            Assert.AreEqual(1, ioControl.SmartVersionCount);
            Assert.AreEqual(1, ioControl.PredictFailureCount);

            Assert.IsTrue(device.SupportsSmart);

            Assert.AreEqual((uint)0x5678, device.SmartVersionRaw);

            Assert.IsTrue(device.PredictsFailure.GetValueOrDefault());

            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, device.PredictFailureVendorData);
        }

        [TestMethod]
        public void ProbeRefreshSkipsSmartOperationsAndPreservesSmartStateWhenDisabled()
        {
            var device = new StorageDevice
            {
                DevicePath = @"\\?\PhysicalDrive42",
                ProbeStrategy = ProbeStrategy.GenericStorageProbe,
                SupportsSmart = true,
                SmartVersionRaw = 0x1234,
                PredictsFailure = false,
                PredictFailureVendorData = new byte[] { 1, 2, 3 },
                SmartAttributeProfile = SmartAttributeProfile.Smart,
                SmartAttributes = new List<SmartAttributeEntry>
                {
                    new SmartAttributeEntry
                    {
                        ID = 0xC2,
                        RawValue = 33,
                        CurrentValue = 67,
                        WorstValue = 65,
                        ThresholdValue = 0
                    }
                }
            };

            StorageProbePlan probePlan = StorageProbePlan.CreateForFullProbe(device);
            probePlan.RecordSuccess(StorageProbeOperation.AtaSmart);
            probePlan.ConsecutiveRefreshFailureCount = 2;
            probePlan.IsInitialized = true;
            device.ProbePlan = probePlan;

            var ioControl = new CountingStorageIoControl();

            StorageProbeDispatcher.Probe(device, ioControl, false);

            Assert.AreEqual(0, ioControl.TotalIoCount);

            Assert.IsTrue(device.SupportsSmart);

            Assert.AreEqual((uint)0x1234, device.SmartVersionRaw);

            Assert.IsFalse(device.PredictsFailure.GetValueOrDefault());

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, device.PredictFailureVendorData);

            Assert.AreEqual(SmartAttributeProfile.Smart, device.SmartAttributeProfile);
            Assert.AreEqual(1, device.SmartAttributes.Count);
            Assert.AreEqual((ulong)33, device.SmartAttributes[0].RawValue);
            Assert.AreEqual(2, device.ProbePlan.ConsecutiveRefreshFailureCount);

            Assert.IsTrue(device.ProbeTrace.Any(entry => entry.Contains("SMART data refresh skipped by caller", StringComparison.Ordinal)));
        }

        #endregion

        #region Nested Types

        private sealed class CountingStorageIoControl : IStorageIoControl
        {
            #region Properties

            public int OpenDeviceCount { get; private set; }

            public int StorageDeviceDescriptorCount { get; private set; }

            public int SmartVersionCount { get; private set; }

            public int PredictFailureCount { get; private set; }

            public int TotalIoCount { get; private set; }

            #endregion

            #region Public

            public SafeFileHandle OpenDevice(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes)
            {
                ++OpenDeviceCount;
                ++TotalIoCount;
                return new SafeFileHandle(new IntPtr(1), false);
            }

            public bool SendRawIoControl(SafeFileHandle handle, uint ioControlCode, byte[] inBuffer, byte[] outBuffer, out int bytesReturned)
            {
                ++TotalIoCount;
                bytesReturned = 0;
                return false;
            }

            public bool TryGetStorageDeviceDescriptor(SafeFileHandle handle, out StorageDeviceDescriptorInfo descriptor)
            {
                ++StorageDeviceDescriptorCount;
                ++TotalIoCount;
                descriptor = new StorageDeviceDescriptorInfo
                {
                    VendorID = "Updated Vendor",
                    ProductID = "Updated Product",
                    ProductRevision = "1.0",
                    SerialNumber = "UPDATED-SERIAL",
                    BusType = StorageBusType.Sata
                };
                return true;
            }

            public bool TryGetStorageAdapterDescriptor(SafeFileHandle handle, out StorageAdapterDescriptorInfo descriptor)
            {
                ++TotalIoCount;
                descriptor = new StorageAdapterDescriptorInfo
                {
                    BusType = StorageBusType.Sata
                };
                return true;
            }

            public bool TryGetDriveLayout(SafeFileHandle handle, out byte[] rawLayout)
            {
                ++TotalIoCount;
                rawLayout = null;
                return false;
            }

            public bool TryGetScsiAddress(SafeFileHandle handle, out ScsiAddressInfo scsiAddress)
            {
                ++TotalIoCount;
                scsiAddress = new ScsiAddressInfo
                {
                    PortNumber = 1,
                    PathID = 2,
                    TargetID = 3,
                    Lun = 4
                };
                return true;
            }

            public bool TryGetStorageDeviceNumber(SafeFileHandle handle, out StorageDeviceNumberInfo info)
            {
                ++TotalIoCount;
                info = new StorageDeviceNumberInfo
                {
                    DeviceNumber = 42
                };
                return true;
            }

            public bool TryGetDriveGeometryEx(SafeFileHandle handle, out DiskGeometryInfo info)
            {
                ++TotalIoCount;
                info = new DiskGeometryInfo
                {
                    DiskSize = 4096
                };
                return true;
            }

            public bool TryGetPredictFailure(SafeFileHandle handle, out PredictFailureInfo info)
            {
                ++PredictFailureCount;
                ++TotalIoCount;
                info = new PredictFailureInfo
                {
                    PredictsFailure = true,
                    VendorSpecificData = new byte[] { 4, 5, 6 }
                };
                return true;
            }

            public bool TryGetSffDiskDeviceProtocol(SafeFileHandle handle, out StorageProtocolType protocolType)
            {
                ++TotalIoCount;
                protocolType = StorageProtocolType.Unknown;
                return false;
            }

            public bool TryGetSmartVersion(SafeFileHandle handle, out SmartVersionInfo info)
            {
                ++SmartVersionCount;
                ++TotalIoCount;
                info = new SmartVersionInfo
                {
                    Capabilities = 0x5678
                };
                return true;
            }

            public bool TryScsiPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++TotalIoCount;
                bytesReturned = 0;
                return false;
            }

            public bool TryScsiMiniport(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++TotalIoCount;
                bytesReturned = 0;
                return false;
            }

            public bool TryAtaPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++TotalIoCount;
                bytesReturned = 0;
                return false;
            }

            public bool TryIdePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++TotalIoCount;
                bytesReturned = 0;
                return false;
            }

            public bool TrySmartReceiveDriveData(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++TotalIoCount;
                bytesReturned = 0;
                return false;
            }

            public bool TrySmartSendDriveCommand(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++TotalIoCount;
                bytesReturned = 0;
                return false;
            }

            public bool TryIntelNvmePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++TotalIoCount;
                bytesReturned = 0;
                return false;
            }

            #endregion
        }

        #endregion
    }
}
