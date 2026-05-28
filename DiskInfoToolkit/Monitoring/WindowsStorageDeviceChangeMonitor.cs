/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Native;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Monitoring
{
    internal static class WindowsStorageDeviceChangeMonitor
    {
        #region Fields

        private const string MessageWindowClassName = nameof(Storage) + "TopLevelHiddenWindowClass";

        private const string MessageWindowTitle = nameof(Storage) + "MessageWindow";

        private const uint StopMonitoringMessage = User32Native.WM_APP + 0x03D1;

        private static readonly object SyncRoot = new object();

        private static IntPtr _messageWindow;

        private static IntPtr _volumeNotificationHandle;

        private static IntPtr _diskNotificationHandle;

        private static StorageWindowProc _windowProcDelegate;

        private static Action _queueRescan;

        #endregion

        #region Public

        public static void Run(WaitHandle stopSignal, Action queueRescan)
        {
            if (stopSignal == null)
            {
                throw new ArgumentNullException(nameof(stopSignal));
            }

            if (queueRescan == null)
            {
                throw new ArgumentNullException(nameof(queueRescan));
            }

            lock (SyncRoot)
            {
                _queueRescan = queueRescan;

                if (_windowProcDelegate == null)
                {
                    _windowProcDelegate = WindowProc;
                }
            }

            if (!CreateMessageWindow())
            {
                ResetState();
                return;
            }

            try
            {
                while (!stopSignal.WaitOne(0) && User32Native.GetMessage(out var msg, GetMessageWindow(), 0, 0) > 0)
                {
                    User32Native.TranslateMessage(ref msg);
                    User32Native.DispatchMessage(ref msg);
                }
            }
            finally
            {
                DestroyMessageWindow();
                ResetState();
            }
        }

        public static void RequestStop()
        {
            IntPtr messageWindow = GetMessageWindow();
            if (messageWindow != IntPtr.Zero)
            {
                User32Native.PostMessage(messageWindow, StopMonitoringMessage, UIntPtr.Zero, IntPtr.Zero);
            }
        }

        #endregion

        #region Private

        private static IntPtr GetMessageWindow()
        {
            lock (SyncRoot)
            {
                return _messageWindow;
            }
        }

        private static bool CreateMessageWindow()
        {
            var wnd = new WNDCLASSEX();
            wnd.cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>();
            wnd.lpfnWndProc = _windowProcDelegate;
            wnd.lpszClassName = MessageWindowClassName;
            wnd.hInstance = Kernel32Native.GetModuleHandle(null);

            //RegisterClassEx returns 0 when the class is already registered in this process.
            //CreateWindowEx below is the authoritative success check and also keeps restart-after-stop working.
            User32Native.RegisterClassEx(ref wnd);

            IntPtr messageWindow = User32Native.CreateWindowEx(
                0,
                wnd.lpszClassName,
                MessageWindowTitle,
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                wnd.hInstance,
                IntPtr.Zero);

            if (messageWindow == IntPtr.Zero)
            {
                return false;
            }

            lock (SyncRoot)
            {
                _messageWindow = messageWindow;
            }

            RegisterStorageNotifications();
            return true;
        }

        private static void DestroyMessageWindow()
        {
            UnregisterStorageNotifications();

            IntPtr messageWindow;
            lock (SyncRoot)
            {
                messageWindow = _messageWindow;
                _messageWindow = IntPtr.Zero;
            }

            if (messageWindow != IntPtr.Zero)
            {
                User32Native.DestroyWindow(messageWindow);
            }
        }

        private static IntPtr WindowProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam)
        {
            if (msg == User32Native.WM_DEVICECHANGE)
            {
                uint eventCode = unchecked((uint)wParam.ToUInt64());

                if (eventCode == User32Native.DBT_DEVICEARRIVAL
                 || eventCode == User32Native.DBT_DEVICEREMOVECOMPLETE
                 || eventCode == User32Native.DBT_DEVNODES_CHANGED)
                {
                    QueueRescanSafely(GetQueueRescan());
                }
            }

            return User32Native.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private static Action GetQueueRescan()
        {
            lock (SyncRoot)
            {
                return _queueRescan;
            }
        }

        private static void RegisterStorageNotifications()
        {
            UnregisterStorageNotifications();

            //Register for volume and disk interface notifications
            _volumeNotificationHandle = RegisterDeviceInterfaceNotification(DeviceInterfaceGuids.Volume);
            _diskNotificationHandle = RegisterDeviceInterfaceNotification(DeviceInterfaceGuids.Disk);
        }

        private static void UnregisterStorageNotifications()
        {
            if (_volumeNotificationHandle != IntPtr.Zero)
            {
                User32Native.UnregisterDeviceNotification(_volumeNotificationHandle);
                _volumeNotificationHandle = IntPtr.Zero;
            }

            if (_diskNotificationHandle != IntPtr.Zero)
            {
                User32Native.UnregisterDeviceNotification(_diskNotificationHandle);
                _diskNotificationHandle = IntPtr.Zero;
            }
        }

        private static IntPtr RegisterDeviceInterfaceNotification(Guid classGuid)
        {
            IntPtr messageWindow = GetMessageWindow();
            if (messageWindow == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var filter = new DEV_BROADCAST_DEVICEINTERFACE();
            filter.dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>();
            filter.dbcc_devicetype = User32Native.DBT_DEVTYP_DEVICEINTERFACE;
            filter.dbcc_reserved = 0;
            filter.dbcc_classguid = classGuid;
            filter.dbcc_name = 0;

            var filterPtr = Marshal.AllocHGlobal(filter.dbcc_size);
            try
            {
                Marshal.StructureToPtr(filter, filterPtr, false);
                return User32Native.RegisterDeviceNotification(messageWindow, filterPtr, User32Native.DEVICE_NOTIFY_WINDOW_HANDLE);
            }
            finally
            {
                Marshal.FreeHGlobal(filterPtr);
            }
        }

        private static void QueueRescanSafely(Action queueRescan)
        {
            if (queueRescan == null)
            {
                return;
            }

            try
            {
                queueRescan();
            }
            catch
            {
            }
        }

        private static void ResetState()
        {
            lock (SyncRoot)
            {
                _messageWindow = IntPtr.Zero;
                _volumeNotificationHandle = IntPtr.Zero;
                _diskNotificationHandle = IntPtr.Zero;
                _queueRescan = null;
            }
        }

        #endregion
    }
}
