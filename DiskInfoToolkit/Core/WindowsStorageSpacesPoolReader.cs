/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using Microsoft.Win32.SafeHandles;
using System.Text;
using OS = BlackSharp.Core.Platform.OperatingSystem;

namespace DiskInfoToolkit.Core
{
    /// <summary>
    /// Reads Storage Spaces pool metadata from spaceport.sys. These IOCTL layouts are undocumented;
    /// the offsets and status pairs were checked on spaceport.sys 10.0.26100.4202. Validate each
    /// response and map only status pairs verified by a controlled drive removal.
    /// </summary>
    internal static class WindowsStorageSpacesPoolReader
    {
        #region Fields

        // These are read-only Spaceport requests. The request layouts below were verified against
        // spaceport.sys 10.0.26100.4202 and are not a documented Windows API.
        private const string SpaceportPath = @"\\.\Spaceport";

        private const uint GetPoolsIoctl        = 0xE70004;
        private const uint GetPoolInfoIoctl     = 0xE70008;
        private const uint GetPoolDisksIoctl    = 0xE70404;
        private const uint GetPoolDiskInfoIoctl = 0xE70408;
        private const uint GetPoolSpacesIoctl   = 0xE70804;
        private const uint GetSpaceInfoIoctl    = 0xE70808;
        private const uint GetTasksIoctl        = 0xE70C04;
        private const uint GetTaskInfoIoctl     = 0xE70C08;

        private const int ErrorMoreData         = 234;

        private const int ListBufferSize        = 65536;
        private const int InfoBufferSize        = 8192;
        private const int SpaceInfoBufferSize   = 65536;

        // Request DWORD 0 is the request size.
        // Pool GUID starts at byte 4; member GUID at byte 20.
        private const int PoolInfoRequestSize   = 0x28;
        private const int MemberListRequestSize = 0x40;

        private const int MemberInfoRequestSize = 0x28;
        private const int SpaceListRequestSize  = 0x44;

        private const int SpaceInfoRequestSize  = 0x28;

        // Fixed fields in SpIoctlGetPoolInfo output.
        private const int PoolIDOffset                = 0x08;
        private const int PoolNameOffset              = 0x18;
        private const int PoolNameCharacters          = 256;
        private const int PoolDescriptionOffset       = 0x218;
        private const int PoolDescriptionCharacters   = 1024;
        private const int PoolPrimordialOffset        = 0xA18;
        private const int PoolStatusA20Offset         = 0xA20;
        private const int PoolStatusA24Offset         = 0xA24;
        private const int PoolSizeOffset              = 0xA38;
        private const int PoolAllocatedOffset         = 0xA40;
        private const int PoolConfiguredMembersOffset = 0xA50;
        private const int PoolInfoMinSize             = PoolConfiguredMembersOffset + sizeof(uint);

        // Fixed fields in SpIoctlGetDiskInfo output. Identity strings are packed, NUL-terminated
        // UTF-16 text after the fixed fields, so their positions depend on the lengths of the
        // strings before them. A table of byte offsets locates each one; zero means absent.
        // 0xA30 vendor, 0xA34 product, 0xA38 firmware, 0xA3C serial, 0xA40 raw serial,
        // 0xA44 trimmed serial (seen on NVMe only), 0xA7C location, 0xA80 PnP device ID.
        private const int  MemberPoolIDOffset           = 0x08;
        private const int  MemberIDOffset               = 0x18;
        private const int  MemberSerialOffsetField      = 0xA3C;
        private const int  MemberShortSerialOffsetField = 0xA44;
        private const int  MemberStringTableEnd         = 0xA88;
        private const int  DiskInfoMinSize              = MemberStringTableEnd;
        private const uint PoolInfoStructureSize  = 0xB10;
        private const uint DiskInfoStructureSize  = 0xC30;

