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
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Partitions;
using Microsoft.Win32.SafeHandles;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Tests.Core
{
    /// <summary>
    /// Uses only synthetic layout bytes and fake handles; no device is opened.
    /// </summary>
    [TestClass]
    public sealed class PartitionRefreshIsolationTest
    {
        #region Public

        /// <summary>
        /// Retains a complete snapshot after failure and clears it after a valid empty layout.
        /// </summary>
        [TestMethod]
        public void FailedReadRetainsSnapshotButValidEmptyLayoutClearsIt()
        {
            //Arrange
            var previousRead = DateTime.UtcNow.AddMinutes(-10);

            var device = new StorageDevice
            {
                DevicePath            = @"\\?\synthetic-disk",
                PartitionsLastReadUtc = previousRead,
                Partitions            = new List<StoragePartitionInfo>
                {
                    new StoragePartitionInfo { PartitionNumber = 1, StartingOffset = 1048576 }
                }
            };

            var io = DispatchProxy.Create<IStorageIoControl, FakeStorageIoProxy>();
            var fake = (FakeStorageIoProxy)io;

            //Act
            bool failedReadChanged = StoragePartitionReader.PopulatePartitions(device, io);

            //Assert
            Assert.IsTrue(failedReadChanged);
            Assert.IsTrue(device.PartitionsAreStale);

            Assert.AreEqual(1, device.Partitions.Count);
            Assert.AreEqual(previousRead, device.PartitionsLastReadUtc);

            Assert.IsTrue(device.PartitionsLastCheckedUtc.HasValue);

            //Arrange
            fake.Layout = new byte[(int)Marshal.OffsetOf<DRIVE_LAYOUT_INFORMATION_EX_RAW>(
                nameof(DRIVE_LAYOUT_INFORMATION_EX_RAW.PartitionInformation))];

            //Act
            bool emptyReadChanged = StoragePartitionReader.PopulatePartitions(device, io);

            //Assert
            Assert.IsTrue(emptyReadChanged);
            Assert.IsFalse(device.PartitionsAreStale);

            Assert.AreEqual(0, device.Partitions.Count);

            Assert.IsTrue(device.PartitionsLastReadUtc > previousRead);
        }

        /// <summary>
        /// Rejects a layout that claims a partition entry missing from its response.
        /// </summary>
        [TestMethod]
        public void TruncatedLayoutDoesNotReplaceLastCompleteSnapshot()
        {
            //Arrange
            var device = new StorageDevice
            {
                DevicePath = @"\\?\synthetic-disk",
                Partitions = new List<StoragePartitionInfo>
                {
                    new StoragePartitionInfo { PartitionNumber = 1 }
                }
            };

            var io = DispatchProxy.Create<IStorageIoControl, FakeStorageIoProxy>();
            var fake = (FakeStorageIoProxy)io;

            fake.Layout = new byte[(int)Marshal.OffsetOf<DRIVE_LAYOUT_INFORMATION_EX_RAW>(
                nameof(DRIVE_LAYOUT_INFORMATION_EX_RAW.PartitionInformation))];

            BitConverter.GetBytes(1U).CopyTo(fake.Layout, sizeof(uint));

            //Act
            bool changed = StoragePartitionReader.PopulatePartitions(device, io);

            //Assert
            Assert.IsTrue(changed);
            Assert.IsTrue(device.PartitionsAreStale);

            Assert.AreEqual(1, device.Partitions.Count);
        }

        /// <summary>
        /// Accepts the current layout when a removed drive letter no longer opens.
        /// </summary>
        [TestMethod]
        public void RemovedDriveLetterDoesNotKeepValidLayoutStale()
        {
            //Arrange
            const long PartitionOffset = 1048576;

            var device = new StorageDevice
            {
                DevicePath          = @"\\?\synthetic-disk",
                StorageDeviceNumber = 7,
                Partitions          = new List<StoragePartitionInfo>
                {
                    new StoragePartitionInfo
                    {
                        PartitionNumber = 1,
                        StartingOffset  = PartitionOffset,
                        DriveLetter     = 'D'
                    }
                }
            };

            var io = DispatchProxy.Create<IStorageIoControl, FakeStorageIoProxy>();

            var fake = (FakeStorageIoProxy)io;
            fake.MissingVolumes = true;

            int firstPartition = (int)Marshal.OffsetOf<DRIVE_LAYOUT_INFORMATION_EX_RAW>(
                nameof(DRIVE_LAYOUT_INFORMATION_EX_RAW.PartitionInformation));

            fake.Layout = new byte[firstPartition + Marshal.SizeOf<PARTITION_INFORMATION_EX_RAW>()];

            BitConverter.GetBytes(1U).CopyTo(fake.Layout, sizeof(uint));
            BitConverter.GetBytes(2).CopyTo(fake.Layout, firstPartition);
            BitConverter.GetBytes(PartitionOffset).CopyTo(fake.Layout, firstPartition + 8);
            BitConverter.GetBytes(4096L).CopyTo(fake.Layout, firstPartition + 16);
            BitConverter.GetBytes(1U).CopyTo(fake.Layout, firstPartition + 24);

            //Act
            bool changed = StoragePartitionReader.PopulatePartitions(device, io);

            //Assert
            Assert.IsTrue(changed);
            Assert.IsFalse(device.PartitionsAreStale);

            Assert.AreEqual(1, device.Partitions.Count);

            Assert.IsNull(device.Partitions[0].DriveLetter);
        }

        /// <summary>
        /// Reads a non-USB disk layout without opening unrelated volumes when USB exclusion is active.
        /// </summary>
        [TestMethod]
        public void PartitionRefreshCanSkipVolumeQueries()
        {
            const long PartitionOffset = 1048576;

            var device = new StorageDevice
            {
                DevicePath = @"\\?\synthetic-disk",
                StorageDeviceNumber = 7
            };

            var io = DispatchProxy.Create<IStorageIoControl, FakeStorageIoProxy>();
            var fake = (FakeStorageIoProxy)io;

            int firstPartition = (int)Marshal.OffsetOf<DRIVE_LAYOUT_INFORMATION_EX_RAW>(
                nameof(DRIVE_LAYOUT_INFORMATION_EX_RAW.PartitionInformation));

            fake.Layout = new byte[firstPartition + Marshal.SizeOf<PARTITION_INFORMATION_EX_RAW>()];

            BitConverter.GetBytes(1U             ).CopyTo(fake.Layout, sizeof(uint)       );
            BitConverter.GetBytes(2              ).CopyTo(fake.Layout, firstPartition     );
            BitConverter.GetBytes(PartitionOffset).CopyTo(fake.Layout, firstPartition +  8);
            BitConverter.GetBytes(4096L          ).CopyTo(fake.Layout, firstPartition + 16);
            BitConverter.GetBytes(1U             ).CopyTo(fake.Layout, firstPartition + 24);

            bool changed = StoragePartitionReader.PopulatePartitions(device, io, resolveVolumeInfo: false);

            Assert.IsTrue(changed);

            Assert.AreEqual(1, fake.OpenCount);
            Assert.AreEqual(1, device.Partitions.Count);

            Assert.IsNull(device.Partitions[0].DriveLetter);
        }

        /// <summary>
        /// Reuses a failed volume lookup instead of issuing it again for another disk.
        /// </summary>
        [TestMethod]
        public void FailedVolumeLookupIsAttemptedOnlyOncePerScan()
        {
            //Arrange
            var io = DispatchProxy.Create<IStorageIoControl, FakeStorageIoProxy>();
            var fake = (FakeStorageIoProxy)io;
            var map = new WindowsVolumeExtentMap(io);

            //Act
            bool firstRead  = map.TryGet('D', out _, out _, out _);
            bool secondRead = map.TryGet('D', out _, out _, out _);

            //Assert
            Assert.IsFalse(firstRead);
            Assert.IsFalse(secondRead);

            Assert.AreEqual(1, fake.OpenCount);
            Assert.AreEqual(1, fake.RawIoCount);
        }

        /// <summary>
        /// Carries partitions only across scans with the same verified device identity.
        /// </summary>
        [TestMethod]
        public void TopologyRescanCarriesSnapshotOnlyForSameDeviceIdentity()
        {
            //Arrange
            var old = new StorageDevice
            {
                DeviceInstanceID      = "USB\\same-device",
                StorageDeviceNumber   = 7,
                PartitionsLastReadUtc = DateTime.UtcNow.AddMinutes(-10),
                Partitions            = new List<StoragePartitionInfo>
                {
                    new StoragePartitionInfo { PartitionNumber = 1 }
                }
            };

            var same = new StorageDevice
            {
                DeviceInstanceID    = "USB\\same-device",
                StorageDeviceNumber = 7,
                PartitionsAreStale  = true
            };

            var reusedNumber = new StorageDevice
            {
                DeviceInstanceID    = "USB\\different-device",
                StorageDeviceNumber = 7,
                PartitionsAreStale  = true
            };

            var validEmpty = new StorageDevice
            {
                DeviceInstanceID    = "USB\\same-device",
                StorageDeviceNumber = 7
            };

            //Act
            Storage.PreservePartitionSnapshotsOnFailure(new[] { old }, new[] { same, reusedNumber, validEmpty });

            //Assert
            Assert.AreEqual(1, same.Partitions.Count);
            Assert.AreEqual(old.PartitionsLastReadUtc, same.PartitionsLastReadUtc);

            Assert.AreNotSame(old.Partitions[0], same.Partitions[0]);

            Assert.AreEqual(0, reusedNumber.Partitions.Count);
            Assert.AreEqual(0, validEmpty.Partitions.Count);
        }

        /// <summary>
        /// Groups verified aliases but leaves a volume spanning two disks ungrouped.
        /// </summary>
        [TestMethod]
        public void KnownAliasesShareCooldownIdentityWithoutGroupingSpannedVolume()
        {
            //Arrange
            var io = new WindowsStorageIoControl();

            //Act
            io.RegisterDeviceAliases(new StorageDevice
            {
                DeviceInstanceID    = "USB\\same-device",
                DevicePath          = @"\\?\synthetic-disk",
                AlternateDevicePath = @"\\?\synthetic-alias",
                StorageDeviceNumber = 7
            });

            io.RegisterDeviceAliases(new StorageDevice
            {
                DeviceInstanceID    = "USB\\other-device",
                DevicePath          = @"\\?\other-disk",
                StorageDeviceNumber = 8
            });

            //Assert
            string key = io.GetCooldownKey(@"\\?\synthetic-disk");

            Assert.AreEqual(key, io.GetCooldownKey(@"\\?\synthetic-alias"));
            Assert.AreEqual(key, io.GetCooldownKey(@"\\.\PhysicalDrive7"));

            Assert.AreNotEqual(key, io.GetCooldownKey(@"\\.\PhysicalDrive8"));

            //Act
            io.RegisterVolumeAlias(@"\\.\D:", new[] { new DISK_EXTENT_RAW { DiskNumber = 7 } });

            //Assert
            Assert.AreEqual(key, io.GetCooldownKey(@"\\.\D:"));

            //Act
            io.RegisterVolumeAlias(@"\\.\E:", new[]
            {
                new DISK_EXTENT_RAW { DiskNumber = 7 },
                new DISK_EXTENT_RAW { DiskNumber = 8 }
            });

            //Assert
            Assert.AreNotEqual(key, io.GetCooldownKey(@"\\.\E:"));
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Supplies fake handles and synthetic layout responses without invoking native I/O.
        /// </summary>
        public class FakeStorageIoProxy : DispatchProxy
        {
            #region Properties

            /// <summary>
            /// Gets or sets the synthetic drive layout returned by the fake.
            /// </summary>
            public byte[] Layout { get; set; }

            /// <summary>
            /// Gets the number of fake device-open calls.
            /// </summary>
            public int OpenCount { get; private set; }

            /// <summary>
            /// Gets the number of fake raw control requests.
            /// </summary>
            public int RawIoCount { get; private set; }

            /// <summary>
            /// Gets or sets whether volume paths return invalid fake handles.
            /// </summary>
            public bool MissingVolumes { get; set; }

            #endregion

            #region Protected

            /// <summary>
            /// Answers only the I/O methods needed by these synthetic tests.
            /// </summary>
            /// <param name="targetMethod">The intercepted interface method.</param>
            /// <param name="args">The call arguments, including output slots.</param>
            /// <returns>The fake method result.</returns>
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                switch (targetMethod.Name)
                {
                    case nameof(IStorageIoControl.OpenDevice):
                    {
                        ++OpenCount;

                        if (MissingVolumes && ((string)args[0]).EndsWith(":", StringComparison.Ordinal))
                        {
                            return new SafeFileHandle(Kernel32.InvalidHandle, false);
                        }

                        return new SafeFileHandle(new IntPtr(1), false);
                    }
                    case nameof(IStorageIoControl.TryGetDriveLayout):
                    {
                        args[1] = Layout;
                        return Layout != null;
                    }
                    case nameof(IStorageIoControl.SendRawIoControl):
                    {
                        ++RawIoCount;

                        args[4] = 0;
                        return false;
                    }
                    default:
                        throw new NotSupportedException(targetMethod.Name);
                }
            }

            #endregion
        }

        #endregion
    }
}
