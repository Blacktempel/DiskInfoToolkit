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
    /// Verifies volume-to-space association with synthetic disk numbers and volume extents.
    /// </summary>
    [TestClass]
    public sealed class WindowsStorageSpaceVolumeReaderTest
    {
        #region Public

        /// <summary>
        /// Associates multiple spaces independently when GetDisks contains no virtual disk.
        /// </summary>
        [TestMethod]
        public void PopulatesSpacesWithoutAggregateDisks()
        {
            var poolID  = new Guid("00000000-0000-0000-0000-000000000001");
            var firstID = new Guid("00000000-0000-0000-0000-000000000002");
            var secondID = new Guid("00000000-0000-0000-0000-000000000003");

            var firstSpace  = new StorageSpace(poolID, firstID , "First space" , string.Empty, 1000);
            var secondSpace = new StorageSpace(poolID, secondID, "Second space", string.Empty, 1000);

            var pool = new StoragePool(poolID, "Test pool", string.Empty, false, 2000, 1000, 3, 3);
            pool.SetSpaces(new List<StorageSpace> { firstSpace, secondSpace });

            var diskNumbersBySpaceID = new Dictionary<Guid, uint>
            {
                [firstID] = 11,
                [secondID] = 12
            };

            var volumeDiskNumbers = new Dictionary<string, IReadOnlyList<uint>>
            {
                ["synthetic-volume-a"] = new uint[] { 11 },
                ["synthetic-volume-b"] = new uint[] { 11, 11 },
                ["synthetic-volume-c"] = new uint[] { 12 },
                ["spanning-volume"] = new uint[] { 11, 12 },
                ["unrelated-volume"] = new uint[] { 20 }
            };

            var queriedPaths = new List<string>();
            WindowsStorageSpaceVolumeReader.PopulateFromVolumeExtents(
                new[] { pool }, diskNumbersBySpaceID, volumeDiskNumbers, path =>
            {
                queriedPaths.Add(path);
                return path switch
                {
                    "synthetic-volume-a" => (400UL, 300UL),
                    "synthetic-volume-b" => (200UL, 50UL),
                    "synthetic-volume-c" => (500UL, 125UL),
                    _ => throw new AssertFailedException("An unrelated or spanning volume was queried.")
                };
            });

            CollectionAssert.AreEquivalent(
                new[] { "synthetic-volume-a", "synthetic-volume-b", "synthetic-volume-c" }, queriedPaths);

            Assert.AreEqual((ulong)600, firstSpace.MountedVolumeSizeBytes);
            Assert.AreEqual((ulong)350, firstSpace.MountedVolumeFreeBytes);
            Assert.AreEqual((ulong)250, firstSpace.MountedVolumeUsedBytes);
            Assert.AreEqual(2, firstSpace.MountedVolumeCount);

            Assert.AreEqual((ulong)500, secondSpace.MountedVolumeSizeBytes);
            Assert.AreEqual((ulong)125, secondSpace.MountedVolumeFreeBytes);
            Assert.AreEqual((ulong)375, secondSpace.MountedVolumeUsedBytes);
            Assert.AreEqual(1, secondSpace.MountedVolumeCount);
        }

        /// <summary>
        /// Leaves capacity unknown when two spaces claim the same temporary disk number.
        /// </summary>
        [TestMethod]
        public void DoesNotAssignAmbiguousDiskNumber()
        {
            var poolID = new Guid("00000000-0000-0000-0000-000000000011");

            var firstSpace = new StorageSpace(poolID , new Guid("00000000-0000-0000-0000-000000000012"), "First" , string.Empty, 1000);
            var secondSpace = new StorageSpace(poolID, new Guid("00000000-0000-0000-0000-000000000013"), "Second", string.Empty, 1000);

            var pool = new StoragePool(poolID, "Test pool", string.Empty, false, 2000, 1000, 2, 2);
            pool.SetSpaces(new List<StorageSpace> { firstSpace, secondSpace });

            WindowsStorageSpaceVolumeReader.PopulateFromVolumeExtents(
                new[] { pool },
                new Dictionary<Guid, uint> { [firstSpace.ID] = 11, [secondSpace.ID] = 11 },
                new Dictionary<string, IReadOnlyList<uint>> { ["synthetic-volume"] = new uint[] { 11 } },
                _ => throw new AssertFailedException("An ambiguous volume was queried."));

            Assert.IsNull(firstSpace.MountedVolumeSizeBytes);
            Assert.IsNull(secondSpace.MountedVolumeSizeBytes);
        }

        #endregion
    }
}