        // Fixed fields in SpIoctlGetSpaceInfo output. The returned second DWORD
        // includes variable data after the 0xBD8-byte fixed structure.
        private const int  SpacePoolIDOffset          = 0x08;
        private const int  SpaceIDOffset              = 0x18;
        private const int  SpaceNameOffset            = 0x28;
        private const int  SpaceNameCharacters        = 256;
        private const int  SpaceDescriptionOffset     = 0x228;
        private const int  SpaceDescriptionCharacters = 1024;
        private const int  SpaceSizeOffset            = 0xA68;
        private const int  SpaceAllocatedOffset       = 0xA70;
        private const int  SpaceFootprintOffset       = 0xA78;
        private const int  SpaceInfoMinSize           = SpaceFootprintOffset + sizeof(ulong);
        private const uint SpaceInfoStructureSize     = 0xBD8;
        private const int  SpaceExtentCountOffset     = 0xB30;
        private const int  SpaceExtentsOffset         = 0xB48;
        private const int  SpaceExtentSize            = 0x90;
        private const int  SpaceExtentDiskIDOffset    = 0x64;

        // Task requests are two adjacent GUIDs, without the size DWORD used by pool requests.
        // The fixed task response is 0x270 bytes.
        private const int  TaskRequestSize          = 0x20;
        private const int  TaskInfoSize             = 0x270;
        private const int  TaskPoolIDOffset         = 0x08;
        private const int  TaskIDOffset             = 0x18;
        private const int  TaskTypeOffset           = 0x23C;
        private const int  TaskStateOffset          = 0x240;
        private const int  TaskProgressOffset       = 0x248;
        private const int  TaskProcessedBytesOffset = 0x250;
        private const int  TaskTotalBytesOffset     = 0x258;
        private const uint RepairTaskType           = 5;
        private const uint RunningTaskState         = 2;

        #endregion

        #region Internal

