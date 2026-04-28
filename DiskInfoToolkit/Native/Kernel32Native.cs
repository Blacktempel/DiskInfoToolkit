/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

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

        [DllImport(DLL_NAME, CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport(DLL_NAME, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDevicePowerState(SafeFileHandle device, [MarshalAs(UnmanagedType.Bool)] out bool isPoweredOn);

        [DllImport(DLL_NAME, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte[] inBuffer, int inBufferSize, [Out] byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint QueryDosDevice(string deviceName, char[] targetPath, int maxCharacters);

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailableToCaller, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);

        [DllImport(DLL_NAME, SetLastError = true)]
        public static extern bool SetFilePointerEx(SafeFileHandle device, long liDistanceToMove,
            IntPtr distanceToMoveHigh, uint dwMoveMethod);

        [DllImport(DLL_NAME, SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle device, byte[] buffer, uint numberOfBytesToRead,
            out uint numberOfBytesRead, IntPtr lpOverlapped);

        public static SafeLibraryHandle LoadLibrarySafe(string fileName)
        {
            IntPtr handle = LoadLibrary(fileName);
            if (handle == IntPtr.Zero)
            {
                return new SafeLibraryHandle();
            }

            return new SafeLibraryHandle(handle, true);
        }

        #endregion

        #region Internal

        internal static bool ReleaseLibrary(IntPtr handle)
        {
            return FreeLibrary(handle);
        }

        #endregion

        #region Private

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport(DLL_NAME, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);

        #endregion
    }
}
