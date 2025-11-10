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

using BlackSharp.Core.Asynchronous;
using BlackSharp.Core.Extensions;
using BlackSharp.Core.Interop.Windows.Utilities;
using DiskInfoToolkit.Events;
using DiskInfoToolkit.Internal;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Interop.Structures;
using DiskInfoToolkit.Logging;
using DiskInfoToolkit.Models;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit
{
    public delegate void StoragesChanged(StoragesChangedEventArgs args);
    delegate void InternalStoragesChanged();

    /// <summary>
    /// Manager for <see cref="Storage"/>s.
    /// </summary>
    /// <remarks>You can subscribe to changes via <see cref="StoragesChanged"/> <see langword="event"/>.</remarks>
    public static class StorageManager
    {
        #region Constructor

        static StorageManager()
        {
            _WndProc = WindowProc;

            //Start device changed listener
            _DevicesChangedThread = new Thread(DevicesChangedListener);
            _DevicesChangedThread.Name = $"{nameof(StorageManager)}.{nameof(DevicesChangedListener)}";
            _DevicesChangedThread.IsBackground = true;
            _DevicesChangedThread.Start();

            //Start message loop
            _MessageLoopThread = new Thread(MessageLoop);
            _MessageLoopThread.Name = $"{nameof(StorageManager)}.{nameof(MessageLoop)}";
            _MessageLoopThread.IsBackground = true;
            _MessageLoopThread.Start();
        }

        #endregion

        #region Fields

        static IntPtr _HiddenWindowHwnd;
        static User32.WndProc _WndProc;

        static Thread _MessageLoopThread;
        static Thread _DevicesChangedThread;

        static readonly AutoResetEvent _DevicesChangedEvent = new(false);

        static ConcurrentQueue<DeviceChangedModel> _ChangedStorages = new();

        static object _StorageLock = new();

        #endregion

        #region Properties

        public static List<Storage> _Storages = new();

        /// <summary>
        /// A collection of all detected <see cref="Storage"/>s.
        /// </summary>
        /// <remarks>This returns a copy of the internal list.</remarks>
        public static List<Storage> Storages
        {
            get
            {
                using (var guard = new LockGuard(_StorageLock))
                {
                    return new(_Storages);
                }
            }
            private set
            {
                using (var guard = new LockGuard(_StorageLock))
                {
                    _Storages = value.ToList();
                }
            }
        }

        #endregion

        #region Public

        /// <summary>
        /// Triggers a manual re-detection of all disks.
        /// </summary>
        public static void ReloadStorages()
        {
            var list = new List<Storage>();

            //Get storages
            foreach (var device in StorageDetector.GetStorageDevices())
            {
                foreach (var drive in device.StorageDeviceIDs)
                {
                    var storage = new Storage(device.Name, drive);

                    LogSimple.LogTrace($"{nameof(Storage)} {nameof(Storage.IsValid)} = {storage.IsValid}");

                    if (storage.IsValid)
                    {
                        list.Add(storage);
                    }
                }
            }

            Storages = list;
        }

        #endregion

        #region Events

        /// <summary>
        /// Notifies if a <see cref="Storage"/> has been added or removed.
        /// </summary>
        public static event StoragesChanged StoragesChanged;

        #endregion

        #region Private

        static void MessageLoop()
        {
            //Create hidden window for message loop
            if (!CreateMessageWindow())
            {
                LogSimple.LogWarn($"Could not create message window for {nameof(StorageManager)} ({User32.WM_DEVICECHANGE}).");
                return;
            }

            try
            {
                //Message loop
                while (User32.GetMessage(out MSG msg, _HiddenWindowHwnd, 0, 0) != 0)
                {
                    User32.TranslateMessage(ref msg);
                    User32.DispatchMessage(ref msg);
                }
            }
            catch (Exception e)
            {
                var str = e.FullExceptionString();

                LogSimple.LogError(str);
            }

            //Cleanup
            User32.DestroyWindow(_HiddenWindowHwnd);
            _HiddenWindowHwnd = IntPtr.Zero;
        }

        static bool CreateMessageWindow()
        {
            var wnd = new WNDCLASSEX()
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _WndProc,
                lpszClassName = "TopLevelHiddenWindowClass",
                hInstance = Kernel32.GetModuleHandle(null),
            };

            var ret = User32.RegisterClassEx(ref wnd);
            if (ret == 0)
            {
                return false;
            }

            //Message only window, not visible
            _HiddenWindowHwnd = User32.CreateWindowEx(
                                        0,
                                        wnd.lpszClassName,
                                        "HiddenWindow",
                                        0, 0, 0, 0, 0,
                                        IntPtr.Zero,
                                        IntPtr.Zero,
                                        wnd.hInstance,
                                        IntPtr.Zero);

            if (_HiddenWindowHwnd == IntPtr.Zero)
            {
                return false;
            }

            return true;
        }

        static IntPtr WindowProc(IntPtr hWnd, uint msg, ulong wParam, IntPtr lParam)
        {
            if (msg == User32.WM_DEVICECHANGE)
            {
                switch (wParam)
                {
                    //This could be an unpartitioned drive
                    case User32.DBT_DEVNODES_CHANGED:
                        _ChangedStorages.Enqueue(new DeviceChangedModel
                        {
                            StorageChangeIdentifier = StorageChangeIdentifierInternal.DevicesChanged,
                        });
                        _DevicesChangedEvent.Set();
                        break;
                    //Normal event when drive was added or removed
                    case User32.DBT_DEVICEARRIVAL:
                    case User32.DBT_DEVICEREMOVECOMPLETE:
                        var devHdrArrive = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);
                        if (devHdrArrive.dbch_devicetype == User32.DBT_DEVTYP_VOLUME)
                        {
                            var volume = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(lParam);

                            var mask = volume.dbcv_unitmask;
                            int count = 0;

                            while (mask > 1)
                            {
                                mask >>= 1;
                                ++count;
                            }

                            char driveLetter = (char)('A' + count);

                            var sci = wParam == User32.DBT_DEVICEARRIVAL
                                             ? StorageChangeIdentifierInternal.Added
                                             : StorageChangeIdentifierInternal.Removed;

                            _ChangedStorages.Enqueue(new DeviceChangedModel
                            {
                                DriveLetter = driveLetter,
                                StorageChangeIdentifier = sci,
                            });
                            _DevicesChangedEvent.Set();
                        }
                        else
                        {
                            LogSimple.LogTrace($"Received unhandled {nameof(devHdrArrive.dbch_devicetype)} = '{devHdrArrive.dbch_devicetype}'.");
                        }
                        break;
                }
            }

            return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        static void DevicesChangedListener()
        {
            while (true)
            {
                //Wait for device changes
                _DevicesChangedEvent.WaitOne();

                while (_ChangedStorages.TryDequeue(out var item))
                {
                    //Handle device change
                    switch (item.StorageChangeIdentifier)
                    {
                        case StorageChangeIdentifierInternal.Added:
                            HandleDriveWithDriveLetterAdded(item);
                            break;
                        case StorageChangeIdentifierInternal.Removed:
                            HandleDriveWithDriveLetterRemoved(item);
                            break;
                        case StorageChangeIdentifierInternal.DevicesChanged:
                            HandleUnpartitionedDrive(item);
                            break;
                    }
                }
            }
        }

        static void HandleUnpartitionedDrive(DeviceChangedModel deviceChangedModel)
        {
            var currentDevices = StorageDetector.GetStorageDevices();

            //Check for each current device if it can be identified in Storages
            //If not -> added

            var added = new List<Tuple<StorageController, List<StorageDevice>>>();

            foreach (var device in currentDevices)
            {
                List<StorageDevice> matches;

                using (var guard = new LockGuard(_StorageLock))
                {
                    matches = device.StorageDeviceIDs.Where(sdi => !_Storages
                                                        .Any(s => s.StorageController == device.Name
                                                               && s.PhysicalPath == sdi.PhysicalPath)).ToList();
                }

                if (matches.Count > 0)
                {
                    added.Add(new(device, matches));
                }
            }

            //Check for each Storage if it can be identified anywhere in current devices
            //If not -> removed

            var removed = new List<Storage>();

            using (var guard = new LockGuard(_StorageLock))
            {
                foreach (var storage in _Storages)
                {
                    //Check if storage exists in current devices
                    var match = currentDevices.Any(si =>
                    {
                        return si.Name == storage.StorageController
                            && si.StorageDeviceIDs.Any(sdi => sdi.PhysicalPath == storage.PhysicalPath);
                    });

                    //Storage does not exist in current devices, it was removed
                    if (!match)
                    {
                        removed.Add(storage);
                    }
                }
            }

            //Handle added device[s]
            foreach (var add in added)
            {
                foreach (var item in add.Item2)
                {
                    LogSimple.LogTrace($"Adding device with {nameof(item.PhysicalPath)} = '{item.PhysicalPath}'.");

                    var storage = new Storage(add.Item1.Name, item);

                    if (storage.IsValid)
                    {
                        using (var guard = new LockGuard(_StorageLock))
                        {
                            _Storages.Add(storage);
                        }

                        //Notify subscribers
                        StoragesChanged?.Invoke(new()
                        {
                            StorageChangeIdentifier = StorageChangeIdentifier.Added,
                            Storage = storage,
                        });
                    }
                    else
                    {
                        LogSimple.LogTrace($"Cannot add device '{item.PhysicalPath}' - device is invalid.");
                    }
                }
            }

            //Handle removed device[s]
            foreach (var rem in removed)
            {
                LogSimple.LogTrace($"Removed device with {nameof(rem.PhysicalPath)} = '{rem.PhysicalPath}'.");

                using (var guard = new LockGuard(_StorageLock))
                {
                    _Storages.Remove(rem);
                }

                //Notify subscribers
                StoragesChanged?.Invoke(new()
                {
                    StorageChangeIdentifier = StorageChangeIdentifier.Removed,
                    Storage = rem,
                });
            }
        }

        static void HandleDriveWithDriveLetterAdded(DeviceChangedModel deviceChangedModel)
        {
            var driveLetter = deviceChangedModel.DriveLetter.Value;

            LogSimple.LogTrace($"Adding {nameof(DEV_BROADCAST_VOLUME)} - drive letter is '{driveLetter}'.");

            //Find device with drive letter
            var si = StorageDetector.GetStorageDevice($@"\\.\{driveLetter}:");

            if (si == null || si.StorageDeviceIDs.Count == 0)
            {
                LogSimple.LogTrace($"Could not find '{driveLetter}' in '{nameof(StorageDetector)}'.");
                return;
            }

            var disk = si.StorageDeviceIDs.First();

            Storage found;
            using (var guard = new LockGuard(_StorageLock))
            {
                //Check if device already exists (avoid duplicates)
                found = _Storages.Find(s => s.StorageController == si.Name
                                         && s.PhysicalPath      == disk.PhysicalPath);
            }

            //Duplicate
            if (found != null)
            {
                return;
            }

            var storage = new Storage(si.Name, disk);

            if (storage.IsValid)
            {
                using (var guard = new LockGuard(_StorageLock))
                {
                    _Storages.Add(storage);
                }

                //Notify subscribers
                StoragesChanged?.Invoke(new()
                {
                    StorageChangeIdentifier = StorageChangeIdentifier.Added,
                    Storage = storage,
                });
            }
            else
            {
                LogSimple.LogTrace($"Cannot add device '{driveLetter}' - device is invalid.");
            }
        }

        static void HandleDriveWithDriveLetterRemoved(DeviceChangedModel deviceChangedModel)
        {
            var driveLetter = deviceChangedModel.DriveLetter.Value;

            LogSimple.LogTrace($"Removing {nameof(DEV_BROADCAST_VOLUME)} - drive letter is '{driveLetter}'.");

            var handle = SafeFileHandler.OpenHandle($@"\\.\{driveLetter}:");

            if (!SafeFileHandler.IsHandleValid(handle))
            {
                LogSimple.LogTrace($"Could not open handle for '{driveLetter}'.");
                return;
            }

            //Get drive number from drive letter
            var driveNumber = Storage.GetDriveNumber(handle);

            Storage found;
            using (var guard = new LockGuard(_StorageLock))
            {
                found = _Storages.Find(s => s.DriveNumber == driveNumber);
            }

            if (found != null)
            {
                using (var guard = new LockGuard(_StorageLock))
                {
                    _Storages.Remove(found);
                }

                //Notify subscribers
                StoragesChanged?.Invoke(new()
                {
                    StorageChangeIdentifier = StorageChangeIdentifier.Removed,
                    Storage = found,
                });
            }
        }

        #endregion
    }
}