        /// <summary>
        /// Enumerates Storage Spaces pools and resolves their present members against existing disk objects.
        /// </summary>
        /// <param name="disks">The caller's already detected disk objects.</param>
        /// <param name="ioControl">The platform IOCTL implementation.</param>
        /// <returns>The pools that returned a validated information block.</returns>
        internal static List<StoragePool> Enumerate(IReadOnlyList<StorageDevice> disks, IStorageIoControl ioControl)
        {
            var result = new List<StoragePool>();

            if (!OS.IsWindows() || ioControl == null)
            {
                return result;
            }

            // These query IOCTLs require no file read or write access. Opening the control
            // device with access 0 also avoids requesting access to any member disk directly.
            using (var handle = ioControl.OpenDevice(SpaceportPath, IoAccess.None, IoShare.ReadWrite, IoCreation.OpenExisting, IoFlags.Normal))
            {
                if (handle == null || handle.IsInvalid)
                {
                    return result;
                }

                // SpIoctlGetPools accepts four zero input bytes and returns a DWORD count followed
                // by that many 16-byte pool GUIDs. Every count is checked against bytesReturned.
                var listBuffer = new byte[ListBufferSize];

                if (!ioControl.SendRawIoControl(handle, GetPoolsIoctl, new byte[4], listBuffer, out int listLength)
                 || !TryReadGuidList(listBuffer, listLength, out var poolIDs))
                {
                    return result;
                }

                foreach (var poolID in poolIDs)
                {
                    // Pool information uses a 0x28-byte request: length at 0, pool GUID at 4.
                    // A malformed or unfamiliar response is omitted rather than guessed at.
                    var infoInput = CreateRequest(PoolInfoRequestSize, poolID);
                    var infoBuffer = new byte[InfoBufferSize];

                    if (!ioControl.SendRawIoControl(handle, GetPoolInfoIoctl, infoInput, infoBuffer, out int infoLength)
                     || !TryParsePoolInfo(infoBuffer, infoLength, poolID, out var pool))
                    {
                        continue;
                    }

                    // The primordial pool is Windows' available-storage pool from which concrete
                    // pools draw capacity. It is not a user-created pool containing spaces.
                    if (pool.IsPrimordial)
                    {
                        continue;
                    }

                    // The member-list request needs 0x40 input bytes. Its returned GUID list
                    // contains currently reported members; a disconnected disk may disappear
                    // while the configured count in the pool information remains unchanged.
                    var memberDevicesByID = new Dictionary<Guid, StorageDevice>();

                    var disksInput = CreateRequest(MemberListRequestSize, poolID);

                    var memberList = new byte[ListBufferSize];

                    if (ioControl.SendRawIoControl(handle, GetPoolDisksIoctl, disksInput, memberList, out int memberListLength)
                     && TryReadGuidList(memberList, memberListLength, out var memberIDs))
                    {
                        var members = new List<StorageDevice>();

                        foreach (var memberID in memberIDs)
                        {
                            // Member information is queried only for an identity to match.
                            var diskInfoInput = CreateRequest(MemberInfoRequestSize, poolID, memberID);

                            var diskInfo = new byte[InfoBufferSize];

                            if (!ioControl.SendRawIoControl(handle, GetPoolDiskInfoIoctl, diskInfoInput, diskInfo, out int diskInfoLength)
                             || !TryParseDiskSerials(diskInfo, diskInfoLength, poolID, memberID, out var serials))
                            {
                                continue;
                            }

                            // Use the first serial that identifies exactly one disk.
                            StorageDevice match = null;

                            foreach (var serial in serials)
                            {
                                match = FindUniqueDiskBySerial(disks, serial);

                                if (match != null)
                                {
                                    break;
                                }
                            }

                            if (match != null)
                            {
                                memberDevicesByID[memberID] = match;
                                if (!members.Contains(match))
                                {
                                    members.Add(match);
                                }
                            }
                        }

                        pool.SetMembers(memberIDs, members);
                    }

                    // SpaceAgent.exe sends a 0x44-byte request for this read-only list IOCTL.
                    // Its pool GUID and selector bytes are required even for a simple query.
                    var spacesInput = CreateSpaceListRequest(poolID);

                    var spaceList = new byte[ListBufferSize];

                    if (ioControl.SendRawIoControl(handle, GetPoolSpacesIoctl, spacesInput, spaceList, out int spaceListLength)
                     && TryReadGuidList(spaceList, spaceListLength, out var spaceIDs))
                    {
                        var spaces = new List<StorageSpace>();
                        foreach (var spaceID in spaceIDs)
                        {
                            // Each GUID is queried separately, so multiple spaces remain distinct
                            // even when they share one pool and the same member disks.
                            var spaceInput = CreateRequest(SpaceInfoRequestSize, poolID, spaceID);

                            // The fixed structure may carry variable data, so allow more room
                            // than the pool and member information buffers use.
                            var spaceInfo = new byte[SpaceInfoBufferSize];

                            bool received = ioControl.SendRawIoControl(handle, GetSpaceInfoIoctl, spaceInput, spaceInfo, out int spaceInfoLength);

                            // The response carries one extent record per slab, so a large space
                            // does not fit (a 20 TB space needed 17.8 MB). The driver still fills
                            // the fixed fields before reporting ERROR_MORE_DATA, so parse those and
                            // leave the extent list unavailable rather than dropping the space.
                            bool truncated = !received && IsMoreDataError(ioControl);

                            if ((received || truncated)
                             && TryParseSpaceInfo(spaceInfo, spaceInfoLength, poolID, spaceID, truncated, out var space))
                            {
                                if (space.ExtentInformationAvailable)
                                {
                                    var extentDisks = new List<StorageDevice>();

                                    foreach (var diskID in space.ExtentDiskIDs)
                                    {
                                        // A space's extent GUID names a physical pool disk. Resolve it
                                        // only through the pool's already matched StorageDevice objects.
                                        if (memberDevicesByID.TryGetValue(diskID, out var disk)
                                         && !extentDisks.Contains(disk))
                                        {
                                            extentDisks.Add(disk);
                                        }
                                    }

                                    space.SetExtentDisks(new(space.ExtentDiskIDs), extentDisks);
                                }

                                if (TryReadRepairProgress(handle, ioControl, poolID, spaceID, out var repairProgress))
                                {
                                    space.SetRepairProgress(repairProgress);
                                }

                                spaces.Add(space);
                            }
                        }

                        pool.SetSpaces(spaces);
                    }

                    result.Add(pool);
                }
            }

            return result;
        }

