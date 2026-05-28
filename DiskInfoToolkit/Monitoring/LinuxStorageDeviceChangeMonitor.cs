/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using System.Text;

namespace DiskInfoToolkit.Monitoring
{
    internal static class LinuxStorageDeviceChangeMonitor
    {
        #region Fields

        private const string DevPath = "/dev";

        private const string DevDiskPath = "/dev/disk";

        private const string SysBlockPath = "/sys/block";

        private const string SysClassBlockPath = "/sys/class/block";

        private const int ChangePollIntervalMilliseconds = 1000;

        private const int ChangeSettleDelayMilliseconds = 500;

        private const int MaxChangeSettleAttempts = 6;

        private const int ForcedRescanMinimumIntervalMilliseconds = 2000;

        private const int WatcherSignalMinimumIntervalMilliseconds = 500;

        private const string ProcPartitionsPath = "/proc/partitions";

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

            var previousSnapshot = ReadLinuxStorageChangeSnapshot();

            var lastForcedRescanUtc = DateTime.MinValue;

            var watchers = new List<FileSystemWatcher>();

            using (var possibleChangeSignal = new AutoResetEvent(false))
            {
                var possibleChangeGate = new PossibleChangeSignalGate(possibleChangeSignal);
                Action signalPossibleChange = possibleChangeGate.Signal;

                try
                {
                    //Add watchers for relevant directories and files that may indicate storage device changes.
                    //The watchers will signal possible changes, which will then be verified by comparing snapshots of the system state.
                    AddWatcher(watchers, CreateWatcher(DevPath          , IsRelevantDevEntry            , signalPossibleChange));
                    AddWatcher(watchers, CreateWatcher(SysBlockPath     , IsRelevantSysBlockEntry       , signalPossibleChange));
                    AddWatcher(watchers, CreateWatcher(SysClassBlockPath, IsRelevantBlockOrPartitionName, signalPossibleChange));

                    AddDevDiskWatchers(watchers, signalPossibleChange);

                    var waitHandles = new[]
                    {
                        stopSignal,
                        possibleChangeSignal
                    };

                    while (true)
                    {
                        int waitResult = WaitHandle.WaitAny(waitHandles, ChangePollIntervalMilliseconds);
                        if (waitResult == 0)
                        {
                            return;
                        }

                        if (waitResult == 1)
                        {
                            //A possible change was signaled by one of the watchers.
                            //We will now wait for a short period to allow changes to settle, and then compare snapshots to determine if a rescan is needed.
                            DrainSignal(possibleChangeSignal);

                            //Before doing potentially expensive snapshot reads, check if the snapshot can be updated after settle.
                            if (TryUpdateSnapshotAfterSettle(stopSignal, possibleChangeSignal, ref previousSnapshot))
                            {
                                //The snapshot changed after settling, so we should queue a rescan.
                                QueueRescanSafely(queueRescan);
                            }
                            //If we couldn't verify a settled snapshot change, we can still force a rescan if enough time has passed since the last forced rescan.
                            else if (CanForceRescanForWatcherEvent(ref lastForcedRescanUtc))
                            {
                                QueueRescanSafely(queueRescan);
                            }

                            continue;
                        }

                        var currentSnapshot = ReadLinuxStorageChangeSnapshot();

                        if (!SetEquals(previousSnapshot, currentSnapshot)
                         && TryUpdateSnapshotAfterSettle(stopSignal, possibleChangeSignal, ref previousSnapshot))
                        {
                            QueueRescanSafely(queueRescan);
                        }
                    }
                }
                finally
                {
                    DisposeWatchers(watchers);
                }
            }
        }

        #endregion

        #region Private

        private static void AddDevDiskWatchers(List<FileSystemWatcher> watchers, Action signalPossibleChange)
        {
            if (watchers == null || !Directory.Exists(DevDiskPath))
            {
                return;
            }

            AddWatcher(watchers, CreateWatcher(DevDiskPath, IsRelevantDevDiskDirectory, signalPossibleChange));

            AddWatcher(watchers, CreateWatcher(Path.Combine(DevDiskPath, "by-id"       ), IsRelevantDevDiskEntry, signalPossibleChange));
            AddWatcher(watchers, CreateWatcher(Path.Combine(DevDiskPath, "by-label"    ), IsRelevantDevDiskEntry, signalPossibleChange));
            AddWatcher(watchers, CreateWatcher(Path.Combine(DevDiskPath, "by-partlabel"), IsRelevantDevDiskEntry, signalPossibleChange));
            AddWatcher(watchers, CreateWatcher(Path.Combine(DevDiskPath, "by-partuuid" ), IsRelevantDevDiskEntry, signalPossibleChange));
            AddWatcher(watchers, CreateWatcher(Path.Combine(DevDiskPath, "by-path"     ), IsRelevantDevDiskEntry, signalPossibleChange));
            AddWatcher(watchers, CreateWatcher(Path.Combine(DevDiskPath, "by-uuid"     ), IsRelevantDevDiskEntry, signalPossibleChange));
        }

