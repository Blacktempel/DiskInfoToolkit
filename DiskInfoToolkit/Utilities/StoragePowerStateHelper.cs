/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Native;
using Microsoft.Win32.SafeHandles;
using OS = BlackSharp.Core.Platform.OperatingSystem;

namespace DiskInfoToolkit.Utilities
{
    internal static class StoragePowerStateHelper
    {
        #region Public

        public static bool TryGetDevicePowerState(string path, out bool isPoweredOn)
        {
            isPoweredOn = false;

            if (!OS.IsWindows())
            {
                return false;
            }

            //Open device for query operations only,
            //as opening for smart read/write operations can already spin up the disk.
            SafeFileHandle handle = Kernel32Native.CreateFile(
                path,
                0,
                IoShare.Read,
                IntPtr.Zero,
                IoCreation.OpenExisting,
                0,
                IntPtr.Zero);

            if (handle == null || handle.IsInvalid)
            {
                return false;
            }

            return Kernel32Native.GetDevicePowerState(handle, out isPoweredOn);
        }

        #endregion
    }
}
