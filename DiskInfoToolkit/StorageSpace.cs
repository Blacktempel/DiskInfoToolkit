/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using System.Collections.ObjectModel;

namespace DiskInfoToolkit
{
    /// <summary>
    /// A virtual disk (storage space) created within a Microsoft Storage Spaces pool.
    /// </summary>
    public sealed class StorageSpace
    {
        #region Constructor

        /// <summary>
        /// Initializes a storage space from a validated Spaceport response.
        /// </summary>
        /// <param name="poolID">The identifier of the containing pool.</param>
        /// <param name="spaceID">The identifier of this storage space.</param>
        /// <param name="name">The friendly name of this storage space.</param>
        /// <param name="description">The description of this storage space.</param>
        /// <param name="sizeBytes">The virtual capacity in bytes.</param>
        /// <param name="allocatedBytes">The logical capacity provisioned for this space.</param>
        /// <param name="footprintOnPoolBytes">The physical pool capacity consumed by this space.</param>
        internal StorageSpace(Guid poolID, Guid spaceID, string name, string description, ulong sizeBytes,
            ulong? allocatedBytes = null, ulong? footprintOnPoolBytes = null)
        {
            PoolID               = poolID;
            ID                   = spaceID;
            Name                 = name;
            Description          = description;
            SizeBytes            = sizeBytes;
            AllocatedBytes       = allocatedBytes;
            FootprintOnPoolBytes = footprintOnPoolBytes;
            ExtentDiskIDs        = new ReadOnlyCollection<Guid>(new List<Guid>());
            ExtentDisks          = new ReadOnlyCollection<StorageDevice>(new List<StorageDevice>());
            OperationalStatus    = new ReadOnlyCollection<StorageSpaceOperationalStatus>(new List<StorageSpaceOperationalStatus>());
            RawOperationalStatus = new ReadOnlyCollection<uint>(new List<uint>());
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the identifier of the containing pool.
        /// </summary>
        public Guid PoolID { get; }

        /// <summary>
        /// Gets the storage space identifier reported by Spaceport.
        /// </summary>
        public Guid ID { get; }

        /// <summary>
        /// Gets the friendly name reported by Spaceport.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the description reported by Spaceport.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the virtual capacity in bytes reported by Spaceport.
        /// </summary>
        public ulong SizeBytes { get; }

        /// <summary>
        /// Gets the logical bytes provisioned for this space. This is not file-system usage.
        /// </summary>
        public ulong? AllocatedBytes { get; }

        /// <summary>
        /// Gets the physical bytes consumed in the pool, including redundancy.
        /// </summary>
        public ulong? FootprintOnPoolBytes { get; }

        /// <summary>
        /// Gets the combined capacity of readable mounted file-system volumes on this space.
        /// Unmounted partitions and unformatted virtual capacity are excluded.
        /// </summary>
        public ulong? MountedVolumeSizeBytes { get; private set; }

        /// <summary>
        /// Gets the combined free bytes on readable mounted volumes on this space.
        /// </summary>
        public ulong? MountedVolumeFreeBytes { get; private set; }

        /// <summary>
        /// Gets the combined used bytes on readable mounted volumes on this space.
        /// </summary>
        public ulong? MountedVolumeUsedBytes
        {
            get
            {
                if (MountedVolumeSizeBytes.HasValue && MountedVolumeFreeBytes.HasValue)
                {
                    return MountedVolumeSizeBytes.Value - MountedVolumeFreeBytes.Value;
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets the number of mounted volumes included in the file-system capacity values.
        /// </summary>
        public int MountedVolumeCount { get; private set; }

        /// <summary>
        /// Gets the distinct physical disk identifiers found in this space's allocation extents.
        /// A pool member with no extent in this space is absent from this list.
        /// </summary>
        public IReadOnlyList<Guid> ExtentDiskIDs { get; private set; }

        /// <summary>
        /// Gets references to already detected disks that could be matched to the extent disk identifiers.
        /// An unresolved or disconnected disk has no object in this list.
        /// </summary>
        public IReadOnlyList<StorageDevice> ExtentDisks { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this space's extent disk identifiers were read from Spaceport.
        /// </summary>
        public bool ExtentInformationAvailable { get; private set; }

        /// <summary>
        /// Gets the active repair task's byte counters and calculated progress or null when
        /// Spaceport reports no running repair task.
        /// </summary>
        public StorageRepairProgress RepairProgress { get; private set; }

        /// <summary>
        /// Space health for verified Spaceport values. Other values remain unknown.
        /// A space can be degraded while its pool reports healthy, for example while a repair
        /// is pending after a lost disk has returned.
        /// </summary>
        public StoragePoolHealthStatus HealthStatus
        {
            get
            {
                // Same scale as the pool status pair, verified against WMI across healthy,
                // degraded, repairing and detached spaces.
                switch (RawHealthStatus)
                {
                    case 3:
                        return StoragePoolHealthStatus.Healthy;
                    case 2:
                        return StoragePoolHealthStatus.Warning;
                    case 1:
                        return StoragePoolHealthStatus.Unhealthy;
                    default:
                        return StoragePoolHealthStatus.Unknown;
                }
            }
        }

        /// <summary>
        /// Gets the operational statuses reported by Spaceport, most significant first, in the
        /// same order Windows reports them (for example Degraded, Incomplete, InService).
        /// Empty when no status was read.
        /// </summary>
        public IReadOnlyList<StorageSpaceOperationalStatus> OperationalStatus { get; private set; }

        internal uint RawHealthStatus { get; private set; }

        internal IReadOnlyList<uint> RawOperationalStatus { get; private set; }

        #endregion

        #region Internal

        /// <summary>
        /// Stores capacity read from mounted file-system volumes of the matching virtual disk.
        /// </summary>
        /// <param name="totalBytes">Combined file-system capacity.</param>
        /// <param name="freeBytes">Combined free file-system capacity.</param>
        /// <param name="volumeCount">Number of included mounted volumes.</param>
        internal void SetMountedVolumeCapacity(ulong totalBytes, ulong freeBytes, int volumeCount)
        {
            if (freeBytes > totalBytes || volumeCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(freeBytes));
            }

            MountedVolumeSizeBytes = totalBytes;
            MountedVolumeFreeBytes = freeBytes;
            MountedVolumeCount     = volumeCount;
        }

        /// <summary>
        /// Stores the distinct extent disk identifiers and references to supplied disk objects.
        /// </summary>
        /// <param name="IDs">The physical disk identifiers found in this space's extents.</param>
        /// <param name="devices">The matching objects from the caller's disk list.</param>
        internal void SetExtentDisks(List<Guid> IDs, List<StorageDevice> devices)
        {
            // Keep the StorageDevice instances themselves by reference. A caller cannot change
            // the association lists after this snapshot has been returned.
            ExtentDiskIDs = new ReadOnlyCollection<Guid>(new List<Guid>(IDs));
            ExtentDisks   = new ReadOnlyCollection<StorageDevice>(new List<StorageDevice>(devices));

            ExtentInformationAvailable = true;
        }

        /// <summary>
        /// Stores the validated progress snapshot of a running repair task.
        /// </summary>
        /// <param name="progress">The repair progress snapshot.</param>
        internal void SetRepairProgress(StorageRepairProgress progress)
        {
            RepairProgress = progress;
        }

        /// <summary>
        /// Stores the raw health and operational status values read from Spaceport.
        /// </summary>
        /// <param name="health">The raw health value.</param>
        /// <param name="operationalStatus">The raw operational status values, most significant first.</param>
        internal void SetStatus(uint health, List<uint> operationalStatus)
        {
            RawHealthStatus      = health;
            RawOperationalStatus = new ReadOnlyCollection<uint>(new List<uint>(operationalStatus));
            OperationalStatus    = new ReadOnlyCollection<StorageSpaceOperationalStatus>(
                operationalStatus.Select(ToOperationalStatus).ToList());
        }

        #endregion

        #region Private

        /// <summary>
        /// Maps a verified Spaceport operational status code. Other codes remain unknown.
        /// </summary>
        /// <param name="value">The raw status code.</param>
        /// <returns>The operational status.</returns>
        private static StorageSpaceOperationalStatus ToOperationalStatus(uint value)
        {
            switch (value)
            {
                case 7:
                    return StorageSpaceOperationalStatus.OK;
                case 5:
                    return StorageSpaceOperationalStatus.InService;
                case 4:
                    return StorageSpaceOperationalStatus.Incomplete;
                case 3:
                    return StorageSpaceOperationalStatus.Degraded;
                case 1:
                    return StorageSpaceOperationalStatus.Detached;
                default:
                    return StorageSpaceOperationalStatus.Unknown;
            }
        }

        #endregion
    }
}
