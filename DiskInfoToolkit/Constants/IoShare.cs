/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Constants
{
    public static class IoShare
    {
        #region Fields

        public const uint Read = 0x00000001;

        public const uint Write = 0x00000002;

        public const uint Delete = 0x00000004;

        public const uint ReadWrite = Read | Write;

        public const uint All = Read | Write | Delete;

        #endregion
    }
}
