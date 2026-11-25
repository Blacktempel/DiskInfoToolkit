/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Core
{
    /// <summary>
    /// Exposes the most recent storage I/O failure state for circuit-breaking fallback probes.
    /// </summary>
    internal interface IStorageIoControlErrorState
    {
        #region Properties

        uint LastIoControlCode { get; }

        int LastIoControlError { get; }

        bool LastOpenDeviceTimedOut { get; }

        bool LastIoControlWasSevereDeviceError { get; }

        #endregion
    }
}
