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
using DiskInfoToolkit.Monitoring;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Partitions
{
    /// <summary>
    /// Reads disk layouts and resolves their drive letters from complete volume extent tables.
    /// </summary>
    internal static class StoragePartitionReader
    {
        #region Fields

        private const int PartitionLdmMbr = 0x42;

        private static readonly Guid PartitionLdmMetadataGuid = new Guid("5808C8AA-7E8F-42E0-85D2-E1E90434CFB3");

        private static readonly Guid PartitionLdmDataGuid = new Guid("AF9B60A0-1431-4F62-BC68-3311714A69AD");

        #endregion

        #region Public

        /// <summary>
        /// Refreshes the partition snapshot while retaining the last complete snapshot on read failure.
        /// </summary>
        /// <param name="device">The disk whose partitions are refreshed.</param>
        /// <param name="ioControl">The I/O implementation used to read the disk and volumes.</param>
        /// <param name="volumeExtents">An optional extent cache shared across disks in one scan.</param>
        /// <returns>Whether the partition snapshot or its stale state changed.</returns>
        public static bool PopulatePartitions(StorageDevice device, IStorageIoControl ioControl, WindowsVolumeExtentMap volumeExtents = null)
        {
            if (device == null || ioControl == null || ioControl is LinuxStorageIoControl)
            {
                return false;
            }

            volumeExtents ??= new WindowsVolumeExtentMap(ioControl);

            if (ioControl is WindowsStorageIoControl windowsIo)
            {
                windowsIo.RegisterDeviceAliases(device);
            }

            device.PartitionsLastCheckedUtc = DateTime.UtcNow;

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
                    if (!TryReadPartitions(handle, ioControl, out var partitions))
                    {
                        continue;
                    }

                    if (!AssignDriveLettersAndFreeSpace(partitions, device, ioControl, volumeExtents))
                    {
                        // A different disk path cannot repair a failed volume lookup.
                        // Avoid repeating the layout IOCTL after a complete layout read.
                        break;
                    }

                    //Check if partitions have changed compared to the existing snapshot
                    bool changed = device.PartitionsAreStale || StorageDeviceSnapshotComparer.AreDifferent(
                        new StorageDevice { Partitions = StorageDeviceCloneHelper.Clone(device).Partitions },
                        new StorageDevice { Partitions = partitions });

                    device.Partitions            = partitions;
                    device.PartitionsLastReadUtc = DateTime.UtcNow;
                    device.PartitionsAreStale    = false;
                    device.LastUpdatedUtc        = DateTime.UtcNow;

                    return changed;
                }
            }

            // A failed read is not an empty partition table. Keep the last complete snapshot.
            bool becameStale = !device.PartitionsAreStale;

            device.PartitionsAreStale = true;

            return becameStale;
        }

        #endregion

        #region Private

        /// <summary>
        /// Enumerates distinct disk paths to try for a layout read, starting with the physical disk path.
        /// </summary>
        /// <param name="device">The disk containing the known paths.</param>
        /// <returns>The paths to try in order.</returns>
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

        /// <summary>
        /// Parses a complete drive layout and distinguishes a valid empty layout from a failed read.
        /// </summary>
        /// <param name="handle">An open disk handle.</param>
        /// <param name="ioControl">The I/O implementation providing the raw layout.</param>
        /// <param name="result">The parsed partitions when the read succeeds.</param>
        /// <returns>Whether the complete layout was read and validated.</returns>
        private static bool TryReadPartitions(SafeFileHandle handle, IStorageIoControl ioControl, out List<StoragePartitionInfo> result)
        {
            result = new List<StoragePartitionInfo>();

            int partitionOffset = (int)Marshal.OffsetOf<DRIVE_LAYOUT_INFORMATION_EX_RAW>(nameof(DRIVE_LAYOUT_INFORMATION_EX_RAW.PartitionInformation));

            if (!ioControl.TryGetDriveLayout(handle, out var rawLayout) || rawLayout == null || rawLayout.Length < partitionOffset)
            {
                return false;
            }

            uint partitionCount = BitConverter.ToUInt32(rawLayout, sizeof(uint));
            int partitionSize = Marshal.SizeOf<PARTITION_INFORMATION_EX_RAW>();

            if (partitionCount > (rawLayout.Length - partitionOffset) / partitionSize)
            {
                return false;
            }

            for (int i = 0; i < partitionCount; ++i)
            {
                int offset = partitionOffset + (i * partitionSize);

                var entryBytes = new byte[partitionSize];
                Buffer.BlockCopy(rawLayout, offset, entryBytes, 0, partitionSize);

                var entry = StructureHelper.FromBytes<PARTITION_INFORMATION_EX_RAW>(entryBytes);

                var partition = new StoragePartitionInfo();
                partition.PartitionStyle     = ConvertPartitionStyle(entry.PartitionStyle);
                partition.StartingOffset     = entry.StartingOffset;
                partition.PartitionLength    = entry.PartitionLength;
                partition.PartitionNumber    = entry.PartitionNumber;
                partition.RewritePartition   = entry.RewritePartition != 0;
                partition.IsServicePartition = entry.IsServicePartition != 0;

                if (partition.PartitionStyle == DiskPartitionStyle.Mbr)
                {
                    partition.MbrPartitionType       = entry.Layout.Mbr.PartitionType;
                    partition.MbrBootIndicator       = entry.Layout.Mbr.BootIndicator != 0;
                    partition.MbrRecognizedPartition = entry.Layout.Mbr.RecognizedPartition != 0;
                    partition.MbrPartitionID         = entry.Layout.Mbr.PartitionID;
                    partition.IsDynamicDiskPartition = entry.Layout.Mbr.PartitionType == PartitionLdmMbr;
                }
                else if (partition.PartitionStyle == DiskPartitionStyle.Gpt)
                {
                    partition.GptPartitionType       = entry.Layout.Gpt.PartitionType;
                    partition.GptPartitionID         = entry.Layout.Gpt.PartitionID;
                    partition.GptAttributes          = entry.Layout.Gpt.Attributes;
                    partition.GptName                = DecodeGptName(entry.Layout.Gpt);
                    partition.IsDynamicDiskPartition =
                        entry.Layout.Gpt.PartitionType == PartitionLdmMetadataGuid
                     || entry.Layout.Gpt.PartitionType == PartitionLdmDataGuid;
                }

                result.Add(partition);
            }

            return true;
        }

        /// <summary>
        /// Resolves drive letters and free space without publishing an incomplete volume lookup.
        /// </summary>
        /// <param name="partitions">The partitions from the current layout read.</param>
        /// <param name="device">The disk being refreshed.</param>
        /// <param name="ioControl">The I/O implementation used for volume queries.</param>
        /// <param name="volumeExtents">The extent cache shared by this scan.</param>
        /// <returns>Whether the volume lookups are complete enough to publish the layout.</returns>
        private static bool AssignDriveLettersAndFreeSpace(List<StoragePartitionInfo> partitions, StorageDevice device,
            IStorageIoControl ioControl, WindowsVolumeExtentMap volumeExtents)
        {
            if (partitions == null || partitions.Count == 0 || !device.StorageDeviceNumber.HasValue)
            {
                return true;
            }

            uint diskNumber = device.StorageDeviceNumber.GetValueOrDefault();

            //Go through all drive letters and query their disk extents to find matches with the partitions we have read
            for (char driveLetter = 'A'; driveLetter <= 'Z'; ++driveLetter)
            {
                // Try to read the disk extents for the volume.
                if (!volumeExtents.TryGet(driveLetter, out var extents,
                    out bool severeFailure, out bool queryFailed))
                {
                    // If the query failed, check if any of the previously known partitions still match the current partitions by starting offset.
                    bool previousVolumeStillExpected = queryFailed
                        && device.Partitions?.Any(old => old.DriveLetter == driveLetter
                            && partitions.Any(current => current.StartingOffset == old.StartingOffset)) == true;

                    if (severeFailure || previousVolumeStillExpected)
                    {
                        return false;
                    }

                    continue;
                }

                foreach (var extent in extents)
                {
                    foreach (var partition in partitions)
                    {
                        //Match the volume to the partition by disk number and starting offset.
                        if (extent.DiskNumber == diskNumber && extent.StartingOffset == partition.StartingOffset)
                        {
                            partition.DriveLetter = driveLetter;
                            partition.VolumePath = $@"{driveLetter}:\";

                            //Read free space only after the volume is matched to this partition.
                            if (Kernel32Native.GetDiskFreeSpaceEx(partition.VolumePath, out var freeBytes, out _, out _))
                            {
                                partition.AvailableFreeSpaceBytes = freeBytes;
                            }
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Converts the native partition style to the toolkit representation.
        /// </summary>
        /// <param name="rawStyle">The native partition style value.</param>
        /// <returns>The partition style.</returns>
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

        /// <summary>
        /// Decodes the name stored in a GPT partition entry.
        /// </summary>
        /// <param name="rawGpt">The native GPT entry.</param>
        /// <returns>The trimmed partition name.</returns>
        private static string DecodeGptName(PARTITION_INFORMATION_GPT_RAW rawGpt)
        {
            return StringUtil.TrimStorageString(rawGpt.NameStr);
        }

        #endregion
    }
}
