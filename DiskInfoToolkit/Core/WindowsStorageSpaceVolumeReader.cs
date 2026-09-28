/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using BlackSharp.Core.Interop.Windows.Native;
using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Pnp;
using DiskInfoToolkit.StorageSpaces;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Text;

namespace DiskInfoToolkit.Core
{
    /// <summary>
    /// Associates Storage Spaces virtual disks with their file-system volumes.
    /// </summary>
    internal static class WindowsStorageSpaceVolumeReader
    {
        #region Fields

        internal const string StorageSpaceInstancePrefix = @"STORAGE\DISK\";

        private const int VolumeNameBufferCharacters = 1024;

        #endregion

        #region Internal

        /// <summary>
        /// Adds file-system usage from volumes on each present Storage Spaces virtual disk.
        /// </summary>
        /// <param name="pools">Pools returned by Spaceport.</param>
        /// <param name="ioControl">The platform IOCTL implementation.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="pools"/> or <paramref name="ioControl"/> is <c>null</c>.</exception>
        internal static void Populate(IReadOnlyList<StoragePool> pools, IStorageIoControl ioControl)
        {
            if (pools == null)
                throw new ArgumentNullException(nameof(pools));

            if (ioControl == null)
                throw new ArgumentNullException(nameof(ioControl));

            var spaceIDs = new HashSet<Guid>(pools.SelectMany(pool => pool.Spaces).Select(space => space.ID));

            if (spaceIDs.Count == 0)
            {
                return;
            }

            // Resolve the current number independently of the caller's disk snapshot.
            // Disk numbers are temporary and can change after a rescan or reboot.
            var diskNumbersBySpaceID = ResolveVirtualDiskNumbers(spaceIDs, ioControl);

            if (diskNumbersBySpaceID.Count == 0)
            {
                return;
            }

            PopulateFromVolumeExtents(pools, diskNumbersBySpaceID, ReadVolumeDiskNumbers(ioControl), ReadVolume);
        }

        /// <summary>
        /// Matches volume extents to virtual disk numbers and sums readable file-system capacities.
        /// </summary>
        /// <param name="pools">Pools whose spaces receive the capacity values.</param>
        /// <param name="diskNumbersBySpaceID">The current virtual disk number of each space.</param>
        /// <param name="volumeDiskNumbers">Volume paths and all disk numbers in their extent tables.</param>
        /// <param name="readVolume">Reads total and free bytes for a volume path.</param>
        /// <exception cref="ArgumentNullException">Thrown if any of the parameters are <c>null</c>.</exception>
        internal static void PopulateFromVolumeExtents(IReadOnlyList<StoragePool> pools,
            IReadOnlyDictionary<Guid, uint> diskNumbersBySpaceID,
            IReadOnlyDictionary<string, IReadOnlyList<uint>> volumeDiskNumbers,
            Func<string, (ulong TotalBytes, ulong FreeBytes)?> readVolume)
        {
            if (pools == null)
                throw new ArgumentNullException(nameof(pools));

            if (diskNumbersBySpaceID == null)
                throw new ArgumentNullException(nameof(diskNumbersBySpaceID));

            if (volumeDiskNumbers == null)
                throw new ArgumentNullException(nameof(volumeDiskNumbers));

            if (readVolume == null)
                throw new ArgumentNullException(nameof(readVolume));

            // Disk numbers are temporary Windows identifiers. A duplicate number cannot
            // unambiguously identify which space owns an entire volume.
            var spaceCountsByDiskNumber = diskNumbersBySpaceID.Values
                .GroupBy(diskNumber => diskNumber)
                .ToDictionary(group => group.Key, group => group.Count());

            // Only consider volumes that are fully contained on one virtual disk. A volume
            // spanning multiple disks cannot be attributed to just one space.
            foreach (var pool in pools)
            {
                foreach (var space in pool.Spaces)
                {
                    if (!diskNumbersBySpaceID.TryGetValue(space.ID, out uint diskNumber)
                     || spaceCountsByDiskNumber[diskNumber] != 1)
                    {
                        continue;
                    }

                    ulong totalBytes = 0;
                    ulong freeBytes = 0;
                    int volumeCount = 0;
                    bool overflow = false;

                    // Use the disk extents to find which virtual disk the volume GUID path resides on.
                    foreach (var volume in volumeDiskNumbers)
                    {
                        // Multiple extents on this one virtual disk are fine.
                        if (volume.Value == null || volume.Value.Count == 0
                         || volume.Value.Any(extentDiskNumber => extentDiskNumber != diskNumber))
                        {
                            continue;
                        }

                        var capacity = readVolume(volume.Key);

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
                            overflow = true;
                            break;
                        }
                    }

                    // Only set the capacity when at least one volume was counted and no overflow occurred.
                    if (!overflow && volumeCount > 0)
                    {
                        space.SetMountedVolumeCapacity(totalBytes, freeBytes, volumeCount);
                    }
                }
            }
        }

