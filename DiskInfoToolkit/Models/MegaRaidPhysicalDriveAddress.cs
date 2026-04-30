/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Models
{
    internal sealed class MegaRaidPhysicalDriveAddress
    {
        #region Properties

        public ushort DeviceId { get; set; }

        public ushort EnclosureDeviceId { get; set; }

        public byte EnclosureIndex { get; set; }

        public byte Slot { get; set; }

        public byte ScsiDeviceType { get; set; }

        public byte ConnectPortBitmap { get; set; }

        public ulong SasAddress0 { get; set; }

        public ulong SasAddress1 { get; set; }

        #endregion
    }
}
