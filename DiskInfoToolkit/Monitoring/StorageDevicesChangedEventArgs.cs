/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;

namespace DiskInfoToolkit.Monitoring
{
    /// <summary>
    /// Defines the event arguments for storage devices changed events.
    /// </summary>
    public sealed class StorageDevicesChangedEventArgs : EventArgs
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of <see cref="StorageDevicesChangedEventArgs"/> class.
        /// </summary>
        public StorageDevicesChangedEventArgs()
        {
            Added = new List<StorageDevice>();
            Removed = new List<StorageDevice>();
            Updated = new List<StorageDevice>();
            Current = new List<StorageDevice>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the collection of storage devices that have been added.
        /// </summary>
        public List<StorageDevice> Added { get; set; }

        /// <summary>
        /// Gets or sets the collection of storage devices that have been removed.
        /// </summary>
        public List<StorageDevice> Removed { get; set; }

        /// <summary>
        /// Gets or sets the collection of storage devices that have been updated.
        /// </summary>
        public List<StorageDevice> Updated { get; set; }

        /// <summary>
        /// Gets or sets the collection of storage devices that are currently present after the changes.
        /// </summary>
        public List<StorageDevice> Current { get; set; }

        /// <summary>
        /// Gets a value indicating whether any added, removed or updated devices are present.
        /// </summary>
        public bool HasChanges
        {
            get
            {
                return (Added != null && Added.Count > 0)
                    || (Removed != null && Removed.Count > 0)
                    || (Updated != null && Updated.Count > 0);
            }
        }

        #endregion
    }
}
