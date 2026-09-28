/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Smart
{
    /// <summary>
    /// Defines the summarized SMART health status of a storage device.
    /// </summary>
    public enum StorageHealthStatus
    {
        /// <summary>
        /// The device health is good.
        /// </summary>
        Good,

        /// <summary>
        /// The device health should be monitored.
        /// </summary>
        Caution,

        /// <summary>
        /// The device health is critically low, but not yet in a failed state.
        /// </summary>
        Warning,

        /// <summary>
        /// The device health indicates a failed or failing state.
        /// </summary>
        Bad,
    }
}
