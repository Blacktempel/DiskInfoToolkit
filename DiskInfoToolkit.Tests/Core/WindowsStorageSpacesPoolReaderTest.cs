/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Core;
using System.Text;

namespace DiskInfoToolkit.Tests.Core
{
    /// <summary>
    /// Verifies the observed Spaceport layout and reference-only member resolution.
    /// </summary>
    [TestClass]
    public class WindowsStorageSpacesPoolReaderTest
    {
        #region Public

        /// <summary>
        /// Verifies pool offsets, capacities, primordial flag and the healthy status pair.
        /// </summary>
        [TestMethod]
        public void ParsesPoolInformationAndVerifiedHealthyStatus()
        {
            // Construct the fixed response shape observed on spaceport.sys 10.0.26100.4202.
            var ID = Guid.NewGuid();
            var bytes = new byte[0xBA8];
            PutUInt32(bytes, 0, 0xB10);
            PutUInt32(bytes, 4, (uint)bytes.Length);
            PutGuid(bytes, 8, ID);
            PutText(bytes, 0x18, "Test Pool");
            PutText(bytes, 0x218, "Test description");

            bytes[0xA18] = 1;

            PutUInt32(bytes, 0xA20, 3);
            PutUInt32(bytes, 0xA24, 3);
            PutUInt64(bytes, 0xA38, 2000);
            PutUInt64(bytes, 0xA40, 300);
            PutUInt32(bytes, 0xA50, 2);

            Assert.IsTrue(WindowsStorageSpacesPoolReader.TryParsePoolInfo(bytes, bytes.Length, ID, out var pool));

            Assert.AreEqual(ID, pool.ID);
            Assert.AreEqual("Test Pool", pool.Name);
            Assert.AreEqual("Test description", pool.Description);

            Assert.IsTrue(pool.IsPrimordial);

            Assert.AreEqual((ulong)2000, pool.SizeBytes);
            Assert.AreEqual((ulong)300, pool.AllocatedBytes);
            Assert.AreEqual((ulong)1700, pool.FreeBytes);
            Assert.AreEqual((uint)2, pool.ConfiguredMemberCount);
            Assert.AreEqual(StoragePoolHealthStatus.Healthy, pool.HealthStatus);
            Assert.AreEqual((uint)3, pool.RawStatusA20);
            Assert.AreEqual((uint)3, pool.RawStatusA24);
        }

        /// <summary>
        /// Verifies the warning pair and rejects a status combination not observed in the live test.
        /// </summary>
        [TestMethod]
        public void MapsOnlyVerifiedWarningPair()
        {
            var ID = Guid.NewGuid();
            var bytes = new byte[0xBA8];

            PutUInt32(bytes, 0, 0xB10);
            PutUInt32(bytes, 4, (uint)bytes.Length);
            PutGuid(bytes, 8, ID);
            PutText(bytes, 0x18, "Test Pool");
            PutUInt32(bytes, 0xA20, 2);
            PutUInt32(bytes, 0xA24, 2);

            Assert.IsTrue(WindowsStorageSpacesPoolReader.TryParsePoolInfo(bytes, bytes.Length, ID, out var pool));
            Assert.AreEqual(StoragePoolHealthStatus.Warning, pool.HealthStatus);

            PutUInt32(bytes, 0xA24, 3);
            Assert.IsTrue(WindowsStorageSpacesPoolReader.TryParsePoolInfo(bytes, bytes.Length, ID, out pool));
            Assert.AreEqual(StoragePoolHealthStatus.Unknown, pool.HealthStatus);
        }

        /// <summary>
        /// Rejects responses that cannot safely be decoded using the observed offsets.
        /// </summary>
        [TestMethod]
        public void RejectsUnexpectedPoolLayoutAndTruncatedList()
        {
            var ID = Guid.NewGuid();
            var bytes = new byte[0xBA8];
            PutUInt32(bytes, 0, 0xB10);
            PutUInt32(bytes, 4, (uint)bytes.Length);
            PutGuid(bytes, 8, ID);
            PutText(bytes, 0x18, "Test Pool");

            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParsePoolInfo(bytes, 0xA40, ID, out _));
            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParsePoolInfo(bytes, bytes.Length, Guid.NewGuid(), out _));

