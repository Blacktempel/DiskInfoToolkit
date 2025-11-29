/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Florian K.
 *
 * Code inspiration, improvements and fixes are from, but not limited to, following projects:
 * CrystalDiskInfo
 */

using DiskInfoToolkit.Interop.Structures;
using DiskInfoToolkit.Structures.Interop;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Tests.Interop.Structures
{
    [TestClass]
    public class StructuresSizeTest
    {
        [TestMethod]
        public void TestStructureSizes()
        {
            TestStructureSize<LargeInteger>(8);
            TestStructureSize<MSG>(48);

            TestStructureSize<DRIVE_LAYOUT_INFORMATION_EX>(192);
            TestStructureSize<DRIVE_LAYOUT_INFORMATION_UNION>(40);
            TestStructureSize<DRIVE_LAYOUT_INFORMATION_MBR>(8);
            TestStructureSize<DRIVE_LAYOUT_INFORMATION_GPT>(40);

            TestStructureSize<PARTITION_INFORMATION_EX>(144);
            TestStructureSize<PartitionInformationUnion>(112);
            TestStructureSize<PartitionInformationMBR>(24);
            TestStructureSize<PartitionInformationGPT>(112);

            TestStructureSize<ATA_PASS_THROUGH_EX>(48);
            TestStructureSize<ATA_PASS_THROUGH_EX_WITH_BUFFERS>(564);

            TestStructureSize<SmartAttributeStructure>(12);

            TestStructureSize<SENDCMDOUTPARAMS>(17);
            TestStructureSize<IDENTIFY_DEVICE_OUTDATA>(528);
            TestStructureSize<DRIVERSTATUS>(12);

            TestStructureSize<NVME_IDENTIFY_DEVICE>(4096);

            TestStructureSize<STORAGE_PROPERTY_QUERY>(12);
        }

        [TestMethod]
        public void TestStructureMemberOffsets()
        {
            TestStructureMemberOffset<DRIVE_LAYOUT_INFORMATION_EX>(nameof(DRIVE_LAYOUT_INFORMATION_EX.PartitionStyle),  0);
            TestStructureMemberOffset<DRIVE_LAYOUT_INFORMATION_EX>(nameof(DRIVE_LAYOUT_INFORMATION_EX.PartitionCount),  4);
            TestStructureMemberOffset<DRIVE_LAYOUT_INFORMATION_EX>(nameof(DRIVE_LAYOUT_INFORMATION_EX.Layout        ),  8);

            TestStructureMemberOffset<PARTITION_INFORMATION_EX>(nameof(PARTITION_INFORMATION_EX.PartitionStyle    ),  0);
            TestStructureMemberOffset<PARTITION_INFORMATION_EX>(nameof(PARTITION_INFORMATION_EX.StartingOffset    ),  8);
            TestStructureMemberOffset<PARTITION_INFORMATION_EX>(nameof(PARTITION_INFORMATION_EX.PartitionLength   ), 16);
            TestStructureMemberOffset<PARTITION_INFORMATION_EX>(nameof(PARTITION_INFORMATION_EX.PartitionNumber   ), 24);
            TestStructureMemberOffset<PARTITION_INFORMATION_EX>(nameof(PARTITION_INFORMATION_EX.RewritePartition  ), 28);
            TestStructureMemberOffset<PARTITION_INFORMATION_EX>(nameof(PARTITION_INFORMATION_EX.IsServicePartition), 29);
            TestStructureMemberOffset<PARTITION_INFORMATION_EX>(nameof(PARTITION_INFORMATION_EX.Layout            ), 32);
        }

        void TestStructureSize<TStructure>(int expectedSize)
            where TStructure : struct
        {
            Assert.AreEqual(expectedSize, Marshal.SizeOf<TStructure>(), $"Invalid size for {typeof(TStructure).Name}.");
        }

        void TestStructureMemberOffset<TStructure>(string member, int expectedOffset)
            where TStructure : struct
        {
            var offset = Marshal.OffsetOf<TStructure>(member);
            Assert.AreEqual(expectedOffset, offset.ToInt32(), $"Invalid offset for {typeof(TStructure).Name}.{member}.");
        }
    }
}
