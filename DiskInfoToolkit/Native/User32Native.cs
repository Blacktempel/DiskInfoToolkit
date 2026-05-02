/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Interop;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Native
{
    internal static class User32Native
    {
        #region Fields

        public const string DLL_NAME = "user32.dll";

        public const uint WM_DEVICECHANGE = 0x0219;

        public const uint WM_APP = 0x8000;

        public const uint DBT_DEVICEARRIVAL = 0x8000;

        public const uint DBT_DEVICEREMOVECOMPLETE = 0x8004;

        public const uint DBT_DEVNODES_CHANGED = 0x0007;

        public const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;

        public const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

        #endregion

        #region Public

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport(DLL_NAME, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parentHandle, IntPtr menuHandle, IntPtr instanceHandle, IntPtr param);

        [DllImport(DLL_NAME, SetLastError = true)]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport(DLL_NAME)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport(DLL_NAME)]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport(DLL_NAME, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport(DLL_NAME)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport(DLL_NAME, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport(DLL_NAME, SetLastError = true)]
        public static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr notificationFilter, uint flags);

        [DllImport(DLL_NAME, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterDeviceNotification(IntPtr handle);

        #endregion
    }
}