        #endregion

        #region Private

        /// <summary>
        /// Finds current virtual disk numbers by GUID serial and SetupAPI interface paths.
        /// </summary>
        /// <param name="spaceIDs">The Spaceport identifiers to resolve.</param>
        /// <param name="ioControl">The platform IOCTL implementation.</param>
        /// <returns>Unambiguous space identifiers and their current disk numbers.</returns>
        private static Dictionary<Guid, uint> ResolveVirtualDiskNumbers(HashSet<Guid> spaceIDs, IStorageIoControl ioControl)
        {
            var result = new Dictionary<Guid, uint>();
            var missingSpaceIDs = new HashSet<Guid>(spaceIDs);

            List<PnpDiskNode> diskInterfaces;

            try
            {
                // Obtain a device interface path from SetupAPI and validate
                // the bus type and GUID serial before accepting its disk number.
                diskInterfaces = PnpDiskEnumerator.EnumerateDiskInterfaces();
            }
            catch (Win32Exception)
            {
                return result;
            }

            var ambiguousSpaceIDs = new HashSet<Guid>();

            // Storage Spaces currently exposes its space GUID in the PnP instance ID.
            // Use that identity to avoid opening unrelated member disks on the usual path.
            // Keep the descriptor scan as a fallback if that instance-ID form changes.
            foreach (var diskInterface in diskInterfaces.Where(node => HasStorageSpaceInstanceID(node, missingSpaceIDs)))
            {
                TryAddVirtualDiskNumber(diskInterface, missingSpaceIDs, ioControl, result, ambiguousSpaceIDs);
            }

            // If any spaces remain unresolved, scan all disk interfaces for a valid descriptor and GUID serial.
            if (missingSpaceIDs.Any(spaceID => !result.ContainsKey(spaceID) && !ambiguousSpaceIDs.Contains(spaceID)))
            {
                foreach (var diskInterface in diskInterfaces.Where(node => !HasStorageSpaceInstanceID(node, missingSpaceIDs)))
                {
                    TryAddVirtualDiskNumber(diskInterface, missingSpaceIDs, ioControl, result, ambiguousSpaceIDs);
                }
            }

            return result;
        }

