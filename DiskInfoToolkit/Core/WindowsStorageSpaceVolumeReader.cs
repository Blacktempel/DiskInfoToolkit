/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using Kernel32 = DiskInfoToolkit.Native.Kernel32Native;

namespace DiskInfoToolkit.Core
{
    /// <summary>
    /// Associates existing Storage Spaces virtual disks with their mounted file-system volumes.
    /// </summary>
    internal static class WindowsStorageSpaceVolumeReader
    {
        #region Internal

        /// <summary>
        /// Adds mounted-volume usage to the matching storage spaces.
        /// </summary>
        /// <param name="pools">Pools returned by Spaceport.</param>
        /// <param name="disks">Already detected disks, including virtual Storage Spaces disks.</param>
        /// <param name="readVolume">Optional volume query used by tests.</param>
        internal static void Populate(IReadOnlyList<StoragePool> pools, IReadOnlyList<StorageDevice> disks,
            Func<string, (ulong TotalBytes, ulong FreeBytes)?> readVolume = null)
        {
            if (pools == null)
                throw new ArgumentNullException(nameof(pools));

            if (disks == null)
                throw new ArgumentNullException(nameof(disks));

            readVolume ??= ReadVolume;

            foreach (var pool in pools)
            {
                foreach (var space in pool.Spaces)
                {
                    // The virtual disk exposes the Spaceport space GUID as its serial number.
                    // Require one exact match so another disk cannot supply this spaces usage.
                    var matches = disks.Where(disk => disk.BusType == StorageBusType.Spaces
                        && Guid.TryParse(disk.SerialNumber, out var diskID) && diskID == space.ID).ToArray();

                    if (matches.Length != 1 || matches[0].Partitions == null)
                    {
                        continue;
                    }

                    ulong totalBytes = 0;
                    ulong freeBytes = 0;
                    int volumeCount = 0;

                    // Distinct paths avoid counting one mounted volume more than once.
                    // Unmounted or unreadable volumes are absent from these aggregate values.
                    foreach (var volumePath in matches[0].Partitions
                        .Select(partition => partition.VolumePath)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var capacity = readVolume(volumePath);

                        if (!capacity.HasValue || capacity.Value.FreeBytes > capacity.Value.TotalBytes)
                        {
                            continue;
                        }

                        try
                        {
                            totalBytes = checked(totalBytes + capacity.Value.TotalBytes);
                            freeBytes  = checked(freeBytes  + capacity.Value.FreeBytes );

                            ++volumeCount;
                        }
                        catch (OverflowException)
                        {
                            volumeCount = 0;
                            break;
                        }
                    }

                    if (volumeCount > 0)
                    {
                        space.SetMountedVolumeCapacity(totalBytes, freeBytes, volumeCount);
                    }
                }
            }
        }

        #endregion

        #region Private

        /// <summary>
        /// Reads total and free bytes from a mounted Windows volume.
        /// </summary>
        /// <param name="volumePath">A mounted volume path.</param>
        /// <returns>The volume capacity or null when the query fails.</returns>
        private static (ulong TotalBytes, ulong FreeBytes)? ReadVolume(string volumePath)
        {
            return Kernel32.GetDiskFreeSpaceEx(volumePath, out _, out ulong totalBytes, out ulong freeBytes)
                 ? (totalBytes, freeBytes) : null;
        }

        #endregion
    }
}
