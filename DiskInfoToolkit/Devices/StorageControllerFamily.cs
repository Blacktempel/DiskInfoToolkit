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
    /// Defines storage controller family values.
    /// </summary>
    public enum StorageControllerFamily
    {
        Unknown,
        Generic,
        UsbStor,
        UaspStor,
        StorNvme,
        IntelRst,
        IntelVroc,
        MegaRaid,
        LsiSas,
        RocketRaid,
        RealtekSd,
        Ahci,
        AmdSata,
        AsusBridge,
        VirtualDisk
    }
}
