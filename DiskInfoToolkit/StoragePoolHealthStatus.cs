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
    /// The health of a Microsoft Storage Spaces pool or storage space, independent of member disk SMART health.
    /// </summary>
    public enum StoragePoolHealthStatus
    {
        #region Values

        /// <summary>
        /// The driver returned a state that has not been verified.
        /// </summary>
        Unknown,

        /// <summary>
        /// The pool or space is healthy.
        /// </summary>
        Healthy,

        /// <summary>
        /// The pool or space is accessible but degraded.
        /// </summary>
        Warning,

        /// <summary>
        /// The pool or space is unhealthy, for example read-only after losing more disks than it can tolerate.
        /// </summary>
        Unhealthy

        #endregion
    }
}
