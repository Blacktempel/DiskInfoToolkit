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
    /// The health of a Microsoft Storage Spaces pool, independent of member disk SMART health.
    /// </summary>
    public enum StoragePoolHealthStatus
    {
        #region Values

        /// <summary>
        /// The driver returned a state that has not been verified.
        /// </summary>
        Unknown,

        /// <summary>
        /// The pool is healthy.
        /// </summary>
        Healthy,

        /// <summary>
        /// The pool is accessible but degraded.
        /// </summary>
        Warning,

        /// <summary>
        /// The pool is unhealthy. No Spaceport code has been verified for this state yet.
        /// </summary>
        Unhealthy

        #endregion
    }
}
