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
    /// A summary derived from the complete, on-demand extent table of a storage space.
    /// The disk objects are references to already detected pool members.
    /// </summary>
    public sealed class StorageSpaceExtentInfo
    {
        #region Constructor

        /// <summary>
        /// Initializes extent information from a validated Spaceport response.
        /// </summary>
        /// <param name="extentCount">The total number of extent records.</param>
        /// <param name="diskIDs">The distinct disk identifiers found in the records.</param>
        /// <param name="disks">The already detected disks matched to those identifiers.</param>
        internal StorageSpaceExtentInfo(uint extentCount, List<Guid> diskIDs, List<StorageDevice> disks)
        {
            ExtentCount = extentCount;
            DiskIDs     = new ReadOnlyCollection<Guid>(new List<Guid>(diskIDs));
            Disks       = new ReadOnlyCollection<StorageDevice>(new List<StorageDevice>(disks));
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the total number of extent records in the complete Spaceport response.
        /// </summary>
        public uint ExtentCount { get; }

        /// <summary>
        /// Gets the distinct physical disk identifiers referenced by the extents.
        /// A pool member without an extent in this space is absent.
        /// </summary>
        public IReadOnlyList<Guid> DiskIDs { get; }

        /// <summary>
        /// Gets references to already detected disks matched to the extent disk identifiers.
        /// Missing or unresolved disks have no object in this list.
        /// </summary>
        public IReadOnlyList<StorageDevice> Disks { get; }

        #endregion
    }
}
