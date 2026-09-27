/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

namespace DiskInfoToolkit
{
    /// <summary>
    /// An operational status of a Microsoft Storage Spaces virtual disk (storage space).
    /// A space can report several at once, such as Degraded and Incomplete.
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
        /// The space is being repaired or regenerated.
        /// </summary>
        InService,

        /// <summary>
        /// The space is accessible but has lost redundancy.
        /// </summary>
        Degraded,

        /// <summary>
        /// The space is not accessible, for example after losing more disks than it can tolerate.
        /// </summary>
        Detached,

        /// <summary>
        /// Some of the space's data is missing a copy until a repair completes.
        /// </summary>
        Incomplete

        #endregion
    }
}
