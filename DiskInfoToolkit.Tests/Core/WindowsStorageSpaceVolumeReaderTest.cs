/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Core;

namespace DiskInfoToolkit.Tests.Core
{
    /// <summary>
    /// Verifies volume-to-space association with synthetic disks and an injected volume reader.
    /// </summary>
    [TestClass]
    public sealed class WindowsStorageSpaceVolumeReaderTest
    {
        #region Public

        /// <summary>
        /// Sums each mounted volume once and never queries the host file system.
        /// </summary>
        [TestMethod]
        public void PopulatesOnlyTheMatchingSpaceFromSyntheticVolumes()
        {
            var poolID  = new Guid("00000000-0000-0000-0000-000000000001");
            var spaceID = new Guid("00000000-0000-0000-0000-000000000002");
            var otherID = new Guid("00000000-0000-0000-0000-000000000003");

            var space      = new StorageSpace(poolID, spaceID, "Test space"      , string.Empty, 1000);
            var otherSpace = new StorageSpace(poolID, otherID, "Other test space", string.Empty, 1000);

            var pool = new StoragePool(poolID, "Test pool", string.Empty, false, 2000, 1000, 1, 3, 3);

            pool.SetSpaces(new List<StorageSpace> { space, otherSpace });

            var disk = new StorageDevice
            {
                BusType = StorageBusType.Spaces,
                SerialNumber = "{" + spaceID.ToString("D") + "}",
                Partitions = new List<StoragePartitionInfo>
                {
                    new() { VolumePath = "synthetic-volume-a" },
                    new() { VolumePath = "synthetic-volume-a" },
                    new() { VolumePath = "synthetic-volume-b" },
                    new() { VolumePath = string.Empty },
                },
            };

            var queriedPaths = new List<string>();
            WindowsStorageSpaceVolumeReader.Populate(new[] { pool }, new[] { disk }, path =>
            {
                queriedPaths.Add(path);
                return path == "synthetic-volume-a" ? (400UL, 300UL) : (200UL, 50UL);
            });

            CollectionAssert.AreEquivalent(new[] { "synthetic-volume-a", "synthetic-volume-b" }, queriedPaths);

            Assert.AreEqual((ulong)600, space.MountedVolumeSizeBytes);
            Assert.AreEqual((ulong)350, space.MountedVolumeFreeBytes);
            Assert.AreEqual((ulong)250, space.MountedVolumeUsedBytes);
            Assert.AreEqual(2, space.MountedVolumeCount);

            Assert.IsNull(otherSpace.MountedVolumeSizeBytes);
        }

        #endregion
    }
}