        /// <summary>
        /// Parses a Spaceport count and GUID array without trusting a count beyond the returned bytes.
        /// </summary>
        /// <param name="buffer">The IOCTL output buffer.</param>
        /// <param name="length">The number of bytes actually returned by DeviceIoControl.</param>
        /// <param name="IDs">The parsed identifiers when the response is valid.</param>
        /// <returns>Whether the complete GUID array is present.</returns>
        internal static bool TryReadGuidList(byte[] buffer, int length, out List<Guid> IDs)
        {
            IDs = new();

            if (buffer == null || length < 4 || length > buffer.Length)
            {
                return false;
            }

            // The first DWORD is a count, not a byte length. Compare it with the actual output
            // length before reading any GUID so a truncated response cannot cause overreads.
            uint count = BitConverter.ToUInt32(buffer, 0);

            if (count > (length - 4) / 16)
            {
                return false;
            }

            for (int i = 0; i < count; ++i)
            {
                IDs.Add(ReadGuid(buffer, 4 + i * 16));
            }

            return true;
        }

        /// <summary>
        /// Parses the verified pool-information layout and retains both raw status fields.
        /// </summary>
        /// <param name="buffer">The IOCTL output buffer.</param>
        /// <param name="length">The number of bytes actually returned by DeviceIoControl.</param>
        /// <param name="expectedID">The pool identifier used in the request.</param>
        /// <param name="pool">The parsed pool when the response is valid.</param>
        /// <returns>Whether the response matches the expected layout and pool.</returns>
        internal static bool TryParsePoolInfo(byte[] buffer, int length, Guid expectedID, out StoragePool pool)
        {
            pool = null;
            if (!HasValidInfoResponse(buffer, length, PoolInfoMinSize, PoolInfoStructureSize)
             || ReadGuid(buffer, PoolIDOffset) != expectedID)
            {
                return false;
            }

            // In this layout the fixed UTF-16 name occupies 0x18..0x218 and the description
            // occupies 0x218..0xA18. The GUID at 0x08 must match the requested pool.
            string name = ReadUtf16(buffer, PoolNameOffset, PoolNameCharacters);

            string description = ReadUtf16(buffer, PoolDescriptionOffset, PoolDescriptionCharacters);

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            // A18 marks the primordial pool, A38/A40 are total/allocated bytes, and A50 is the
            // configured member count. In the controlled unplug/replug test, only A20 and A24
            // changed (3/3 -> 2/2 -> 3/3); other status pairs remain unmapped.
            pool = new StoragePool(
                expectedID,
                name,
                description,
                BitConverter.ToUInt16(buffer, PoolPrimordialOffset) != 0,
                BitConverter.ToUInt64(buffer, PoolSizeOffset), BitConverter.ToUInt64(buffer, PoolAllocatedOffset),
                BitConverter.ToUInt32(buffer, PoolConfiguredMembersOffset),
                BitConverter.ToUInt32(buffer, PoolStatusA20Offset), BitConverter.ToUInt32(buffer, PoolStatusA24Offset));

            return true;
        }

        /// <summary>
        /// Reads the member serials used to identify an already detected physical disk.
        /// </summary>
        /// <param name="buffer">The IOCTL output buffer.</param>
        /// <param name="length">The number of bytes actually returned by DeviceIoControl.</param>
        /// <param name="expectedPoolID">The pool identifier used in the request.</param>
        /// <param name="expectedDiskID">The member identifier used in the request.</param>
        /// <param name="serials">The candidate serials, most specific first, when the response is valid.</param>
        /// <returns>Whether at least one complete serial is available.</returns>
        internal static bool TryParseDiskSerials(byte[] buffer, int length, Guid expectedPoolID, Guid expectedDiskID, out List<string> serials)
        {
            serials = new();

            if (!HasValidInfoResponse(buffer, length, DiskInfoMinSize, DiskInfoStructureSize)
             || ReadGuid(buffer, MemberPoolIDOffset) != expectedPoolID
             || ReadGuid(buffer, MemberIDOffset) != expectedDiskID)
            {
                return false;
            }

            // NVMe disks report a trimmed serial at 0xA44 that matches StorageDevice.SerialNumber,
            // while their 0xA3C serial is the EUI-based one. ATA disks have no 0xA44 string and
            // their 0xA3C serial matches. A missing disk still produced a short response during
            // testing, so a string must end inside the response to count.
            foreach (int field in new[] { MemberShortSerialOffsetField, MemberSerialOffsetField })
            {
                string serial = ReadTableString(buffer, length, field);

                if (!string.IsNullOrWhiteSpace(serial) && !serials.Contains(serial))
                {
                    serials.Add(serial);
                }
            }

            return serials.Count > 0;
        }

