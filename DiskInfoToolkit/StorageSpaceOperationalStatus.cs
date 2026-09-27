/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit
{
    /// <summary>
    /// An operational status of a Microsoft Storage Spaces virtual disk (storage space).
    /// A space can report several at once, such as <see cref="Degraded"/> and <see cref="Incomplete"/>.
    /// </summary>
    public enum StorageSpaceOperationalStatus
    {
        #region Values

        /// <summary>
        /// The driver returned a status that has not been verified.
        /// </summary>
        Unknown,

        /// <summary>
        /// The space is operating normally.
        /// </summary>
        OK,

        /// <summary>
        /// The space is being configured, maintained, cleaned or otherwise serviced.
        /// This can include repair.
        /// </summary>
        InService,

        /// <summary>
        /// The space is accessible but has lost redundancy.
        /// </summary>
        Degraded,

        /// <summary>
        /// The space is visible to Windows but is not attached as a disk device.
        /// This can be caused by policy or unavailable member disks.
        /// </summary>
        Detached,

        /// <summary>
        /// The space does not currently have enough redundancy to repair or regenerate
        /// all data. Missing member disks may need to return.
        /// </summary>
        Incomplete

        #endregion
    }
}
