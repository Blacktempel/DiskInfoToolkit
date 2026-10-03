/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using BlackSharp.Core.Interop.Windows.Native;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Monitoring;
using DiskInfoToolkit.Pnp;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Reflection;

namespace DiskInfoToolkit.Tests.Core
{
    [TestClass]
    [DoNotParallelize]
    public sealed class UsbDeviceExclusionTest
    {
        #region Public

        [TestMethod]
        public void UsbIdentificationCoversWindowsAndLinuxTopology()
        {
            var windowsNode = new PnpDiskNode
            {
                DeviceInstanceID = @"SCSI\Disk&Ven_Test",
                ParentInstanceID = @"USBSTOR\Disk&Ven_Test"
            };

            var linuxNode = new PnpDiskNode
            {
                DeviceInstanceID = "/sys/devices/pci0000:00/usb2/2-1/host0/target0:0:0/0:0:0:0/block/sdb"
            };

            var internalNode = new PnpDiskNode
            {
                DeviceInstanceID = @"SCSI\Disk&Ven_Internal",
                ParentInstanceID = @"PCI\VEN_1234"
            };

            var usbServiceNode = new PnpDiskNode
            {
                DeviceInstanceID = @"SCSI\Disk&Ven_External",
                ParentService = "UASPStor"
            };

            Assert.IsTrue(UsbStorageDeviceIdentifier.IsUsbDevice(windowsNode));
            Assert.IsTrue(UsbStorageDeviceIdentifier.IsUsbDevice(linuxNode));
            Assert.IsTrue(UsbStorageDeviceIdentifier.IsUsbDevice(usbServiceNode));
            Assert.IsFalse(UsbStorageDeviceIdentifier.IsUsbDevice(internalNode));

            var usbNvme = new StorageDevice
            {
                BusType = StorageBusType.Usb,
                TransportKind = StorageTransportKind.Nvme
            };

            Assert.IsTrue(UsbStorageDeviceIdentifier.IsUsbDevice(usbNvme));
        }

        [TestMethod]
        public void UsbDiskIsRejectedBeforeOpeningItsDevicePath()
        {
            var ioControl = DispatchProxy.Create<IStorageIoControl, ThrowOnIoProxy>();
            var engine = new StorageDetectionEngine(ioControl);

            var disks = engine.GetDisks(true, () => new List<PnpDiskNode>
            {
                new PnpDiskNode
                {
                    DevicePath = @"\\?\synthetic-usb-disk",
                    IsUsbConnected = true
                }
            });

            Assert.AreEqual(0, disks.Count);
        }

        [TestMethod]
        public void InProgressScanUsesItsCapturedSetting()
        {
            bool previousSetting = Storage.ExcludeUsbDevices;

            try
            {
                Storage.ExcludeUsbDevices = false;

                var ioControl = DispatchProxy.Create<IStorageIoControl, OpenFailIoProxy>();
                var engine = new StorageDetectionEngine(ioControl);

                var disks = engine.GetDisks(false, () =>
                {
                    Storage.ExcludeUsbDevices = true;

                    return new List<PnpDiskNode>
                    {
                        new PnpDiskNode
                        {
                            DevicePath = @"\\?\synthetic-usb-disk",
                            IsUsbConnected = true,
                            ParentService = "USBSTOR"
                        }
                    };
                });

                Assert.AreEqual(1, disks.Count);

                Assert.IsTrue(disks[0].IsUsbConnected);
                Assert.IsTrue(Storage.ExcludeUsbDevices);
            }
            finally
            {
                Storage.ExcludeUsbDevices = previousSetting;
            }
        }

        [TestMethod]
        public void ExcludedUsbDeviceIsNotRefreshedOrWoken()
        {
            bool previousSetting = Storage.ExcludeUsbDevices;

            try
            {
                Storage.ExcludeUsbDevices = true;

                var lastUpdated = DateTime.UtcNow.AddMinutes(-10);

                var device = new StorageDevice
                {
                    DevicePath = @"\\.\does-not-exist",
                    IsUsbConnected = true,
                    TransportKind = StorageTransportKind.Nvme,
                    LastUpdatedUtc = lastUpdated
                };

                Assert.IsFalse(Storage.Refresh(device));
                Assert.IsFalse(Storage.RefreshVolatileData(device));
                Assert.IsFalse(Storage.RefreshPartitions(device));

                Storage.TryWakeUp(device);

                Assert.AreEqual(lastUpdated, device.LastUpdatedUtc);

                Assert.IsFalse(device.PartitionsLastCheckedUtc.HasValue);
            }
            finally
            {
                Storage.ExcludeUsbDevices = previousSetting;
            }
        }

        [TestMethod]
        public void UsbExclusionProducesRemovalAndReadditionEvents()
        {
            var usbDevice = new StorageDevice
            {
                DeviceInstanceID = @"USBSTOR\Disk&Ven_Test",
                IsUsbConnected = true
            };

            var removed = StorageDeviceDiffBuilder.Build(new List<StorageDevice> { usbDevice }, new List<StorageDevice>());
            var added = StorageDeviceDiffBuilder.Build(new List<StorageDevice>(), new List<StorageDevice> { usbDevice });

            Assert.AreEqual(1, removed.Removed.Count);
            Assert.AreEqual(1, added.Added.Count);
        }

        #endregion

        #region Nested Types

        public class ThrowOnIoProxy : DispatchProxy
        {
            #region Protected

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                throw new AssertFailedException("USB storage I/O was attempted: " + targetMethod.Name);
            }

            #endregion
        }

        public class OpenFailIoProxy : DispatchProxy
        {
            #region Protected

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == nameof(IStorageIoControl.OpenDevice))
                {
                    return new SafeFileHandle(Kernel32.InvalidHandle, false);
                }

                throw new AssertFailedException("Unexpected storage I/O: " + targetMethod.Name);
            }

            #endregion
        }

        #endregion
    }
}
