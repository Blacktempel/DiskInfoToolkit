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
    public static class ProbeTraceRecorder
    {
        #region Public

        public static void Add(StorageDevice device, string message)
        {
            if (device == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (device.ProbeTrace == null)
            {
                device.ProbeTrace = new List<string>();
            }

            device.ProbeTrace.Add(message);
        }

        #endregion
    }
}
