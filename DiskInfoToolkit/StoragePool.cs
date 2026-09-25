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
    /// A Microsoft Storage Spaces pool. Member devices are references to the supplied disk list;
    /// this object does not own or probe separate <see cref="StorageDevice"/> instances.
    /// </summary>
    public sealed class StoragePool
    {
        #region Constructor

        /// <summary>
        /// Initializes a Storage Spaces pool from a validated Spaceport response.
        /// </summary>
        /// <param name="poolID">The pool identifier returned by Spaceport.</param>
        /// <param name="name">The friendly name of the pool.</param>
        /// <param name="description">The description of the pool.</param>
        /// <param name="isPrimordial">Whether this is the primordial pool.</param>
        /// <param name="sizeBytes">The total physical capacity in bytes.</param>
        /// <param name="allocatedBytes">The allocated physical capacity in bytes.</param>
        /// <param name="configuredMemberCount">The number of configured members.</param>
        /// <param name="rawStatusA20">The unmodified status field at response offset 0xA20.</param>
        /// <param name="rawStatusA24">The unmodified status field at response offset 0xA24.</param>
        internal StoragePool(Guid poolID, string name, string description, bool isPrimordial,
            ulong sizeBytes, ulong allocatedBytes, uint configuredMemberCount, uint rawStatusA20, uint rawStatusA24)
        {
            ID                      = poolID;
            Name                    = name;
            Description             = description;
            IsPrimordial            = isPrimordial;
            SizeBytes               = sizeBytes;
            AllocatedBytes          = allocatedBytes;
            ConfiguredMemberCount   = configuredMemberCount;
            RawStatusA20            = rawStatusA20;
            RawStatusA24            = rawStatusA24;

            MemberDiskIDs = new ReadOnlyCollection<Guid>(new List<Guid>());
            Members       = new ReadOnlyCollection<StorageDevice>(new List<StorageDevice>());
            Spaces        = new ReadOnlyCollection<StorageSpace>(new List<StorageSpace>());
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the pool identifier reported by Spaceport.
        /// </summary>
        public Guid ID { get; }

        /// <summary>
        /// Gets the friendly name reported by Spaceport.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the pool description reported by Spaceport.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets a value indicating whether this is the system's primordial pool.
        /// </summary>
        public bool IsPrimordial { get; }

        /// <summary>
        /// Gets the total physical capacity in bytes.
        /// </summary>
        public ulong SizeBytes { get; }

        /// <summary>
        /// Gets the physical capacity allocated in bytes.
        /// </summary>
        public ulong AllocatedBytes { get; }

        /// <summary>
        /// Gets the unallocated physical capacity in bytes when the reported values are consistent.
        /// </summary>
        public ulong? FreeBytes => AllocatedBytes <= SizeBytes ? SizeBytes - AllocatedBytes : (ulong?)null;

        /// <summary>
        /// Gets the number of members configured in a non-primordial pool, including disconnected disks.
        /// </summary>
        public uint ConfiguredMemberCount { get; }

        /// <summary>
        /// Pool health for verified Spaceport status pairs. Other values remain unknown.
        /// This is independent of the SMART health of any member disk.
        /// </summary>
        public StoragePoolHealthStatus HealthStatus
        {
            get
            {
                // Both raw DWORDs changed together during the controlled removal and returned
                // to their initial values after reconnection. Mixed or unseen pairs stay unknown.
                if (RawStatusA20 == 3 && RawStatusA24 == 3)
                {
                    return StoragePoolHealthStatus.Healthy;
                }

                if (RawStatusA20 == 2 && RawStatusA24 == 2)
                {
                    return StoragePoolHealthStatus.Warning;
                }

                return StoragePoolHealthStatus.Unknown;
            }
        }

        /// <summary>
        /// Gets the identifiers of the currently reported members. Disconnected disks may be absent from this list.
        /// </summary>
        public IReadOnlyList<Guid> MemberDiskIDs { get; private set; }

        /// <summary>
        /// Gets the already detected disk objects matched to this pool by serial number.
        /// </summary>
        public IReadOnlyList<StorageDevice> Members { get; private set; }

        /// <summary>
        /// Gets a value indicating whether member identifiers could be read from Spaceport.
        /// </summary>
        public bool MemberInformationAvailable { get; private set; }

        /// <summary>
        /// Gets the storage spaces reported for this pool. A pool can contain multiple spaces.
        /// </summary>
        public IReadOnlyList<StorageSpace> Spaces { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the storage space list could be read from Spaceport.
        /// </summary>
        public bool SpaceInformationAvailable { get; private set; }

        /// <summary>
        /// Gets the byte-weighted repair progress of running tasks in this pool or null when
        /// none of its storage spaces reports an active repair task.
        /// </summary>
        public double? RepairProgressPercent
        {
            get
            {
                decimal processedBytes = 0;
                decimal totalBytes = 0;

                foreach (var space in Spaces)
                {
                    if (space.RepairProgress != null)
                    {
                        processedBytes += space.RepairProgress.ProcessedBytes;
                        totalBytes     += space.RepairProgress.TotalBytes;
                    }
                }

                return totalBytes > 0 ? (double)(processedBytes * 100 / totalBytes) : null;
            }
        }

        /// <summary>
        /// Gets the first raw pool status field used for the verified status pair.
        /// </summary>
        internal uint RawStatusA20 { get; }

        /// <summary>
        /// Gets the second raw pool status field used for the verified status pair.
        /// </summary>
        internal uint RawStatusA24 { get; }

        #endregion

        #region Internal

        /// <summary>
        /// Stores the reported member identifiers and references to already detected disk objects.
        /// </summary>
        /// <param name="IDs">The member identifiers returned by Spaceport.</param>
        /// <param name="devices">The matching objects from the caller's disk list.</param>
        internal void SetMembers(List<Guid> IDs, List<StorageDevice> devices)
        {
            // Copy the collections so a later caller cannot change this pool snapshot.
            // The disk objects themselves are kept by reference; no second StorageDevice is constructed.
            MemberDiskIDs = new ReadOnlyCollection<Guid>(new List<Guid>(IDs));
            Members       = new ReadOnlyCollection<StorageDevice>(new List<StorageDevice>(devices));

            MemberInformationAvailable = true;
        }

        /// <summary>
        /// Stores a snapshot of the storage spaces read for this pool.
        /// </summary>
        /// <param name="spaces">The storage spaces returned by Spaceport.</param>
        internal void SetSpaces(List<StorageSpace> spaces)
        {
            Spaces = new ReadOnlyCollection<StorageSpace>(new List<StorageSpace>(spaces));

            SpaceInformationAvailable = true;
        }

        #endregion
    }
}
