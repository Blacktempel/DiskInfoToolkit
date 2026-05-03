/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using OS = BlackSharp.Core.Platform.OperatingSystem;

namespace DiskInfoToolkit.Core
{
    internal static class StorageIoControlFactory
    {
        #region Public

        public static IStorageIoControl Create()
        {
            if (OS.IsWindows())
            {
                return new WindowsStorageIoControl();
            }

            if (OS.IsLinux())
            {
                return new LinuxStorageIoControl();
            }

            throw new PlatformNotSupportedException("Storage IOCTL access is currently implemented for Windows and Linux only.");
        }

        #endregion
    }
}
