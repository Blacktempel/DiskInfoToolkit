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
    /// Defines storage bus type values.
    /// </summary>
    public enum StorageBusType
    {
        Unknown = 0,
        Scsi = 1,
        Atapi = 2,
        Ata = 3,
        Ieee1394 = 4,
        Ssa = 5,
        Fibre = 6,
        Usb = 7,
        RAID = 8,
        ISCSI = 9,
        Sas = 10,
        Sata = 11,
        Sd = 12,
        Mmc = 13,
        Virtual = 14,
        FileBackedVirtual = 15,
        Spaces = 16,
        Nvme = 17,
        Scm = 18,
        Ufs = 19
    }
}
