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

        const int MaxDrives = 64;

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
                //Iterate drives for each storage controller
                foreach (var drive in device.StorageDeviceIDs)
                {
                    if (CreateStorage(device.Name, drive, out var storage))
                    {
                        list.Add(storage);
                    }
                }
            }

            //Check for drives that may exist but have not yet been detected (e.g. ReFS)
            for (int i = 0; i < MaxDrives; ++i)
            {
                //Skip already detected drives
                if (list.Any(s => s.DriveNumber == i))
                {
                    continue;
                }

                //Simple physical path
                var path = $@"\\.\PhysicalDrive{i}";

                var handle = SafeFileHandler.OpenHandle(path);

                //Handle invalid
                if (!SafeFileHandler.IsHandleValid(handle))
                {
                    continue;
                }

                //Handle valid -> close and continue
                SafeFileHandler.CloseHandle(handle);

                LogSimple.LogDebug($"Creating {nameof(Storage)} for '{path}'.");

                var drive = new StorageDevice
                {
                    DeviceID = string.Empty,
                    HardwareID = string.Empty,
                    PhysicalPath = path,
                    DriveNumber = i,
                };

                if (CreateStorage(string.Empty, drive, out var storage))
                {
                    list.Add(storage);
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

        static bool CreateStorage(string storageController, StorageDevice storageDevice, out Storage storage)
        {
            storage = new Storage(storageController, storageDevice);

            LogSimple.LogTrace($"{nameof(Storage)} {nameof(Storage.IsValid)} = {storage.IsValid}");

            return storage.IsValid;
        }

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

        static IntPtr WindowProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam)
        {
            var wparamInt = wParam.ToUInt64();

            if (msg == User32.WM_DEVICECHANGE)
            {
                switch (wparamInt)
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

                            var sci = wparamInt == User32.DBT_DEVICEARRIVAL
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

            //Snapshot current storages
            List<Storage> storagesSnapshot;

            using (var guard = new LockGuard(_StorageLock))
            {
                storagesSnapshot = new(_Storages);
            }

            //Prefer DriveNumber as stable identity across different PhysicalPath representations
            //(e.g. \\?\scsi#... vs \\.\PhysicalDriveN).
            var existingByDriveNumber = storagesSnapshot
                .Where(s => s.DriveNumber >= 0)
                .GroupBy(s => s.DriveNumber)
                .ToDictionary(g => g.Key, g => g.First());

            //Build a unique set of currently detected drives (DriveNumber -> candidate StorageDevice)
            var currentByDriveNumber = new Dictionary<int, (StorageController Controller, StorageDevice Device)>();
            foreach (var device in currentDevices)
            {
                //Iterate drives for each storage controller
                foreach (var sdi in device.StorageDeviceIDs)
                {
                    if (sdi.DriveNumber < 0)
                    {
                        continue;
                    }

                    //Skip duplicates
                    if (!currentByDriveNumber.ContainsKey(sdi.DriveNumber))
                    {
                        currentByDriveNumber.Add(sdi.DriveNumber, (device, sdi));
                    }
                }
            }

            //Check all PhysicalDrives for changes
            for (int i = 0; i < MaxDrives; ++i)
            {
                //Skip already detected drives
                if (currentByDriveNumber.ContainsKey(i))
                {
                    continue;
                }

                //Simple physical path
                var path = $@"\\.\PhysicalDrive{i}";

                var handle = SafeFileHandler.OpenHandle(path);

                //Handle invalid
                if (!SafeFileHandler.IsHandleValid(handle))
                {
                    continue;
                }

                //Handle valid -> close and continue
                SafeFileHandler.CloseHandle(handle);

                var drive = new StorageDevice
                {
                    DeviceID = string.Empty,
                    HardwareID = string.Empty,
                    PhysicalPath = path,
                    DriveNumber = i,
                };

                //Add to current drives
                currentByDriveNumber[i] = (new() { Name = string.Empty }, drive);
            }

            //Check for removed devices
            var removed = new List<Storage>();
            foreach (var existing in storagesSnapshot)
            {
                if (existing.DriveNumber < 0)
                {
                    continue;
                }

                //Not existing anymore
                if (!currentByDriveNumber.ContainsKey(existing.DriveNumber))
                {
                    removed.Add(existing);
                }
            }

            //Check for added devices
            var added = new List<(StorageController Controller, StorageDevice Device)>();
            foreach (var kvp in currentByDriveNumber)
            {
                //Not yet existing
                if (!existingByDriveNumber.ContainsKey(kvp.Key))
                {
                    added.Add(kvp.Value);
                }
            }

            //Handle added device[s]
            foreach (var add in added)
            {
                var item = add.Device;

                LogSimple.LogTrace($"Adding device with {nameof(item.PhysicalPath)} = '{item.PhysicalPath}'.");

                var storage = new Storage(add.Controller.Name, item);

                if (storage.IsValid)
                {
                    bool alreadyExisted;
                    using (var guard = new LockGuard(_StorageLock))
                    {
                        alreadyExisted = _Storages.Any(s => s.DriveNumber == storage.DriveNumber);
                        if (!alreadyExisted)
                        {
                            _Storages.Add(storage);
                        }
                    }

                    if (!alreadyExisted)
                    {
                        //Notify subscribers
                        StoragesChanged?.Invoke(new()
                        {
                            StorageChangeIdentifier = StorageChangeIdentifier.Added,
                            Storage = storage,
                        });
                    }
                }
                else
                {
                    LogSimple.LogTrace($"Cannot add device '{item.PhysicalPath}' - device is invalid.");
                }
            }

            //Handle removed device[s]
            foreach (var rem in removed)
            {
                LogSimple.LogTrace($"Removed device with {nameof(rem.PhysicalPath)} = '{rem.PhysicalPath}'.");

                bool wasRemoved;
                using (var guard = new LockGuard(_StorageLock))
                {
                    wasRemoved = _Storages.Remove(rem);
                }

                if (wasRemoved)
                {
                    //Notify subscribers
                    StoragesChanged?.Invoke(new()
                    {
                        StorageChangeIdentifier = StorageChangeIdentifier.Removed,
                        Storage = rem,
                    });
                }
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
                found = _Storages.Find(s => s.DriveNumber == disk.DriveNumber)
                     ?? _Storages.Find(s => s.StorageController == si.Name
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
