/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Constants
{
    internal static class MegaRaidMiniportConstants
    {
        #region Fields

        public const int MaximumPhysicalDriveCount = 240;

        public const int MailboxLength = 12;

        public const int SenseBufferLengthForPassThrough = 112;

        public const int SenseBufferLengthForDcmd = 120;

        public const int DataBufferLength = 4096;

        #endregion
    }
}
