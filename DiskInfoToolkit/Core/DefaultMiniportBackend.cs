/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Probes;

namespace DiskInfoToolkit.Core
{
    public sealed class DefaultMiniportBackend : IOptionalMiniportBackend
    {
        #region Public

        public bool TryProbeCsmi(StorageDevice device, IStorageIoControl ioControl)
        {
            return CsmiProbe.TryPopulateDriverInfo(device, ioControl);
        }

        public bool TryProbeIntelRaid(StorageDevice device, IStorageIoControl ioControl)
        {
            return IntelNvmeProbe.TryPopulateIntelNvmeData(device, ioControl);
        }

        #endregion
    }
}