        private static void AddWatcher(List<FileSystemWatcher> watchers, FileSystemWatcher watcher)
        {
            if (watchers == null || watcher == null)
            {
                return;
            }

            watchers.Add(watcher);
        }

        private static FileSystemWatcher CreateWatcher(string path, Predicate<string> isRelevantName, Action signalPossibleChange)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return null;
            }

            FileSystemWatcher watcher = null;
            try
            {
                watcher = new FileSystemWatcher(path);

                watcher.IncludeSubdirectories = false;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName;

                var watcherSignalLimiter = new SignalRateLimiter();

                FileSystemEventHandler fileChanged = (sender, args) =>
                {
                    if (IsRelevantChange(args.Name, isRelevantName) && watcherSignalLimiter.TryAccept())
                    {
                        SignalPossibleChange(signalPossibleChange);
                    }
                };

                RenamedEventHandler renamed = (sender, args) =>
                {
                    if ((IsRelevantChange(args.Name, isRelevantName) || IsRelevantChange(args.OldName, isRelevantName))
                     && watcherSignalLimiter.TryAccept())
                    {
                        SignalPossibleChange(signalPossibleChange);
                    }
                };

                ErrorEventHandler error = (sender, args) =>
                {
                    if (watcherSignalLimiter.TryAccept())
                    {
                        SignalPossibleChange(signalPossibleChange);
                    }
                };

                watcher.Created += fileChanged;
                watcher.Deleted += fileChanged;
                watcher.Renamed += renamed;
                watcher.Error   += error;

                watcher.EnableRaisingEvents = true;

                return watcher;
            }
            catch
            {
                DisposeWatcher(watcher);

                return null;
            }
        }

        private static void DisposeWatchers(List<FileSystemWatcher> watchers)
        {
            if (watchers == null)
            {
                return;
            }

            foreach (var watcher in watchers)
            {
                DisposeWatcher(watcher);
            }
        }

        private static void DisposeWatcher(FileSystemWatcher watcher)
        {
            if (watcher == null)
            {
                return;
            }

            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
            }
        }

        private static bool IsRelevantChange(string name, Predicate<string> isRelevantName)
        {
            if (string.IsNullOrWhiteSpace(name) || isRelevantName == null)
            {
                return false;
            }

            string fileName = Path.GetFileName(name);

            return !string.IsNullOrWhiteSpace(fileName) && isRelevantName(fileName);
        }

        private static bool IsRelevantDevEntry(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return StartsWithStoragePrefix(name, "sd"    , true )
                || StartsWithStoragePrefix(name, "hd"    , true )
                || StartsWithStoragePrefix(name, "vd"    , true )
                || StartsWithStoragePrefix(name, "xvd"   , true )
                || StartsWithStoragePrefix(name, "sr"    , false)
                || StartsWithStoragePrefix(name, "nvme"  , false)
                || StartsWithStoragePrefix(name, "mmcblk", false)
                || StartsWithStoragePrefix(name, "pmem"  , false)
                || StartsWithStoragePrefix(name, "dasd"  , true );
        }

        private static bool IsRelevantDevDiskDirectory(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.StartsWith("by-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRelevantDevDiskEntry(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && !name.StartsWith(".", StringComparison.Ordinal);
        }

        private static bool IsRelevantSysBlockEntry(string name)
        {
            return !ShouldSkipSysBlockDevice(name);
        }

        private static bool IsRelevantBlockOrPartitionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || ShouldSkipSysBlockDevice(TrimLinuxPartitionSuffix(name)))
            {
                return false;
            }

            return IsRelevantDevEntry(name);
        }

        private static string TrimLinuxPartitionSuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            if (name.StartsWith("nvme"  , StringComparison.OrdinalIgnoreCase)
             || name.StartsWith("mmcblk", StringComparison.OrdinalIgnoreCase))
            {
                int index = name.LastIndexOf('p');
                return index > 0 ? name.Substring(0, index) : name;
            }

            int end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1]))
            {
                --end;
            }

            return end > 0 ? name.Substring(0, end) : name;
        }

        private static bool StartsWithStoragePrefix(string name, string prefix, bool requireLetterAfterPrefix)
        {
            if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            char next = name[prefix.Length];

            return requireLetterAfterPrefix
                 ? IsAsciiLetter(next)
                 : char.IsDigit(next);
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z');
        }

        private static bool TryUpdateSnapshotAfterSettle(WaitHandle stopSignal, AutoResetEvent possibleChangeSignal, ref HashSet<string> previousSnapshot)
        {
            if (!TryReadSettledSnapshot(stopSignal, possibleChangeSignal, out var currentSnapshot))
            {
                return false;
            }

            bool changed = !SetEquals(previousSnapshot, currentSnapshot);
            if (changed)
            {
                previousSnapshot = currentSnapshot;
            }

            return changed;
        }

        private static bool TryReadSettledSnapshot(WaitHandle stopSignal, AutoResetEvent possibleChangeSignal, out HashSet<string> snapshot)
        {
            snapshot = null;
            HashSet<string> previousRead = null;

            for (int i = 0; i < MaxChangeSettleAttempts; ++i)
            {
                if (stopSignal.WaitOne(ChangeSettleDelayMilliseconds))
                {
                    return false;
                }

                DrainSignal(possibleChangeSignal);

                var currentRead = ReadLinuxStorageChangeSnapshot();

                if (previousRead != null && SetEquals(previousRead, currentRead))
                {
                    snapshot = currentRead;
                    return true;
                }

                previousRead = currentRead;
            }

            snapshot = previousRead ?? ReadLinuxStorageChangeSnapshot();
            return true;
        }

        private static bool CanForceRescanForWatcherEvent(ref DateTime lastForcedRescanUtc)
        {
            var now = DateTime.UtcNow;

            if (lastForcedRescanUtc != DateTime.MinValue
             && (now - lastForcedRescanUtc).TotalMilliseconds < ForcedRescanMinimumIntervalMilliseconds)
            {
                return false;
            }

            lastForcedRescanUtc = now;
            return true;
        }

        private static HashSet<string> ReadLinuxStorageChangeSnapshot()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddSysBlockSnapshot      (result);
            AddSysClassBlockSnapshot (result);
            AddProcPartitionsSnapshot(result);
            AddDevDiskSnapshot       (result);

            return result;
        }

        private static void AddSysBlockSnapshot(HashSet<string> result)
        {
            if (result == null || !Directory.Exists(SysBlockPath))
            {
                return;
            }

            try
            {
                foreach (var blockDirectory in Directory.EnumerateDirectories(SysBlockPath))
                {
                    string name = Path.GetFileName(blockDirectory);
                    if (!ShouldSkipSysBlockDevice(name))
                    {
                        result.Add("sysblock:" + BuildSysBlockDeviceSignature(blockDirectory, name));
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddSysClassBlockSnapshot(HashSet<string> result)
        {
            if (result == null || !Directory.Exists(SysClassBlockPath))
            {
                return;
            }

            try
            {
                foreach (var blockEntry in Directory.EnumerateFileSystemEntries(SysClassBlockPath))
                {
                    string name = Path.GetFileName(blockEntry);
                    if (!IsRelevantBlockOrPartitionName(name))
                    {
                        continue;
                    }

                    result.Add("classblock:" + name + ":" + ReadFileString(Path.Combine(blockEntry, "dev")) + ":" + ReadFileString(Path.Combine(blockEntry, "size")));
                }
            }
            catch
            {
            }
        }

        private static void AddProcPartitionsSnapshot(HashSet<string> result)
        {
            if (result == null || !File.Exists(ProcPartitionsPath))
            {
                return;
            }

            try
            {
                foreach (var line in File.ReadAllLines(ProcPartitionsPath))
                {
                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4)
                    {
                        continue;
                    }

                    string name = parts[3];
                    if (IsRelevantBlockOrPartitionName(name))
                    {
                        result.Add("procpart:" + name + ":" + parts[0] + ":" + parts[1] + ":" + parts[2]);
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddDevDiskSnapshot(HashSet<string> result)
        {
            if (result == null || !Directory.Exists(DevDiskPath))
            {
                return;
            }

            AddDevDiskDirectorySnapshot(result, DevDiskPath                              );
            AddDevDiskDirectorySnapshot(result, Path.Combine(DevDiskPath, "by-id"       ));
            AddDevDiskDirectorySnapshot(result, Path.Combine(DevDiskPath, "by-label"    ));
            AddDevDiskDirectorySnapshot(result, Path.Combine(DevDiskPath, "by-partlabel"));
            AddDevDiskDirectorySnapshot(result, Path.Combine(DevDiskPath, "by-partuuid" ));
            AddDevDiskDirectorySnapshot(result, Path.Combine(DevDiskPath, "by-path"     ));
            AddDevDiskDirectorySnapshot(result, Path.Combine(DevDiskPath, "by-uuid"     ));
        }

        private static void AddDevDiskDirectorySnapshot(HashSet<string> result, string directory)
        {
            if (result == null || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    string name = Path.GetFileName(entry);
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.Add("devdisk:" + directory + ":" + name);
                }
            }
            catch
            {
            }
        }

        private static string BuildSysBlockDeviceSignature(string blockDirectory, string name)
        {
            var builder = new StringBuilder();

            builder.Append(name ?? string.Empty);

            builder.Append("|dev=");
            builder.Append(ReadFileString(Path.Combine(blockDirectory, "dev")));

            builder.Append("|size=");
            builder.Append(ReadFileString(Path.Combine(blockDirectory, "size")));

            builder.Append("|removable=");
            builder.Append(ReadFileString(Path.Combine(blockDirectory, "removable")));

            builder.Append("|ro=");
            builder.Append(ReadFileString(Path.Combine(blockDirectory, "ro")));

            builder.Append("|diskseq=");
            builder.Append(ReadFileString(Path.Combine(blockDirectory, "diskseq")));

            var partitions = ReadPartitionSignatures(blockDirectory);

            foreach (var partition in partitions)
            {
                builder.Append("|part=");
                builder.Append(partition);
            }

            return builder.ToString();
        }

        private static List<string> ReadPartitionSignatures(string blockDirectory)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(blockDirectory) || !Directory.Exists(blockDirectory))
            {
                return result;
            }

            try
            {
                foreach (var childDirectory in Directory.EnumerateDirectories(blockDirectory))
                {
                    if (!File.Exists(Path.Combine(childDirectory, "partition")))
                    {
                        continue;
                    }

                    string name = Path.GetFileName(childDirectory);

                    string dev   = ReadFileString(Path.Combine(childDirectory, "dev"  ));
                    string start = ReadFileString(Path.Combine(childDirectory, "start"));
                    string size  = ReadFileString(Path.Combine(childDirectory, "size" ));

                    result.Add((name ?? string.Empty) + ":" + dev + ":" + start + ":" + size);
                }
            }
            catch
            {
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static string ReadFileString(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return string.Empty;
                }

                return File.ReadAllText(path).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool SetEquals(HashSet<string> left, HashSet<string> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.SetEquals(right);
        }

        private static bool ShouldSkipSysBlockDevice(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            return name.StartsWith("loop", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ram" , StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("zram", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("dm-" , StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("md"  , StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("fd"  , StringComparison.OrdinalIgnoreCase);
        }

        private static void SignalPossibleChange(AutoResetEvent possibleChangeSignal)
        {
            if (possibleChangeSignal == null)
            {
                return;
            }

            try
            {
                possibleChangeSignal.Set();
            }
            catch
            {
            }
        }

        private static void SignalPossibleChange(Action signalPossibleChange)
        {
            try
            {
                signalPossibleChange?.Invoke();
            }
            catch
            {
            }
        }

        private static void DrainSignal(AutoResetEvent signal)
        {
            if (signal == null)
            {
                return;
            }

            try
            {
                while (signal.WaitOne(0))
                {
                }
            }
            catch
            {
            }
        }

        private static void QueueRescanSafely(Action queueRescan)
        {
            try
            {
                queueRescan();
            }
            catch
            {
            }
        }

        #endregion

        #region Nested Types

        private sealed class PossibleChangeSignalGate
        {
            #region Fields

            private readonly AutoResetEvent _signal;

            private readonly SignalRateLimiter _limiter = new SignalRateLimiter();

            #endregion

            #region Constructors

            public PossibleChangeSignalGate(AutoResetEvent signal)
            {
                _signal = signal;
            }

            #endregion

            #region Public

            public void Signal()
            {
                if (_limiter.TryAccept())
                {
                    SignalPossibleChange(_signal);
                }
            }

            #endregion
        }

        private sealed class SignalRateLimiter
        {
            #region Fields

            private readonly object _syncRoot = new object();

            private DateTime _lastSignalUtc = DateTime.MinValue;

            #endregion

            #region Public

            public bool TryAccept()
            {
                var now = DateTime.UtcNow;

                lock (_syncRoot)
                {
                    if (_lastSignalUtc != DateTime.MinValue
                     && (now - _lastSignalUtc).TotalMilliseconds < WatcherSignalMinimumIntervalMilliseconds)
                    {
                        return false;
                    }

                    _lastSignalUtc = now;
                    return true;
                }
            }

            #endregion
        }

        #endregion
    }
}
