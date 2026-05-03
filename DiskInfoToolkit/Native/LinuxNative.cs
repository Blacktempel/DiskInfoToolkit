/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Native
{
    internal static class LinuxNative
    {
        #region Fields

        public const int O_RDONLY = 0x0000;

        public const int O_RDWR = 0x0002;

        public const int O_NONBLOCK = 0x0800;

        public const int O_CLOEXEC = 0x80000;

        public const int SG_DXFER_NONE = -1;

        public const int SG_DXFER_TO_DEV = -2;

        public const int SG_DXFER_FROM_DEV = -3;

        public const uint SG_IO = 0x2285;

        public const uint HDIO_GET_IDENTITY = 0x030D;

        public const uint BLKGETSIZE64 = 0x80081272;

        public const uint BLKSSZGET = 0x1268;

        public const uint NVME_IOCTL_ADMIN_CMD = 0xC0484E41;

        #endregion

        #region Public

        public static SafeFileHandle OpenDevice(string path, int flags)
        {
            int fd = open(path, flags, 0);
            if (fd < 0)
            {
                return new SafeFileHandle(new IntPtr(-1), true);
            }

            return new SafeFileHandle(new IntPtr(fd), true);
        }

        [DllImport("libc", SetLastError = true)]
        public static extern int ioctl(SafeFileHandle fd, UIntPtr request, IntPtr argp);

        [DllImport("libc", SetLastError = true)]
        public static extern IntPtr readlink(string path, byte[] buffer, UIntPtr bufferSize);

        #endregion

        #region Private

        [DllImport("libc", SetLastError = true, EntryPoint = "open")]
        private static extern int open(string pathname, int flags, int mode);

        #endregion
    }
}