        /// <summary>
        /// Parses a Storage Spaces virtual disk from the Spaceport information layout.
        /// </summary>
        /// <param name="buffer">The IOCTL output buffer.</param>
        /// <param name="length">The number of bytes actually returned by DeviceIoControl.</param>
        /// <param name="expectedPoolID">The pool identifier used in the request.</param>
        /// <param name="expectedSpaceID">The storage space identifier used in the request.</param>
        /// <param name="space">The parsed storage space when the response is valid.</param>
        /// <returns>Whether the response matches the layout and requested identifiers.</returns>
        internal static bool TryParseSpaceInfo(byte[] buffer, int length, Guid expectedPoolID, Guid expectedSpaceID, out StorageSpace space)
        {
            return TryParseSpaceInfo(buffer, length, expectedPoolID, expectedSpaceID, false, out space);
        }

        /// <summary>
        /// Parses a Storage Spaces virtual disk, optionally from a response truncated by ERROR_MORE_DATA.
        /// </summary>
        /// <param name="buffer">The IOCTL output buffer.</param>
        /// <param name="length">The number of bytes actually returned by DeviceIoControl.</param>
        /// <param name="expectedPoolID">The pool identifier used in the request.</param>
        /// <param name="expectedSpaceID">The storage space identifier used in the request.</param>
        /// <param name="truncated">Whether the driver reported that the response did not fit.</param>
        /// <param name="space">The parsed storage space when the response is valid.</param>
        /// <returns>Whether the response matches the layout and requested identifiers.</returns>
        internal static bool TryParseSpaceInfo(byte[] buffer, int length, Guid expectedPoolID, Guid expectedSpaceID,
            bool truncated, out StorageSpace space)
        {
            space = null;

            bool valid = truncated
                ? HasTruncatedInfoResponse(buffer, length, SpaceInfoMinSize, SpaceInfoStructureSize)
                : HasValidInfoResponse    (buffer, length, SpaceInfoMinSize, SpaceInfoStructureSize);

            if (!valid
             || ReadGuid(buffer, SpacePoolIDOffset) != expectedPoolID
             || ReadGuid(buffer, SpaceIDOffset) != expectedSpaceID)
            {
                return false;
            }

            // SpIoctlGetSpaceInfo enumerates SDB_SPACE extents. For each extent it follows
            // SDB_EXTENT::GetDrive and copies that SDB_DRIVE's GUID into the 0x90-byte record
            // at record + 0x64. The count is at 0xB30 and the array begins at 0xB48.
            // Validate against the declared response size before reading any extent record.
            // A truncated response holds only part of the array, so its extents are not read.
            List<Guid> extentDiskIDs = null;

            if (!truncated)
            {
                uint count = BitConverter.ToUInt32(buffer, SpaceExtentCountOffset);

                uint responseSize = BitConverter.ToUInt32(buffer, 4);

                if (count > (responseSize - SpaceExtentsOffset) / SpaceExtentSize)
                {
                    return false;
                }

                extentDiskIDs = new List<Guid>();

                for (int i = 0; i < count; i++)
                {
                    var diskID = ReadGuid(buffer, SpaceExtentsOffset + i * SpaceExtentSize + SpaceExtentDiskIDOffset);

                    if (diskID != Guid.Empty && !extentDiskIDs.Contains(diskID))
                    {
                        extentDiskIDs.Add(diskID);
                    }
                }
            }

            // A space can have no allocated extents, even while its pool has member disks.
            string name = ReadUtf16(buffer, SpaceNameOffset, SpaceNameCharacters);

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            // On the verified Spaceport layout these adjacent QWORDs describe logical
            // allocation and physical pool footprint.
            space = new StorageSpace(expectedPoolID, expectedSpaceID, name,
                ReadUtf16(buffer, SpaceDescriptionOffset, SpaceDescriptionCharacters),
                BitConverter.ToUInt64(buffer, SpaceSizeOffset),
                BitConverter.ToUInt64(buffer, SpaceAllocatedOffset),
                BitConverter.ToUInt64(buffer, SpaceFootprintOffset));

            if (extentDiskIDs != null)
            {
                space.SetExtentDisks(extentDiskIDs, new List<StorageDevice>());
            }

            return true;
        }

