/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;

namespace DiskInfoToolkit.Core
{
    public interface IOptionalMiniportBackend
    {
        bool TryProbeCsmi(StorageDevice device, IStorageIoControl ioControl);
        bool TryProbeIntelRaid(StorageDevice device, IStorageIoControl ioControl);
    }
}