        /// <summary>
        /// Checks the PnP device instance ID without interpreting the interface path.
        /// </summary>
        /// <param name="diskInterface">The interface reported by SetupAPI.</param>
        /// <param name="spaceIDs">The unresolved space identifiers.</param>
        /// <returns>Whether the instance ID names one of the requested spaces.</returns>
        private static bool HasStorageSpaceInstanceID(PnpDiskNode diskInterface, HashSet<Guid> spaceIDs)
        {
            string instanceID = diskInterface.DeviceInstanceID;

            return !string.IsNullOrWhiteSpace(instanceID)
                && instanceID.StartsWith(StorageSpaceInstancePrefix, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(instanceID.Substring(StorageSpaceInstancePrefix.Length), out var spaceID)
                && spaceIDs.Contains(spaceID);
        }

        /// <summary>
        /// Accepts a virtual disk number only after validating its descriptor and GUID serial.
        /// </summary>
        /// <param name="diskInterface">The interface reported by SetupAPI.</param>
        /// <param name="spaceIDs">The unresolved space identifiers.</param>
        /// <param name="ioControl">The platform IOCTL implementation.</param>
        /// <param name="result">Validated space-to-disk-number assignments.</param>
        /// <param name="ambiguousSpaceIDs">Space identifiers with conflicting disk numbers.</param>
        private static void TryAddVirtualDiskNumber(PnpDiskNode diskInterface, HashSet<Guid> spaceIDs,
            IStorageIoControl ioControl, Dictionary<Guid, uint> result, HashSet<Guid> ambiguousSpaceIDs)
        {
            SafeFileHandle handle = ioControl.OpenDevice(
                diskInterface.DevicePath,
                IoAccess.None,
                IoShare.All,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            using (handle)
            {
                // Validate the bus type and GUID serial before accepting the disk number.
                // The serial is a stable identifier for the virtual disk,
                // while the disk number is temporary and may change after a reboot or reconfiguration.
                if (handle == null || handle.IsInvalid
                 || !ioControl.TryGetStorageDeviceDescriptor(handle, out var descriptor)
                 || descriptor.BusType != StorageBusType.Spaces
                 || !Guid.TryParse(descriptor.SerialNumber, out var spaceID)
                 || !spaceIDs.Contains(spaceID)
                 || ambiguousSpaceIDs.Contains(spaceID)
                 || !ioControl.TryGetStorageDeviceNumber(handle, out var deviceNumber))
                {
                    return;
                }

                // Only accept the disk number if it is unambiguous for this space ID.
                if (result.TryGetValue(spaceID, out uint previousNumber))
                {
                    if (previousNumber != deviceNumber.DeviceNumber)
                    {
                        result.Remove(spaceID);

                        ambiguousSpaceIDs.Add(spaceID);
                    }
                }
                else
                {
                    result.Add(spaceID, deviceNumber.DeviceNumber);
                }
            }
        }

        /// <summary>
        /// Enumerates Windows volume GUID paths once and reads their disk extents.
        /// </summary>
        /// <param name="ioControl">The platform IOCTL implementation.</param>
        /// <returns>Readable volume paths and their complete disk-number lists.</returns>
        private static Dictionary<string, IReadOnlyList<uint>> ReadVolumeDiskNumbers(IStorageIoControl ioControl)
        {
            var result = new Dictionary<string, IReadOnlyList<uint>>(StringComparer.OrdinalIgnoreCase);
            var seenVolumeDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var volumeName = new StringBuilder(VolumeNameBufferCharacters);

            var findHandle = Kernel32Native.FindFirstVolume(volumeName, volumeName.Capacity);

            if (findHandle == IntPtr.Zero || findHandle == Kernel32.InvalidHandle)
            {
                return result;
            }

            try
            {
                do
                {
                    string volumePath = volumeName.ToString();
                    string volumeDevice = GetVolumeDeviceName(volumePath);

                    // Only read the disk extents once for each unique volume device name.
                    // Multiple GUID paths may point to the same volume.
                    if (!seenVolumeDevices.Contains(volumeDevice)
                     && WindowsVolumeDiskExtentReader.TryRead(volumePath.TrimEnd('\\'), ioControl, out var extents))
                    {
                        seenVolumeDevices.Add(volumeDevice);
                        result.Add(volumePath, extents.Select(extent => extent.DiskNumber).ToArray());
                    }

                    volumeName.Clear();
                }
                while (Kernel32Native.FindNextVolume(findHandle, volumeName, volumeName.Capacity));
            }
            finally
            {
                Kernel32Native.FindVolumeClose(findHandle);
            }

            return result;
        }

        /// <summary>
        /// Gets the NT volume device name so multiple GUID paths for one volume count once.
        /// </summary>
        /// <param name="volumePath">A volume GUID path with its trailing separator.</param>
        /// <returns>The device name or the original path when it cannot be resolved.</returns>
        private static string GetVolumeDeviceName(string volumePath)
        {
            if (string.IsNullOrWhiteSpace(volumePath)
             || volumePath.Length <= 5
             || !volumePath.StartsWith(@"\\?\", StringComparison.Ordinal)
             || !volumePath.EndsWith(@"\", StringComparison.Ordinal))
            {
                return volumePath;
            }

            string dosDeviceName = volumePath.Substring(4, volumePath.Length - 5);
            var targetPath = new char[VolumeNameBufferCharacters];

            uint length = Kernel32Native.QueryDosDevice(dosDeviceName, targetPath, targetPath.Length);

            if (length == 0)
            {
                return volumePath;
            }

            int terminator = Array.IndexOf(targetPath, '\0');

            return terminator > 0 ? new string(targetPath, 0, terminator) : volumePath;
        }

        /// <summary>
        /// Reads total and free bytes from a readable volume GUID path.
        /// </summary>
        /// <param name="volumePath">A volume GUID path with its trailing separator.</param>
        /// <returns>The file-system capacity, or null when it cannot be read.</returns>
        private static (ulong TotalBytes, ulong FreeBytes)? ReadVolume(string volumePath)
        {
            return Kernel32Native.GetDiskFreeSpaceEx(volumePath, out _, out ulong totalBytes, out ulong freeBytes)
                 ? (totalBytes, freeBytes) : null;
        }

        #endregion
    }
}