        /// <summary>
        /// Parses only a running repair task from the Spaceport task layout.
        /// </summary>
        /// <param name="buffer">The IOCTL output buffer.</param>
        /// <param name="length">The number of bytes actually returned by DeviceIoControl.</param>
        /// <param name="expectedPoolID">The pool identifier used in the request.</param>
        /// <param name="expectedTaskID">The task identifier used in the request.</param>
        /// <param name="progress">The validated repair progress when available.</param>
        /// <returns>Whether this is a complete, running repair task with consistent counters.</returns>
        internal static bool TryParseRepairTaskInfo(byte[] buffer, int length, Guid expectedPoolID,
            Guid expectedTaskID, out StorageRepairProgress progress)
        {
            progress = null;

            if (!HasValidInfoResponse(buffer, length, TaskInfoSize, TaskInfoSize)
             || ReadGuid(buffer, TaskPoolIDOffset) != expectedPoolID
             || ReadGuid(buffer, TaskIDOffset    ) != expectedTaskID
             || BitConverter.ToUInt32(buffer, TaskTypeOffset ) != RepairTaskType
             || BitConverter.ToUInt32(buffer, TaskStateOffset) != RunningTaskState)
            {
                return false;
            }

            // SpIoctlGetRepairTaskInfo writes type 5, state 2 for active regeneration, the
            // rounded percentage at 0x248, and processed/total bytes at 0x250/0x258. Ignore
            // a pending task (state 1). Use byte counters to calculate the public percentage.
            uint value      = BitConverter.ToUInt32(buffer, TaskProgressOffset);
            ulong processed = BitConverter.ToUInt64(buffer, TaskProcessedBytesOffset);
            ulong total     = BitConverter.ToUInt64(buffer, TaskTotalBytesOffset);

            if (value > 100 || total == 0 || processed > total)
            {
                return false;
            }

            progress = new StorageRepairProgress(processed, total);
            return true;
        }

