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
using DiskInfoToolkit.Monitoring;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Partitions
{
    internal static class StoragePartitionReader
    {
        #region Fields

        private const int PartitionLdmMbr = 0x42;

        private static readonly Guid PartitionLdmMetadataGuid = new Guid("5808C8AA-7E8F-42E0-85D2-E1E90434CFB3");

        private static readonly Guid PartitionLdmDataGuid = new Guid("AF9B60A0-1431-4F62-BC68-3311714A69AD");

        #endregion

        #region Public

        public static bool PopulatePartitions(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null)
            {
                return false;
            }

            foreach (var path in GetPartitionReadPaths(device))
            {
                SafeFileHandle handle = ioControl.OpenDevice(
                    path,
                    IoAccess.ReadAttributes,
                    IoShare.All,
                    IoCreation.OpenExisting,
                    IoFlags.Normal);

                if (handle == null || handle.IsInvalid)
                {
                    continue;
                }

                using (handle)
                {
                    var partitions = ReadPartitions(handle, ioControl);
                    if (partitions.Count == 0)
                    {
                        continue;
                    }

                    AssignDriveLettersAndFreeSpace(partitions, device.StorageDeviceNumber.GetValueOrDefault(), ioControl);

                    //Check if partitions have changed compared to the existing snapshot
                    bool changed = StorageDeviceSnapshotComparer.AreDifferent(
                        new StorageDevice { Partitions = StorageDeviceCloneHelper.Clone(device).Partitions },
                        new StorageDevice { Partitions = partitions });

                    device.Partitions = partitions;
                    device.LastUpdatedUtc = DateTime.UtcNow;
                    return changed;
                }
            }

            device.Partitions = new List<StoragePartitionInfo>();
            device.LastUpdatedUtc = DateTime.UtcNow;

            return false;
        }

        #endregion

        #region Private

        private static IEnumerable<string> GetPartitionReadPaths(StorageDevice device)
        {
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            //If we have a storage device number, we can try to read partitions from the physical drive path
            if (device?.StorageDeviceNumber.HasValue == true)
            {
                var physicalDrivePath = $@"\\.\PhysicalDrive{device.StorageDeviceNumber.Value}";
                if (yielded.Add(physicalDrivePath))
                {
                    yield return physicalDrivePath;
                }
            }

            //Try to read from device path
            var devicePath = StringUtil.FirstNonEmpty(device?.DevicePath);
            if (!string.IsNullOrWhiteSpace(devicePath) && yielded.Add(devicePath))
            {
                yield return devicePath;
            }

            //Try alternate device path
            var alternateDevicePath = StringUtil.FirstNonEmpty(device?.AlternateDevicePath);
            if (!string.IsNullOrWhiteSpace(alternateDevicePath) && yielded.Add(alternateDevicePath))
            {
                yield return alternateDevicePath;
            }
        }

        private static List<StoragePartitionInfo> ReadPartitions(SafeFileHandle handle, IStorageIoControl ioControl)
        {
            var result = new List<StoragePartitionInfo>();

            if (!ioControl.TryGetDriveLayout(handle, out var rawLayout) || rawLayout == null || rawLayout.Length < Marshal.SizeOf<DRIVE_LAYOUT_INFORMATION_EX_RAW>())
            {
                return result;
            }

            var layoutHeader = StructureHelper.FromBytes<DRIVE_LAYOUT_INFORMATION_EX_RAW>(rawLayout);

            int partitionOffset = (int)Marshal.OffsetOf<DRIVE_LAYOUT_INFORMATION_EX_RAW>(nameof(DRIVE_LAYOUT_INFORMATION_EX_RAW.PartitionInformation));
            int partitionSize = Marshal.SizeOf<PARTITION_INFORMATION_EX_RAW>();

            for (int i = 0; i < layoutHeader.PartitionCount; ++i)
            {
                int offset = partitionOffset + (i * partitionSize);
                if (offset + partitionSize > rawLayout.Length)
                {
                    break;
                }

                var entryBytes = new byte[partitionSize];
                Buffer.BlockCopy(rawLayout, offset, entryBytes, 0, partitionSize);
                var entry = StructureHelper.FromBytes<PARTITION_INFORMATION_EX_RAW>(entryBytes);

                var partition = new StoragePartitionInfo();
                partition.PartitionStyle = ConvertPartitionStyle(entry.PartitionStyle);
                partition.StartingOffset = entry.StartingOffset;
                partition.PartitionLength = entry.PartitionLength;
                partition.PartitionNumber = entry.PartitionNumber;
                partition.RewritePartition = entry.RewritePartition != 0;
                partition.IsServicePartition = entry.IsServicePartition != 0;

                if (partition.PartitionStyle == DiskPartitionStyle.Mbr)
                {
                    partition.MbrPartitionType = entry.Layout.Mbr.PartitionType;
                    partition.MbrBootIndicator = entry.Layout.Mbr.BootIndicator != 0;
                    partition.MbrRecognizedPartition = entry.Layout.Mbr.RecognizedPartition != 0;
                    partition.MbrPartitionID = entry.Layout.Mbr.PartitionID;
                    partition.IsDynamicDiskPartition = entry.Layout.Mbr.PartitionType == PartitionLdmMbr;
                }
                else if (partition.PartitionStyle == DiskPartitionStyle.Gpt)
                {
                    partition.GptPartitionType = entry.Layout.Gpt.PartitionType;
                    partition.GptPartitionID = entry.Layout.Gpt.PartitionID;
                    partition.GptAttributes = entry.Layout.Gpt.Attributes;
                    partition.GptName = DecodeGptName(entry.Layout.Gpt);
                    partition.IsDynamicDiskPartition =
                        entry.Layout.Gpt.PartitionType == PartitionLdmMetadataGuid
                        || entry.Layout.Gpt.PartitionType == PartitionLdmDataGuid;
                }

                result.Add(partition);
            }

            return result;
        }

        private static void AssignDriveLettersAndFreeSpace(List<StoragePartitionInfo> partitions, uint diskNumber, IStorageIoControl ioControl)
        {
            if (partitions == null || partitions.Count == 0)
            {
                return;
            }

            //Go through all drive letters and query their disk extents to find matches with the partitions we have read
            for (char driveLetter = 'A'; driveLetter <= 'Z'; ++driveLetter)
            {
                string drivePath = $@"\\.\{driveLetter}:";
                SafeFileHandle handle = ioControl.OpenDevice(
                    drivePath,
                    IoAccess.ReadAttributes,
                    IoShare.All,
                    IoCreation.OpenExisting,
                    IoFlags.Normal);

                if (handle == null || handle.IsInvalid)
                {
                    continue;
                }

                using (handle)
                {
                    var extentBuffer = new byte[BufferSizeConstants.Size4K];
                    if (!ioControl.SendRawIoControl(handle, IoControlCodes.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, extentBuffer, out var bytesReturned))
                    {
                        continue;
                    }

                    if (bytesReturned < Marshal.SizeOf<VOLUME_DISK_EXTENTS_RAW>())
                    {
                        continue;
                    }

                    var header = StructureHelper.FromBytes<VOLUME_DISK_EXTENTS_RAW>(extentBuffer);

                    int extentOffset = (int)Marshal.OffsetOf<VOLUME_DISK_EXTENTS_RAW>(nameof(VOLUME_DISK_EXTENTS_RAW.FirstExtent));
                    int extentSize = Marshal.SizeOf<DISK_EXTENT_RAW>();

                    //Iterate through the disk extents for this volume
                    for (int i = 0; i < header.NumberOfDiskExtents; ++i)
                    {
                        int offset = extentOffset + (i * extentSize);
                        if (offset + extentSize > extentBuffer.Length)
                        {
                            break;
                        }

                        var entryBytes = new byte[extentSize];
                        Buffer.BlockCopy(extentBuffer, offset, entryBytes, 0, extentSize);

                        var extent = StructureHelper.FromBytes<DISK_EXTENT_RAW>(entryBytes);

                        foreach (var partition in partitions)
                        {
                            //Try to find a matching partition for this extent based on disk number and starting offset
                            if (extent.DiskNumber == diskNumber && extent.StartingOffset == partition.StartingOffset)
                            {
                                partition.DriveLetter = driveLetter;
                                partition.VolumePath = $@"{driveLetter}:\";

                                //Get free space for this partition
                                if (Kernel32Native.GetDiskFreeSpaceEx(partition.VolumePath, out var freeBytes, out var totalBytes, out var totalFreeBytes))
                                {
                                    partition.AvailableFreeSpaceBytes = freeBytes;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static DiskPartitionStyle ConvertPartitionStyle(int rawStyle)
        {
            switch (rawStyle)
            {
                case 0:
                    return DiskPartitionStyle.Mbr;
                case 1:
                    return DiskPartitionStyle.Gpt;
                default:
                    return DiskPartitionStyle.Raw;
            }
        }

        private static string DecodeGptName(PARTITION_INFORMATION_GPT_RAW rawGpt)
        {
            return StringUtil.TrimStorageString(rawGpt.NameStr);
        }

        #endregion
    }
}
