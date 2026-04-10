/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Monitoring;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Partitions;
using DiskInfoToolkit.Vendors;
using Microsoft.Win32.SafeHandles;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit
{
    /// <summary>
    /// This class provides static methods for storage device enumeration, monitoring and management.
    /// </summary>
    public static class Storage
    {
        #region Events

        /// <summary>
        /// Occurs when the set of available storage devices changes.
        /// </summary>
        /// <remarks>Subscribe to this event to be notified when storage devices are added or removed.<br/>
        /// The event provides information about the change through the <see cref="StorageDevicesChangedEventArgs"/> parameter.</remarks>
        public static event EventHandler<StorageDevicesChangedEventArgs> DevicesChanged
        {
            add
            {
                EnsureMonitoringStarted();
                _devicesChanged += value;
            }
            remove
            {
                _devicesChanged -= value;
            }
        }

        #endregion

        #region Fields

        private const string MessageWindowClassName = nameof(Storage) + "TopLevelHiddenWindowClass";

        private const string MessageWindowTitle = nameof(Storage) + "MessageWindow";

        private static readonly object SyncRoot = new object();

        private static readonly AutoResetEvent RescanSignal = new AutoResetEvent(false);

        private static IntPtr _messageWindow;

        private static IntPtr _volumeNotificationHandle;

        private static IntPtr _diskNotificationHandle;

        private static StorageWindowProc _windowProcDelegate;

        private static Thread _messageLoopThread;

        private static Thread _rescanThread;

        private static Thread _mediaWatchThread;

        private static bool _monitoringStarted;

        private static TimeSpan _mediaWatchLoopDelay = TimeSpan.FromSeconds(1);

        private static CultureInfo _resourceCulture = CultureInfo.InvariantCulture;

        private static ResourceManager _resourceManager;

        private static string _resourceBaseName;

        private static List<StorageDevice> _currentDisks = new List<StorageDevice>();

        private static List<StorageDevice> _mediaWatchDevices = new List<StorageDevice>();

        private static Dictionary<string, bool?> _removableMediaStates = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

        private static EventHandler<StorageDevicesChangedEventArgs> _devicesChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the delay between removable-media polling cycles.<br/>
        /// Default is 1 second.
        /// </summary>
        /// <remarks>Setting a very low value may increase CPU usage, while setting a very high value may cause slower reaction to media changes.</remarks>
        public static TimeSpan MediaWatchLoopDelay
        {
            get
            {
                lock (SyncRoot)
                {
                    return _mediaWatchLoopDelay;
                }
            }
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                lock (SyncRoot)
                {
                    _mediaWatchLoopDelay = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the culture used to localize assembly resources.
        /// </summary>
        public static CultureInfo ResourceCulture
        {
            get
            {
                lock (SyncRoot)
                {
                    return _resourceCulture;
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    _resourceCulture = value ?? CultureInfo.InvariantCulture;
                }
            }
        }

        /// <summary>
        /// Gets a snapshot of the currently cached storage devices.
        /// </summary>
        public static List<StorageDevice> CurrentDisks
        {
            get
            {
                lock (SyncRoot)
                {
                    return StorageDeviceCloneHelper.CloneList(_currentDisks);
                }
            }
        }

        #endregion

        #region Public

        /// <summary>
        /// Gets the list of currently visible storage devices.
        /// </summary>
        /// <returns>A list of <see cref="StorageDevice"/> objects representing the currently visible storage devices.</returns>
        public static List<StorageDevice> GetDisks()
        {
            EnumerateStorageState(out var visibleDisks, out var mediaWatchDevices, out var mediaStates);
            return visibleDisks;
        }

        /// <summary>
        /// Refreshes the current state of the specified storage device.
        /// </summary>
        /// <param name="device">The device to refresh.</param>
        /// <returns>Whether the device state was successfully refreshed or device had no changes.</returns>
        public static bool Refresh(StorageDevice device)
        {
            return Refresh(device, true, true);
        }

        /// <summary>
        /// Refreshes the volatile data of the specified storage device.
        /// </summary>
        /// <param name="device">The device to refresh.</param>
        /// <returns>Whether the volatile data was successfully refreshed or device had no changes.</returns>
        public static bool RefreshVolatileData(StorageDevice device)
        {
            return Refresh(device, true, true);
        }

        /// <summary>
        /// Refreshes the partitions of the specified storage device.
        /// </summary>
        /// <param name="device">The device to refresh.</param>
        /// <returns>Whether the partitions were successfully refreshed or device had no changes.</returns>
        public static bool RefreshPartitions(StorageDevice device)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            bool changed = StoragePartitionReader.PopulatePartitions(device, new WindowsStorageIoControl());
            device.LastUpdatedUtc = DateTime.UtcNow;
            return changed;
        }

        /// <summary>
        /// Refreshes the current state of the specified storage device.
        /// </summary>
        /// <param name="device">The device to refresh.</param>
        /// <param name="refreshProbeData">Whether to refresh the probe data.</param>
        /// <param name="refreshPartitions">Whether to refresh the partitions.</param>
        /// <returns>Whether the device state was successfully refreshed or device had no changes.</returns>
        public static bool Refresh(StorageDevice device, bool refreshProbeData, bool refreshPartitions)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            bool changed = false;

            if (refreshProbeData)
            {
                //Probe the device again and update the properties
                var refreshedDevices = GetDisks();

                //Find the best match for the device in the refreshed list
                var refreshed = StorageDeviceIdentityMatcher.FindBestMatch(refreshedDevices, device);

                //If the device is no longer present, return false
                if (refreshed == null)
                {
                    return false;
                }

                changed = StorageDeviceCloneHelper.CopyInto(refreshed, device);
            }
            else if (refreshPartitions)
            {
                changed = StoragePartitionReader.PopulatePartitions(device, new WindowsStorageIoControl());
            }

            if (refreshProbeData && refreshPartitions)
            {
                return changed;
            }

            if (refreshPartitions)
            {
                changed |= StoragePartitionReader.PopulatePartitions(device, new WindowsStorageIoControl());
            }

            device.LastUpdatedUtc = DateTime.UtcNow;
            return changed;
        }

        /// <summary>
        /// Starts monitoring storage devices for changes.
        /// </summary>
        public static void StartMonitoring()
        {
            EnsureMonitoringStarted();
        }

        /// <summary>
        /// Refreshes the cached disks.
        /// </summary>
        public static void RefreshCachedDisks()
        {
            EnsureMonitoringStarted();
            HandleStorageTopologyChanged();
        }

        /// <summary>
        /// Attempts to wake up the specified device if it is currently powered off.
        /// </summary>
        /// <param name="device">The storage device to wake up. Must not be null.</param>
        public static void TryWakeUp(StorageDevice device)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            var ioControl = new WindowsStorageIoControl();

            SafeFileHandle handle = ioControl.OpenDevice(
                device.DevicePath,
                IoAccess.GenericRead,
                IoShare.ReadWrite,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            using (handle)
            {
                var buffer = new byte[512];

                Kernel32Native.SetFilePointerEx(handle, 0, IntPtr.Zero, 0);
                Kernel32Native.ReadFile(handle, buffer, (uint)buffer.Length, out _, IntPtr.Zero);
            }
        }

        #endregion

        #region Internal

        internal static ILocalizedTextProvider GetTextProvider()
        {
            lock (SyncRoot)
            {
                if (_resourceManager == null)
                {
                    _resourceBaseName = ResolveResourceBaseName(typeof(Storage).Assembly);
                    if (!string.IsNullOrWhiteSpace(_resourceBaseName))
                    {
                        _resourceManager = new ResourceManager(_resourceBaseName, typeof(Storage).Assembly);
                    }
                }

                return _resourceManager != null
                    ? new ResourceManagerLocalizedTextProvider(_resourceManager, _resourceCulture)
                    : null;
            }
        }

        internal static string ResolveResourceBaseName(Assembly assembly)
        {
            if (assembly == null)
            {
                return string.Empty;
            }

            var resourceNames = assembly.GetManifestResourceNames();
            if (resourceNames == null || resourceNames.Length == 0)
            {
                return string.Empty;
            }

            const string preferredSuffix = ".Resources.Resources.resources";
            const string fallbackSuffix = ".Resources.resources";
            const string resourceExtension = ".resources";

            foreach (var resourceName in resourceNames)
            {
                if (resourceName != null && resourceName.EndsWith(preferredSuffix, StringComparison.Ordinal))
                {
                    return resourceName.Substring(0, resourceName.Length - resourceExtension.Length);
                }
            }

            foreach (var resourceName in resourceNames)
            {
                if (resourceName != null && resourceName.EndsWith(fallbackSuffix, StringComparison.Ordinal))
                {
                    return resourceName.Substring(0, resourceName.Length - resourceExtension.Length);
                }
            }

            return string.Empty;
        }

        #endregion

        #region Private

        private static void EnumerateStorageState(out List<StorageDevice> visibleDisks, out List<StorageDevice> mediaWatchDevices, out Dictionary<string, bool?> mediaStates)
        {
            //Get the raw list of disks
            var rawDisks = EnumerateRawDisks();

            //Extract the media-watch candidates and build the media presence state snapshot before filtering
            mediaWatchDevices = StorageMediaPresenceMonitor.ExtractMediaWatchDevices(rawDisks);

            //Build the media presence state snapshot before filtering,
            //so that devices that are filtered due to no media presence are still monitored for media changes
            mediaStates = StorageMediaPresenceMonitor.BuildStateSnapshot(mediaWatchDevices);

            //Filter the raw list to the visible list
            visibleDisks = StorageDeviceCloneHelper.CloneList(rawDisks);

            //Filter out devices that should not be visible
            StorageMediaPresenceMonitor.FilterNoMediaDevices(visibleDisks);
        }

        private static List<StorageDevice> EnumerateRawDisks()
        {
            var vendorLibraries = new ExternalVendorLibraryManager();

            var engine = new StorageDetectionEngine(
                new WindowsStorageIoControl(),
                vendorLibraries,
                new OptionalVendorBackendSet(vendorLibraries));

            //Get the raw list of disks
            var disks = engine.GetDisks();
            var ioControl = new WindowsStorageIoControl();

            foreach (var disk in disks)
            {
                //Populate the partitions for all disks
                StoragePartitionReader.PopulatePartitions(disk, ioControl);
                disk.LastUpdatedUtc = DateTime.UtcNow;
            }

            return disks;
        }

        private static void EnsureMonitoringStarted()
        {
            lock (SyncRoot)
            {
                if (_monitoringStarted)
                {
                    return;
                }

                _windowProcDelegate = WindowProc;

                //Get the initial storage state before starting the monitoring threads, so that we have a baseline for change detection and can populate the media watch state
                EnumerateStorageState(out var initialVisibleDisks, out var initialMediaWatchDevices, out var initialMediaStates);

                _currentDisks = StorageDeviceCloneHelper.CloneList(initialVisibleDisks);
                _mediaWatchDevices = StorageDeviceCloneHelper.CloneList(initialMediaWatchDevices);
                _removableMediaStates = initialMediaStates;

                //Start rescan thread, which rescans the storage state when signaled by the message loop thread or media watch loop
                _rescanThread = new Thread(RescanLoop)
                {
                    IsBackground = true,
                    Name = $"{nameof(Storage)}.{nameof(RescanLoop)}"
                };
                _rescanThread.Start();

                //Start the message loop thread, which listens for device change notifications and signals rescans
                _messageLoopThread = new Thread(MessageLoop)
                {
                    IsBackground = true,
                    Name = $"{nameof(Storage)}.{nameof(MessageLoop)}"
                };
                _messageLoopThread.Start();

                //Start the media watch loop thread, which periodically checks for removable media state changes and signals rescans
                _mediaWatchThread = new Thread(MediaWatchLoop)
                {
                    IsBackground = true,
                    Name = $"{nameof(Storage)}.{nameof(MediaWatchLoop)}"
                };
                _mediaWatchThread.Start();

                _monitoringStarted = true;
            }
        }

        private static void MessageLoop()
        {
            if (!CreateMessageWindow())
            {
                return;
            }

            try
            {
                while (User32Native.GetMessage(out var msg, _messageWindow, 0, 0) > 0)
                {
                    User32Native.TranslateMessage(ref msg);
                    User32Native.DispatchMessage(ref msg);
                }
            }
            finally
            {
                UnregisterStorageNotifications();
                if (_messageWindow != IntPtr.Zero)
                {
                    User32Native.DestroyWindow(_messageWindow);
                    _messageWindow = IntPtr.Zero;
                }
            }
        }

        private static bool CreateMessageWindow()
        {
            var wnd = new WNDCLASSEX();
            wnd.cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>();
            wnd.lpfnWndProc = _windowProcDelegate;
            wnd.lpszClassName = MessageWindowClassName;
            wnd.hInstance = Kernel32Native.GetModuleHandle(null);

            ushort atom = User32Native.RegisterClassEx(ref wnd);
            if (atom == 0)
            {
                return false;
            }

            _messageWindow = User32Native.CreateWindowEx(
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

            if (_messageWindow == IntPtr.Zero)
            {
                return false;
            }

            RegisterStorageNotifications();
            return true;
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
                    QueueRescan();
                }
            }

            return User32Native.DefWindowProc(hWnd, msg, wParam, lParam);
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
            if (_messageWindow == IntPtr.Zero)
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
                return User32Native.RegisterDeviceNotification(_messageWindow, filterPtr, User32Native.DEVICE_NOTIFY_WINDOW_HANDLE);
            }
            finally
            {
                Marshal.FreeHGlobal(filterPtr);
            }
        }

        private static void QueueRescan()
        {
            RescanSignal.Set();
        }

        private static void MediaWatchLoop()
        {
            while (true)
            {
                var delay = MediaWatchLoopDelay;
                Thread.Sleep(delay);

                try
                {
                    if (CheckForRemovableMediaStateChanges())
                    {
                        QueueRescan();
                    }
                }
                catch
                {
                }
            }
        }

        private static bool CheckForRemovableMediaStateChanges()
        {
            List<StorageDevice> snapshot;
            Dictionary<string, bool?> previousStates;

            lock (SyncRoot)
            {
                snapshot = StorageDeviceCloneHelper.CloneList(_mediaWatchDevices);
                previousStates = new Dictionary<string, bool?>(_removableMediaStates, StringComparer.OrdinalIgnoreCase);
            }

            var currentStates = StorageMediaPresenceMonitor.BuildStateSnapshot(snapshot);
            bool changed = !MediaStateDictionariesEqual(previousStates, currentStates);

            if (changed)
            {
                lock (SyncRoot)
                {
                    _removableMediaStates = currentStates;
                }
            }

            return changed;
        }

        private static bool MediaStateDictionariesEqual(Dictionary<string, bool?> left, Dictionary<string, bool?> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var otherValue))
                {
                    return false;
                }

                if (pair.Value != otherValue)
                {
                    return false;
                }
            }

            return true;
        }

        private static void RescanLoop()
        {
            while (true)
            {
                RescanSignal.WaitOne();
                Thread.Sleep(250);

                while (RescanSignal.WaitOne(0))
                {
                }

                try
                {
                    HandleStorageTopologyChanged();
                }
                catch
                {
                }
            }
        }

        private static void HandleStorageTopologyChanged()
        {
            List<StorageDevice> previous;
            lock (SyncRoot)
            {
                //Clone the previous state to avoid holding the lock during the potentially long enumeration and diffing operations
                previous = StorageDeviceCloneHelper.CloneList(_currentDisks);
            }

            //Get the new state
            EnumerateStorageState(out var current, out var mediaWatchDevices, out var mediaStates);

            //Build the difference between the previous and current state
            var diff = StorageDeviceDiffBuilder.Build(previous, current);

            lock (SyncRoot)
            {
                _currentDisks = StorageDeviceCloneHelper.CloneList(current);
                _mediaWatchDevices = StorageDeviceCloneHelper.CloneList(mediaWatchDevices);

                _removableMediaStates = mediaStates;
            }

            //Raise change event if there are any changes
            if (diff.HasChanges)
            {
                _devicesChanged?.Invoke(null, diff);
            }
        }

        #endregion
    }
}