        /// <summary>
        /// Finds exactly one existing disk with the Spaceport serial number.
        /// </summary>
        /// <param name="disks">The caller's already detected disk objects.</param>
        /// <param name="serial">The serial reported for the pool member.</param>
        /// <returns>The original disk object, or null when no unique match exists.</returns>
        internal static StorageDevice FindUniqueDiskBySerial(IReadOnlyList<StorageDevice> disks, string serial)
        {
            if (disks == null || string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            string wanted = NormalizeSerial(serial);

            StorageDevice match = null;

            foreach (var disk in disks)
            {
                if (disk == null || NormalizeSerial(disk.SerialNumber) != wanted)
                {
                    continue;
                }

                // Model names are not unique. If two different objects share a serial, leaving
                // the member unresolved is safer than linking the wrong physical device.
                if (match != null && !ReferenceEquals(match, disk))
                {
                    return null; // An ambiguous serial must never attach an unrelated disk.
                }

                match = disk;
            }

            return match;
        }

        #endregion

        #region Private

        /// <summary>
        /// Reads the active repair task associated with one space using read-only Spaceport requests.
        /// </summary>
        /// <param name="handle">The opened Spaceport control device.</param>
        /// <param name="ioControl">The platform IOCTL implementation.</param>
        /// <param name="poolID">The containing pool identifier.</param>
        /// <param name="spaceID">The storage space identifier used to filter the task list.</param>
        /// <param name="progress">The selected task's validated progress.</param>
        /// <returns>Whether a running repair task was found.</returns>
        private static bool TryReadRepairProgress(SafeFileHandle handle,
            IStorageIoControl ioControl, Guid poolID, Guid spaceID, out StorageRepairProgress progress)
        {
            progress = null;

            // GetTasks filters by both pool and space GUID. Its result is a count followed by
            // task GUIDs, including a synthetic repair task only when the space needs repair.
            var taskIDsBuffer = new byte[ListBufferSize];

            if (!ioControl.SendRawIoControl(handle, GetTasksIoctl, CreateTaskRequest(poolID, spaceID), taskIDsBuffer, out int taskListLength)
             || !TryReadGuidList(taskIDsBuffer, taskListLength, out var taskIDs))
            {
                return false;
            }

            foreach (var taskID in taskIDs)
            {
                var taskInfo = new byte[TaskInfoSize];

                if (!ioControl.SendRawIoControl(handle, GetTaskInfoIoctl, CreateTaskRequest(poolID, taskID), taskInfo, out int taskInfoLength)
                 || !TryParseRepairTaskInfo(taskInfo, taskInfoLength, poolID, taskID, out var taskProgress))
                {
                    continue;
                }

                // The synthetic space repair task aggregates repair bytes. When both it and an
                // underlying task are listed, prefer the task with the larger total to avoid
                // reporting a single component instead of the full repair operation.
                if (progress == null || taskProgress.TotalBytes >= progress.TotalBytes)
                {
                    progress = taskProgress;
                }
            }

            return progress != null;
        }

        /// <summary>
        /// Creates the two-GUID request used by Spaceport task IOCTLs.
        /// </summary>
        /// <param name="poolID">The containing pool identifier.</param>
        /// <param name="selectorID">A space ID for listing or task ID for task information.</param>
        /// <returns>The initialized 32-byte request.</returns>
        private static byte[] CreateTaskRequest(Guid poolID, Guid selectorID)
        {
            var buffer = new byte[TaskRequestSize];

            poolID    .ToByteArray().CopyTo(buffer, 0);
            selectorID.ToByteArray().CopyTo(buffer, 16);

            return buffer;
        }

        /// <summary>
        /// Normalizes insignificant serial formatting without removing vendor prefixes.
        /// </summary>
        /// <param name="serial">The serial to normalize.</param>
        /// <returns>The comparable serial value.</returns>
        private static string NormalizeSerial(string serial)
        {
            return (serial ?? string.Empty).Trim().Trim('\0').Replace(" ", string.Empty).ToUpperInvariant();
        }

        /// <summary>
        /// Validates the fixed structure signature and the returned-byte count.
        /// </summary>
        /// <param name="buffer">The IOCTL output buffer.</param>
        /// <param name="length">The number of bytes actually returned by DeviceIoControl.</param>
        /// <param name="minimumLength">The final byte required by fields this reader uses.</param>
        /// <param name="structureSize">The expected first DWORD for this response type.</param>
        /// <returns>Whether the response can be parsed using the observed offsets.</returns>
        private static bool HasValidInfoResponse(byte[] buffer, int length, int minimumLength, uint structureSize)
        {
            // The first DWORD identifies the fixed structure layout; the second is the response
            // byte count. Reject updates that change the layout instead of silently misreading it.
            return buffer != null && length >= minimumLength && length >= structureSize && length <= buffer.Length
                && BitConverter.ToUInt32(buffer, 0) == structureSize
                && BitConverter.ToUInt32(buffer, 4) >= structureSize
                && BitConverter.ToUInt32(buffer, 4) <= length;
        }

        /// <summary>
        /// Validates a response the driver cut short with ERROR_MORE_DATA. The fixed structure
        /// must be complete, and the declared size must exceed what was returned.
        /// </summary>
        /// <param name="buffer">The IOCTL output buffer.</param>
        /// <param name="length">The number of bytes actually returned by DeviceIoControl.</param>
        /// <param name="minimumLength">The final byte required by fields this reader uses.</param>
        /// <param name="structureSize">The expected first DWORD for this response type.</param>
        /// <returns>Whether the fixed fields can be parsed using the observed offsets.</returns>
        private static bool HasTruncatedInfoResponse(byte[] buffer, int length, int minimumLength, uint structureSize)
        {
            return buffer != null && length >= minimumLength && length >= structureSize && length <= buffer.Length
                && BitConverter.ToUInt32(buffer, 0) == structureSize
                && BitConverter.ToUInt32(buffer, 4) > length;
        }

        /// <summary>
        /// Checks whether the last IOCTL failed only because its output buffer was too small.
        /// </summary>
        /// <param name="ioControl">The platform IOCTL implementation.</param>
        /// <returns>Whether the last Windows error was ERROR_MORE_DATA.</returns>
        private static bool IsMoreDataError(IStorageIoControl ioControl)
        {
            return ioControl is WindowsStorageIoControl windowsIo && windowsIo.LastIoControlError == ErrorMoreData;
        }

        /// <summary>
        /// Reads a Windows-layout GUID from a validated buffer range.
        /// </summary>
        /// <param name="buffer">The response containing the GUID.</param>
        /// <param name="offset">The first byte of the GUID.</param>
        /// <returns>The decoded GUID.</returns>
        private static Guid ReadGuid(byte[] buffer, int offset)
        {
            var bytes = new byte[16];
            Array.Copy(buffer, offset, bytes, 0, 16);

            return new Guid(bytes);
        }

        /// <summary>
        /// Reads a fixed-size UTF-16 field up to its terminator or field boundary.
        /// </summary>
        /// <param name="buffer">The response containing the text.</param>
        /// <param name="offset">The first byte of the field.</param>
        /// <param name="maxCharacters">The number of WCHARs reserved for the field.</param>
        /// <returns>The trimmed text.</returns>
        private static string ReadUtf16(byte[] buffer, int offset, int maxCharacters)
        {
            int length = 0;

            while (length < maxCharacters && BitConverter.ToUInt16(buffer, offset + length * 2) != 0)
            {
                ++length;
            }

            return Encoding.Unicode.GetString(buffer, offset, length * 2).Trim();
        }

        /// <summary>
        /// Reads a NUL-terminated UTF-16 string located through a disk-information offset field.
        /// </summary>
        /// <param name="buffer">The validated response.</param>
        /// <param name="length">The number of bytes actually returned by DeviceIoControl.</param>
        /// <param name="field">The offset of the DWORD holding the string's byte offset.</param>
        /// <returns>The string, or empty when absent, out of range or not terminated.</returns>
        private static string ReadTableString(byte[] buffer, int length, int field)
        {
            uint offset = BitConverter.ToUInt32(buffer, field);
            uint end    = Math.Min((uint)length, BitConverter.ToUInt32(buffer, 4));

            // Strings follow the offset table. Zero marks an absent string.
            if (offset < MemberStringTableEnd || offset >= end || offset % sizeof(char) != 0)
            {
                return string.Empty;
            }

            int maxCharacters = (int)(end - offset) / sizeof(char);
            int characters    = 0;

            while (characters < maxCharacters && BitConverter.ToUInt16(buffer, (int)offset + characters * sizeof(char)) != 0)
            {
                ++characters;
            }

            // Reject text that runs to the end of the response without a terminator.
            if (characters == maxCharacters)
            {
                return string.Empty;
            }

            return Encoding.Unicode.GetString(buffer, (int)offset, characters * sizeof(char)).Trim();
        }

        /// <summary>
        /// Creates a Spaceport request containing its length, pool GUID, and optional member GUID.
        /// </summary>
        /// <param name="size">The request size required by the selected IOCTL.</param>
        /// <param name="poolID">The pool identifier.</param>
        /// <param name="memberID">The optional member identifier.</param>
        /// <returns>The initialized request bytes.</returns>
        private static byte[] CreateRequest(int size, Guid poolID, Guid? memberID = null)
        {
            // Request DWORD 0 is the request length. The pool GUID begins at byte 4 and the
            // member GUID, when present, begins at byte 20; unused bytes remain zero.
            var buffer = new byte[size];
            BitConverter.GetBytes(size).CopyTo(buffer, 0);

            poolID.ToByteArray().CopyTo(buffer, 4);

            if (memberID.HasValue)
            {
                memberID.Value.ToByteArray().CopyTo(buffer, 20);
            }

            return buffer;
        }

        /// <summary>
        /// Creates the Spaceport request for a pool's virtual disk list.
        /// </summary>
        /// <param name="poolID">The identifier of the pool to query.</param>
        /// <returns>The initialized 0x44-byte list request.</returns>
        private static byte[] CreateSpaceListRequest(Guid poolID)
        {
            var buffer = CreateRequest(SpaceListRequestSize, poolID);

            // SpaceAgent.exe sets byte 0x14 to 1 and the WORD at 0x15
            // to 2 before sending E70804. A zeroed 0x40-byte request returns BAD_LENGTH.
            buffer[0x14] = 1;

            BitConverter.GetBytes((ushort)2).CopyTo(buffer, 0x15);

            return buffer;
        }

        #endregion
    }
}