            PutUInt32(bytes, 0, 0xB14);
            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParsePoolInfo(bytes, bytes.Length, ID, out _));

            var list = new byte[20];
            PutUInt32(list, 0, 2);
            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryReadGuidList(list, list.Length, out _));
        }

        /// <summary>
        /// Matches only a unique object from the caller's list and preserves its reference identity.
        /// </summary>
        [TestMethod]
        public void ResolvesOnlyUniqueExistingDiskReferences()
        {
            var member = new StorageDevice { SerialNumber = " SOME-SERIAL-0001 " };
            var other = new StorageDevice { SerialNumber = "OTHER" };
            var disks = new[] { other, member };

            Assert.AreSame(member, WindowsStorageSpacesPoolReader.FindUniqueDiskBySerial(disks, "SOME-SERIAL-0001"));
            Assert.IsNull(WindowsStorageSpacesPoolReader.FindUniqueDiskBySerial(
                new[] { member, new StorageDevice { SerialNumber = member.SerialNumber } }, "SOME-SERIAL-0001"));
        }

        /// <summary>
        /// Reads the complete fixed-width serial and rejects a different member identifier.
        /// </summary>
        [TestMethod]
        public void ReadsFixedLengthSerialFromDriverDiskInformation()
        {
            var poolID = Guid.NewGuid();
            var diskID = Guid.NewGuid();
            var bytes = new byte[0xD30];

            PutUInt32(bytes, 0, 0xC30);
            PutUInt32(bytes, 4, (uint)bytes.Length);
            PutGuid(bytes, 8, poolID);
            PutGuid(bytes, 24, diskID);
            PutText(bytes, 0xBFC, "SOME-SERIAL-0001");

            Assert.IsTrue(WindowsStorageSpacesPoolReader.TryParseDiskSerial(bytes, bytes.Length, poolID, diskID, out var serial));
            Assert.AreEqual("SOME-SERIAL-0001", serial);
            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParseDiskSerial(bytes, bytes.Length, poolID, Guid.NewGuid(), out _));
        }

        /// <summary>
        /// Preserves two distinct storage spaces under the same pool and validates their GUIDs.
        /// </summary>
        [TestMethod]
        public void ParsesMultipleStorageSpacesForOnePool()
        {
            var poolID = Guid.NewGuid();
            var spaceIDs = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var list = new byte[4 + spaceIDs.Length * 16];

            PutUInt32(list, 0, (uint)spaceIDs.Length);

            for (int i = 0; i < spaceIDs.Length; ++i)
            {
                PutGuid(list, 4 + i * 16, spaceIDs[i]);
            }

            Assert.IsTrue(WindowsStorageSpacesPoolReader.TryReadGuidList(list, list.Length, out var parsedIDs));

            var spaces = new List<StorageSpace>();

            for (int i = 0; i < parsedIDs.Count; ++i)
            {
                // The fixed 0xBD8-byte structure can be followed by variable data.
                var bytes = new byte[0x10D8];
                PutUInt32(bytes, 0, 0xBD8);
                PutUInt32(bytes, 4, (uint)bytes.Length);
                PutGuid(bytes, 8, poolID);
                PutGuid(bytes, 0x18, parsedIDs[i]);
                PutText(bytes, 0x28, $"Test Space {i + 1} 12345");
                PutText(bytes, 0x228, "Test description");
                PutUInt64(bytes, 0xA68, (ulong)(i + 1) * 1000);
                PutUInt64(bytes, 0xA70, (ulong)(i + 1) *  100);
                PutUInt64(bytes, 0xA78, (ulong)(i + 1) *  200);

                Assert.IsTrue (WindowsStorageSpacesPoolReader.TryParseSpaceInfo(bytes, bytes.Length, poolID, parsedIDs[i]  , out var space));
                Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParseSpaceInfo(bytes, bytes.Length, poolID, Guid.NewGuid(), out _));
                Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParseSpaceInfo(bytes, 0xBD7       , poolID, parsedIDs[i]  , out _));
                spaces.Add(space);
            }

            var pool = new StoragePool(poolID, "Test Pool", string.Empty, false, 5000, 3000, 2, 3, 3);
            pool.SetSpaces(spaces);

            Assert.IsTrue(pool.SpaceInformationAvailable);

            Assert.AreEqual(2, pool.Spaces.Count);
            Assert.AreEqual(spaceIDs[0], pool.Spaces[0].ID);
            Assert.AreEqual(spaceIDs[1], pool.Spaces[1].ID);
            Assert.AreEqual(poolID, pool.Spaces[0].PoolID);
            Assert.AreEqual("Test Space 2 12345", pool.Spaces[1].Name);
            Assert.AreEqual("Test description", pool.Spaces[1].Description);
            Assert.AreEqual((ulong)2000, pool.Spaces[1].SizeBytes);
            Assert.AreEqual((ulong) 200, pool.Spaces[1].AllocatedBytes);
            Assert.AreEqual((ulong) 400, pool.Spaces[1].FootprintOnPoolBytes);
        }

        /// <summary>
        /// Reads distinct physical disk GUIDs from space extents and keeps existing disk references.
        /// </summary>
        [TestMethod]
        public void ParsesSpaceExtentDisksWithoutCreatingDiskObjects()
        {
            var poolID = Guid.NewGuid();
            var spaceID = Guid.NewGuid();
            var firstID = Guid.NewGuid();
            var secondID = Guid.NewGuid();
            var bytes = new byte[0xD00];

            PutUInt32(bytes, 0, 0xBD8);
            PutUInt32(bytes, 4, (uint)bytes.Length);
            PutGuid(bytes, 8, poolID);
            PutGuid(bytes, 0x18, spaceID);
            PutText(bytes, 0x28, "Space with extents");
            PutUInt32(bytes, 0xB30, 3);
            PutGuid(bytes, 0xB48 + 0x64, firstID);
            PutGuid(bytes, 0xB48 + 0x90 + 0x64, secondID);
            PutGuid(bytes, 0xB48 + 2 * 0x90 + 0x64, firstID);

            Assert.IsTrue(WindowsStorageSpacesPoolReader.TryParseSpaceInfo(bytes, bytes.Length, poolID, spaceID, out var space));
            CollectionAssert.AreEqual(new[] { firstID, secondID }, space.ExtentDiskIDs.ToArray());
            Assert.IsTrue(space.ExtentInformationAvailable);

            var firstDisk = new StorageDevice();
            var secondDisk = new StorageDevice();

            space.SetExtentDisks(new List<Guid>(space.ExtentDiskIDs), new List<StorageDevice> { firstDisk, secondDisk });

            Assert.AreSame(firstDisk, space.ExtentDisks[0]);
            Assert.AreSame(secondDisk, space.ExtentDisks[1]);

            // The declared count must fit in the actual response, even when a large buffer exists.
            PutUInt32(bytes, 0xB30, 5);
            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParseSpaceInfo(bytes, bytes.Length, poolID, spaceID, out _));
        }

        /// <summary>
        /// Accepts only a running repair task with the observed type and consistent byte counters.
        /// </summary>
        [TestMethod]
        public void ParsesOnlyRunningRepairProgress()
        {
            var poolID = Guid.NewGuid();
            var taskID = Guid.NewGuid();
            var bytes = new byte[0x270];
            PutUInt32(bytes, 0, 0x270);
            PutUInt32(bytes, 4, 0x270);
            PutGuid(bytes, 8, poolID);
            PutGuid(bytes, 0x18, taskID);
            PutUInt32(bytes, 0x23C, 5);
            PutUInt32(bytes, 0x240, 2);
            PutUInt32(bytes, 0x248, 37);
            PutUInt64(bytes, 0x250, 370);
            PutUInt64(bytes, 0x258, 1000);

            Assert.IsTrue(WindowsStorageSpacesPoolReader.TryParseRepairTaskInfo(bytes, bytes.Length, poolID, taskID, out var progress));

            Assert.AreEqual((ulong)370, progress.ProcessedBytes);
            Assert.AreEqual((ulong)1000, progress.TotalBytes);
            Assert.AreEqual(37.0, progress.PercentComplete);

            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParseRepairTaskInfo(bytes, bytes.Length, poolID, Guid.NewGuid(), out _));

            PutUInt32(bytes, 0x240, 1);
            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParseRepairTaskInfo(bytes, bytes.Length, poolID, taskID, out _));

            PutUInt32(bytes, 0x240, 2);
            PutUInt32(bytes, 0x23C, 4);
            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParseRepairTaskInfo(bytes, bytes.Length, poolID, taskID, out _));

            PutUInt32(bytes, 0x23C, 5);
            PutUInt64(bytes, 0x250, 1001);
            Assert.IsFalse(WindowsStorageSpacesPoolReader.TryParseRepairTaskInfo(bytes, bytes.Length, poolID, taskID, out _));
        }

        /// <summary>
        /// Aggregates repair bytes across spaces and returns null when no repair task is running.
        /// </summary>
        [TestMethod]
        public void AggregatesPoolRepairProgressOnlyFromActiveSpaces()
        {
            var poolID = Guid.NewGuid();
            var pool = new StoragePool(poolID, "Test Pool", string.Empty, false, 5000, 3000, 2, 3, 3);

            Assert.IsNull(pool.RepairProgressPercent);

            var first  = new StorageSpace(poolID, Guid.NewGuid(), "First" , string.Empty, 1000);
            var second = new StorageSpace(poolID, Guid.NewGuid(), "Second", string.Empty, 3000);
            var idle   = new StorageSpace(poolID, Guid.NewGuid(), "Idle"  , string.Empty, 1000);

            first .SetRepairProgress(new StorageRepairProgress( 250, 1000));
            second.SetRepairProgress(new StorageRepairProgress(2250, 3000));

            pool.SetSpaces(new List<StorageSpace> { first, second, idle });

            Assert.AreEqual(62.5, pool.RepairProgressPercent);
            Assert.IsNull(idle.RepairProgress);
            Assert.AreEqual(100.0 / 3, new StorageRepairProgress(1, 3).PercentComplete, 0.000001);
        }

        #endregion

        #region Private

        /// <summary>
        /// Writes a GUID into a synthetic Spaceport response.
        /// </summary>
        /// <param name="bytes">The response buffer.</param>
        /// <param name="offset">The field offset.</param>
        /// <param name="value">The GUID to write.</param>
        private static void PutGuid(byte[] bytes, int offset, Guid value) => value.ToByteArray().CopyTo(bytes, offset);

        /// <summary>
        /// Writes a 32-bit value into a synthetic Spaceport response.
        /// </summary>
        /// <param name="bytes">The response buffer.</param>
        /// <param name="offset">The field offset.</param>
        /// <param name="value">The value to write.</param>
        private static void PutUInt32(byte[] bytes, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);

        /// <summary>
        /// Writes a 64-bit value into a synthetic Spaceport response.
        /// </summary>
        /// <param name="bytes">The response buffer.</param>
        /// <param name="offset">The field offset.</param>
        /// <param name="value">The value to write.</param>
        private static void PutUInt64(byte[] bytes, int offset, ulong value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);

        /// <summary>
        /// Writes UTF-16 text into a synthetic Spaceport response.
        /// </summary>
        /// <param name="bytes">The response buffer.</param>
        /// <param name="offset">The field offset.</param>
        /// <param name="value">The text to write.</param>
        private static void PutText(byte[] bytes, int offset, string value) => Encoding.Unicode.GetBytes(value).CopyTo(bytes, offset);

        #endregion
    }
}
