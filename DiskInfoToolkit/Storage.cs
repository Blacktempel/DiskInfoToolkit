/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Monitoring;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Partitions;
using Microsoft.Win32.SafeHandles;
using System.Globalization;
using System.Reflection;
using System.Resources;
using OS = BlackSharp.Core.Platform.OperatingSystem;

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
                if (value == null)
                {
                    return;
                }

                lock (SyncRoot)
                {
                    _devicesChanged += value;
                }

                EnsureMonitoringStarted();
            }
            remove
            {
                if (value == null)
                {
                    return;
                }

                lock (SyncRoot)
                {
                    _devicesChanged -= value;
                }

                StopMonitoringIfUnused();
            }
        }

        #endregion

        #region Fields

        private const int MonitoringThreadStopTimeoutMilliseconds = 5000;

        private static readonly object SyncRoot = new object();

        private static readonly AutoResetEvent RescanSignal = new AutoResetEvent(false);

        private static readonly ManualResetEvent MonitoringStopSignal = new ManualResetEvent(false);

        private static Thread _messageLoopThread;

        private static Thread _rescanThread;

        private static Thread _mediaWatchThread;

        private static bool _monitoringStarted;

        private static bool _monitoringStopping;

        private static bool _explicitMonitoringStarted;

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
        /// Gets or sets the maximum number of consecutive refresh failures allowed before fully reprobing the device.
        /// </summary>
        /// <remarks>The default value is 3.</remarks>
        public static int MaxConsecutiveRefreshFailureCount { get; set; } = 3;

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
        /// <remarks>After the first full probe, compatible devices reuse their cached successful probe path and refresh only volatile disk data where possible.<br/>
        /// Polling HDD SMART data too frequently may cause performance degradation or audible clicking on some drives or USB/SATA bridges.<br/>
        /// Applications should throttle HDD refresh intervals appropriately.</remarks>
        public static bool Refresh(StorageDevice device)
        {
            return Refresh(device, true, true, true);
        }

        /// <summary>
        /// Refreshes the current state of the specified storage device.
        /// </summary>
        /// <param name="device">The device to refresh.</param>
        /// <param name="refreshSmartData">Whether to refresh SMART data. When disabled, previously collected SMART data is retained.</param>
        /// <returns>Whether the device state was successfully refreshed or device had no changes.</returns>
        /// <remarks>After the first full probe, compatible devices reuse their cached successful probe path and refresh only volatile disk data where possible.<br/>
        /// Polling HDD SMART data too frequently may cause performance degradation or audible clicking on some drives or USB/SATA bridges.<br/>
        /// Applications should throttle HDD refresh intervals appropriately.</remarks>
        public static bool Refresh(StorageDevice device, bool refreshSmartData)
        {
            return Refresh(device, true, true, refreshSmartData);
        }

        /// <summary>
        /// Refreshes the volatile data of the specified storage device.
        /// </summary>
        /// <param name="device">The device to refresh.</param>
        /// <returns>Whether the volatile data was successfully refreshed or device had no changes.</returns>
        /// <remarks>After the first full probe, compatible devices reuse their cached successful probe path and refresh only volatile disk data where possible.<br/>
        /// Polling HDD SMART data too frequently may cause performance degradation or audible clicking on some drives or USB/SATA bridges.<br/>
        /// Applications should throttle HDD refresh intervals appropriately.</remarks>
        public static bool RefreshVolatileData(StorageDevice device)
        {
            return Refresh(device, true, true, true);
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

            bool changed = StoragePartitionReader.PopulatePartitions(device, StorageIoControlFactory.Create());
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
        /// <remarks>After the first full probe, compatible devices reuse their cached successful probe path and refresh only volatile disk data where possible.<br/>
        /// Polling HDD SMART data too frequently may cause performance degradation or audible clicking on some drives or USB/SATA bridges.<br/>
        /// Applications should throttle HDD refresh intervals appropriately.</remarks>
        public static bool Refresh(StorageDevice device, bool refreshProbeData, bool refreshPartitions)
        {
            return Refresh(device, refreshProbeData, refreshPartitions, true);
        }

        /// <summary>
        /// Refreshes the current state of the specified storage device.
        /// </summary>
        /// <param name="device">The device to refresh.</param>
        /// <param name="refreshProbeData">Whether to refresh the probe data.</param>
        /// <param name="refreshPartitions">Whether to refresh the partitions.</param>
        /// <param name="refreshSmartData">Whether to refresh SMART data as part of the probe data refresh. When disabled, previously collected SMART data is retained.</param>
        /// <returns>Whether the device state was successfully refreshed or device had no changes.</returns>
        /// <remarks>After the first full probe, compatible devices reuse their cached successful probe path and refresh only volatile disk data where possible.<br/>
        /// Polling HDD SMART data too frequently may cause performance degradation or audible clicking on some drives or USB/SATA bridges.<br/>
        /// Applications should throttle HDD refresh intervals appropriately.</remarks>
        public static bool Refresh(StorageDevice device, bool refreshProbeData, bool refreshPartitions, bool refreshSmartData)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            var ioControl = StorageIoControlFactory.Create();
            var refreshed = StorageDeviceCloneHelper.Clone(device);

            if (refreshProbeData)
            {
                RefreshSingleDeviceProbeData(refreshed, ioControl, refreshSmartData);
            }

            if (refreshPartitions)
            {
                StoragePartitionReader.PopulatePartitions(refreshed, ioControl);
            }

            refreshed.LastUpdatedUtc = DateTime.UtcNow;
            return StorageDeviceCloneHelper.CopyInto(refreshed, device);
        }

        /// <summary>
        /// Starts monitoring storage devices for changes.
        /// </summary>
        public static void StartMonitoring()
        {
            lock (SyncRoot)
            {
                _explicitMonitoringStarted = true;
            }

            EnsureMonitoringStarted();
        }

        /// <summary>
        /// Stops explicit storage device monitoring when no event subscribers are registered.
        /// </summary>
        public static void StopMonitoring()
        {
            lock (SyncRoot)
            {
                _explicitMonitoringStarted = false;
            }

            StopMonitoringIfUnused();
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

            var ioControl = StorageIoControlFactory.Create();

            SafeFileHandle handle = ioControl.OpenDevice(
                device.DevicePath,
                IoAccess.GenericRead,
                IoShare.ReadWrite,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            using (handle)
            {
                var buffer = new byte[512];

                if (OS.IsWindows())
                {
                    Kernel32Native.SetFilePointerEx(handle, 0, IntPtr.Zero, 0);
                    Kernel32Native.ReadFile(handle, buffer, (uint)buffer.Length, out _, IntPtr.Zero);
                }
                else
                {
                    using (var stream = new FileStream(handle, FileAccess.Read))
                    {
#pragma warning disable CA2022 //Avoid inexact read with Stream.Read
                        stream.Read(buffer, 0, buffer.Length);
#pragma warning restore CA2022 //Avoid inexact read with Stream.Read
                    }
                }
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
            StorageMediaPresenceMonitor.FilterNoMediaDevices(visibleDisks, mediaStates);
        }

        private static List<StorageDevice> EnumerateRawDisks()
        {
            var engine = new StorageDetectionEngine(StorageIoControlFactory.Create());

            //Get the raw list of disks
            var disks = engine.GetDisks();
            var ioControl = StorageIoControlFactory.Create();

            foreach (var disk in disks)
            {
                //Populate the partitions for all disks
                StoragePartitionReader.PopulatePartitions(disk, ioControl);
                disk.LastUpdatedUtc = DateTime.UtcNow;
            }

            return disks;
        }

        private static void RefreshSingleDeviceProbeData(StorageDevice device, IStorageIoControl ioControl, bool refreshSmartData)
        {
            if (device == null || ioControl == null)
            {
                return;
            }

            bool canUseCachedProbePlan = CanUseCachedProbePlanForRefresh(device);

            device.ProbeTrace = new List<string>();

            if (!canUseCachedProbePlan)
            {
                if (refreshSmartData)
                {
                    ResetVolatileProbeData(device);
                }

                //Refresh the IOCTL-backed base properties for this single device only
                //Do not enumerate all disks here
                StorageDetectionEngine.AttachStandardStorageProperties(device, ioControl, refreshSmartData);
                StorageDetectionEngine.SelectProbeStrategy(device);
            }

            if (!device.IsFiltered)
            {
                StorageProbeDispatcher.Probe(device, ioControl, refreshSmartData);
            }
        }

        private static bool CanUseCachedProbePlanForRefresh(StorageDevice device)
        {
            StorageProbePlan plan = device != null ? device.ProbePlan : null;
            return plan != null && plan.IsInitialized && plan.IsCompatibleWith(device);
        }

        private static void ResetVolatileProbeData(StorageDevice device)
        {
            device.SupportsSmart = false;
            device.SmartVersionRaw = 0;
            device.PredictsFailure = null;
            device.PredictFailureVendorData = Array.Empty<byte>();
            device.SmartAttributes = new List<SmartAttributeEntry>();
            device.SmartAttributeProfile = SmartAttributeProfile.Unknown;
            device.ProbeTrace = new List<string>();
            device.Nvme = new StorageNvmeInfo();
            device.CapacitySource = string.Empty;
        }

        private static void EnsureMonitoringStarted()
        {
            while (true)
            {
                Thread messageLoopThread;
                Thread rescanThread;
                Thread mediaWatchThread;

                lock (SyncRoot)
                {
                    if (!ShouldKeepMonitoringAliveLocked())
                    {
                        return;
                    }

                    if (_monitoringStarted && !_monitoringStopping)
                    {
                        return;
                    }

                    if (_monitoringStopping)
                    {
                        CaptureMonitoringThreadsLocked(out messageLoopThread, out rescanThread, out mediaWatchThread);
                    }
                    else
                    {
                        StartMonitoringLocked();
                        return;
                    }
                }

                WaitForMonitoringThreads(messageLoopThread, rescanThread, mediaWatchThread);

                lock (SyncRoot)
                {
                    CompleteMonitoringStopLocked();
                }
            }
        }

        private static void StartMonitoringLocked()
        {
            _monitoringStopping = false;
            MonitoringStopSignal.Reset();

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

            //Start the media watch loop thread, which periodically checks for removable media state changes and signals rescans.
            //This is required on Linux as well, because some SD/MMC/card-reader stacks keep the same block-device topology while the medium changes.
            _mediaWatchThread = new Thread(MediaWatchLoop)
            {
                IsBackground = true,
                Name = $"{nameof(Storage)}.{nameof(MediaWatchLoop)}"
            };
            _mediaWatchThread.Start();

            _monitoringStarted = true;
        }

        private static void StopMonitoringIfUnused()
        {
            Thread messageLoopThread;
            Thread rescanThread;
            Thread mediaWatchThread;

            lock (SyncRoot)
            {
                if (ShouldKeepMonitoringAliveLocked())
                {
                    return;
                }

                RequestMonitoringStopLocked(out messageLoopThread, out rescanThread, out mediaWatchThread);
            }

            WaitForMonitoringThreads(messageLoopThread, rescanThread, mediaWatchThread);

            lock (SyncRoot)
            {
                CompleteMonitoringStopLocked();
            }
        }

        private static bool ShouldKeepMonitoringAliveLocked()
        {
            return _devicesChanged != null || _explicitMonitoringStarted;
        }

        private static void RequestMonitoringStopLocked(out Thread messageLoopThread, out Thread rescanThread, out Thread mediaWatchThread)
        {
            CaptureMonitoringThreadsLocked(out messageLoopThread, out rescanThread, out mediaWatchThread);

            if (!_monitoringStarted || _monitoringStopping)
            {
                return;
            }

            _monitoringStopping = true;

            MonitoringStopSignal.Set();
            RescanSignal.Set();

            if (OS.IsWindows())
            {
                WindowsStorageDeviceChangeMonitor.RequestStop();
            }
        }

        private static void CaptureMonitoringThreadsLocked(out Thread messageLoopThread, out Thread rescanThread, out Thread mediaWatchThread)
        {
            messageLoopThread = _messageLoopThread;
            rescanThread      = _rescanThread;
            mediaWatchThread  = _mediaWatchThread;
        }

        private static void CompleteMonitoringStopLocked()
        {
            if (!_monitoringStopping)
            {
                return;
            }

            _monitoringStarted  = false;
            _monitoringStopping = false;

            _messageLoopThread = null;
            _rescanThread      = null;
            _mediaWatchThread  = null;
        }

        private static void WaitForMonitoringThreads(Thread messageLoopThread, Thread rescanThread, Thread mediaWatchThread)
        {
            WaitForMonitoringThread(messageLoopThread);
            WaitForMonitoringThread(rescanThread);
            WaitForMonitoringThread(mediaWatchThread);
        }

        private static void WaitForMonitoringThread(Thread thread)
        {
            if (thread == null || !thread.IsAlive || ReferenceEquals(Thread.CurrentThread, thread))
            {
                return;
            }

            thread.Join(MonitoringThreadStopTimeoutMilliseconds);
        }

        private static void MessageLoop()
        {
            if (OS.IsWindows())
            {
                WindowsStorageDeviceChangeMonitor.Run(MonitoringStopSignal, QueueRescan);
                return;
            }

            if (OS.IsLinux())
            {
                LinuxStorageDeviceChangeMonitor.Run(MonitoringStopSignal, QueueRescan);
            }
        }

        private static void QueueRescan()
        {
            RescanSignal.Set();
        }

        private static void MediaWatchLoop()
        {
            while (!MonitoringStopSignal.WaitOne(0))
            {
                var delay = MediaWatchLoopDelay;

                if (MonitoringStopSignal.WaitOne(delay))
                {
                    return;
                }

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

        private static List<StorageDevice> MergeMediaWatchDevicesForMonitoring(List<StorageDevice> previous, List<StorageDevice> current)
        {
            if (!OS.IsLinux())
            {
                return StorageDeviceCloneHelper.CloneList(current);
            }

            var result = new List<StorageDevice>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddMediaWatchDevices(result, keys, current);
            AddMediaWatchDevices(result, keys, previous);

            return result;
        }

        private static void AddMediaWatchDevices(List<StorageDevice> result, HashSet<string> keys, List<StorageDevice> devices)
        {
            if (result == null || keys == null || devices == null)
            {
                return;
            }

            foreach (var device in devices)
            {
                if (!StorageMediaPresenceMonitor.IsMediaWatchCandidate(device))
                {
                    continue;
                }

                string key = GetMediaWatchKey(device);
                if (string.IsNullOrWhiteSpace(key) || keys.Contains(key))
                {
                    continue;
                }

                keys.Add(key);
                result.Add(StorageDeviceCloneHelper.Clone(device));
            }
        }

        private static string GetMediaWatchKey(StorageDevice device)
        {
            if (device == null)
            {
                return string.Empty;
            }

            string key = StorageDeviceIdentityMatcher.GetStableKey(device);
            if (!string.IsNullOrWhiteSpace(key))
            {
                return key;
            }

            if (!string.IsNullOrWhiteSpace(device.DevicePath))
            {
                return device.DevicePath;
            }

            if (!string.IsNullOrWhiteSpace(device.AlternateDevicePath))
            {
                return device.AlternateDevicePath;
            }

            return device.DeviceInstanceID ?? string.Empty;
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
            WaitHandle[] waitHandles =
            {
                RescanSignal,
                MonitoringStopSignal
            };

            while (true)
            {
                if (WaitHandle.WaitAny(waitHandles) == 1)
                {
                    return;
                }

                if (MonitoringStopSignal.WaitOne(250))
                {
                    return;
                }

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

                if (MonitoringStopSignal.WaitOne(0))
                {
                    return;
                }
            }
        }

        private static void HandleStorageTopologyChanged()
        {
            List<StorageDevice> previous;
            List<StorageDevice> previousMediaWatchDevices;
            lock (SyncRoot)
            {
                //Clone the previous state to avoid holding the lock during the potentially long enumeration and diffing operations
                previous = StorageDeviceCloneHelper.CloneList(_currentDisks);
                previousMediaWatchDevices = StorageDeviceCloneHelper.CloneList(_mediaWatchDevices);
            }

            //Get the new state
            EnumerateStorageState(out var current, out var mediaWatchDevices, out var mediaStates);

            var mergedMediaWatchDevices = MergeMediaWatchDevicesForMonitoring(previousMediaWatchDevices, mediaWatchDevices);
            var mergedMediaStates = OS.IsLinux()
                ? StorageMediaPresenceMonitor.BuildStateSnapshot(mergedMediaWatchDevices)
                : mediaStates;

            //Build the difference between the previous and current state
            var diff = StorageDeviceDiffBuilder.Build(previous, current);

            lock (SyncRoot)
            {
                _currentDisks = StorageDeviceCloneHelper.CloneList(current);
                _mediaWatchDevices = StorageDeviceCloneHelper.CloneList(mergedMediaWatchDevices);
                _removableMediaStates = mergedMediaStates;
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
