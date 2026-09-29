/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.Native
{
    internal static class Kernel32Native
    {
        #region Fields

        public const string DLL_NAME = "kernel32.dll";

        #endregion

        #region Public

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport(DLL_NAME, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDevicePowerState(SafeFileHandle device, [MarshalAs(UnmanagedType.Bool)] out bool isPoweredOn);

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint QueryDosDevice(string deviceName, char[] targetPath, int maxCharacters);

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailableToCaller, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstVolume(StringBuilder volumeName, int bufferLength);

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindNextVolume(IntPtr findHandle, StringBuilder volumeName, int bufferLength);

        [DllImport(DLL_NAME, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindVolumeClose(IntPtr findHandle);

        [DllImport(DLL_NAME, SetLastError = true)]
        public static extern bool SetFilePointerEx(SafeFileHandle device, long liDistanceToMove,
            IntPtr distanceToMoveHigh, uint dwMoveMethod);

        [DllImport(DLL_NAME, SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle device, byte[] buffer, uint numberOfBytesToRead,
            out uint numberOfBytesRead, IntPtr lpOverlapped);

        #endregion
    }
}
