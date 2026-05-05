/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Constants
{
    public static class IoAccess
    {
        #region Fields

        public const uint None = 0x00000000;

        public const uint GenericRead = 0x80000000;

        public const uint GenericWrite = 0x40000000;

        public const uint ReadAttributes = 0x00000080;

        #endregion
    }
}
