/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Florian K.
 *
 * Code inspiration, improvements and fixes are from, but not limited to, following projects:
 * CrystalDiskInfo
 */

using DiskInfoToolkit.Interop.Structures;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Interop
{
    internal static class User32
    {
        const string DLLNAME = "user32.dll";

        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        public const int WM_DEVICECHANGE = 0x0219;
        public const int DBT_DEVNODES_CHANGED = 0x0007;
        public const int DBT_DEVICEARRIVAL = 0x8000;
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        public const int DBT_DEVTYP_VOLUME          = 0x00000002;
        public const int DBT_DEVTYP_PORT            = 0x00000003;
        public const int DBT_DEVTYP_OEM             = 0x00000000;
        public const int DBT_DEVTYP_HANDLE          = 0x00000006;
        public const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport(DLLNAME, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

        [DllImport(DLLNAME, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
           int dwExStyle,
           [MarshalAs(UnmanagedType.LPStr)] string lpClassName,
           [MarshalAs(UnmanagedType.LPStr)] string lpWindowName,
           int dwStyle,
           int x,
           int y,
           int nWidth,
           int nHeight,
           IntPtr hWndParent,
           IntPtr hMenu,
           IntPtr hInstance,
           IntPtr lpParam);

        [DllImport(DLLNAME, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport(DLLNAME)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport(DLLNAME)]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport(DLLNAME)]
        public static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport(DLLNAME)]
        public static extern IntPtr DispatchMessage([In] ref MSG lpmsg);
    }
}
