/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Constants
{
    /// <summary>
    /// Flags used when opening Windows storage device handles.
    /// </summary>
    public static class IoFlags
    {
        #region Fields

        /// <summary>
        /// Requests normal file attributes when opening a Windows device handle.
        /// </summary>
        public const uint Normal = 0x00000080;

        /// <summary>
        /// Enables asynchronous I/O for a Windows device handle.
        /// </summary>
        public const uint Overlapped = 0x40000000;

        #endregion
    }
}
