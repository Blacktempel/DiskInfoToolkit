/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Native;
using OS = BlackSharp.Core.Platform.OperatingSystem;

namespace DiskInfoToolkit.Utilities
{
    /// <summary>
    /// Detects CD/DVD/BD volumes without opening them.
    /// </summary>
    /// <remarks>
    /// Opening an optical volume, even briefly and without data access, can block media
    /// changes, ejects and device removal while the drive is spinning up. Optical drives
    /// are never disks, so their volumes never contain extents of a disk we monitor.
    /// </remarks>
    internal static class OpticalDeviceHelper
    {
        #region Fields

        private const string CdRomDevicePrefix = @"\Device\CdRom";

        private const int TargetBufferCharacters = 1024;

        #endregion

        #region Internal

        /// <summary>
        /// Checks whether a drive letter is mapped to an optical drive.
        /// </summary>
        /// <param name="driveLetter">The drive letter to check.</param>
        /// <returns>Whether the drive letter points to a CD-ROM class device.</returns>
        internal static bool IsOpticalDriveLetter(char driveLetter)
        {
            return IsOpticalDosDevice($"{driveLetter}:");
        }

        /// <summary>
        /// Checks whether a DOS device name (for example "F:" or "Volume{GUID}") is mapped to an optical drive.
        /// </summary>
        /// <param name="dosDeviceName">The DOS device name without the "\\.\" or "\\?\" prefix.</param>
        /// <returns>Whether the name points to a CD-ROM class device.</returns>
        internal static bool IsOpticalDosDevice(string dosDeviceName)
        {
            if (string.IsNullOrWhiteSpace(dosDeviceName) || !OS.IsWindows())
            {
                return false;
            }

            var targetPath = new char[TargetBufferCharacters];

            //QueryDosDevice only reads the object manager's symbolic link, it does not touch the device
            uint length = Kernel32Native.QueryDosDevice(dosDeviceName, targetPath, targetPath.Length);

            if (length == 0)
            {
                return false;
            }

            int terminator = Array.IndexOf(targetPath, '\0');
            string target = terminator > 0 ? new string(targetPath, 0, terminator) : new string(targetPath, 0, (int)Math.Min(length, (uint)targetPath.Length));

            return IsOpticalDeviceName(target);
        }

        /// <summary>
        /// Checks whether an NT device name belongs to an optical drive.
        /// </summary>
        /// <param name="ntDeviceName">An NT device name such as "\Device\CdRom0".</param>
        /// <returns>Whether the name is a CD-ROM class device.</returns>
        internal static bool IsOpticalDeviceName(string ntDeviceName)
        {
            return ntDeviceName != null
                && ntDeviceName.StartsWith(CdRomDevicePrefix, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
