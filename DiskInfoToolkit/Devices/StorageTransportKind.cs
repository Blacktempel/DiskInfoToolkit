/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Devices
{
    /// <summary>
    /// Defines storage transport kind values.
    /// </summary>
    public enum StorageTransportKind
    {
        Unknown,
        Ata,
        Scsi,
        Nvme,
        Usb,
        Sd,
        Mmc,
        Raid,
        Sas,
        Ahci,
        Virtual
    }
}
