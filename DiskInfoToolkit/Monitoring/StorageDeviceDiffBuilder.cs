/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;

namespace DiskInfoToolkit.Monitoring
{
    internal static class StorageDeviceDiffBuilder
    {
        #region Public

        public static StorageDevicesChangedEventArgs Build(List<StorageDevice> previous, List<StorageDevice> current)
        {
            var args = new StorageDevicesChangedEventArgs();
            args.Current = StorageDeviceCloneHelper.CloneList(current);

            var oldByKey = ToDictionary(previous);
            var newByKey = ToDictionary(current);

            foreach (var pair in newByKey)
            {
                if (!oldByKey.TryGetValue(pair.Key, out var oldDevice))
                {
                    args.Added.Add(StorageDeviceCloneHelper.Clone(pair.Value));
                    continue;
                }

                if (StorageDeviceSnapshotComparer.AreDifferent(oldDevice, pair.Value))
                {
                    args.Updated.Add(StorageDeviceCloneHelper.Clone(pair.Value));
                }
            }

            foreach (var pair in oldByKey)
            {
                if (!newByKey.ContainsKey(pair.Key))
                {
                    args.Removed.Add(StorageDeviceCloneHelper.Clone(pair.Value));
                }
            }

            return args;
        }

        #endregion

        #region Private

        private static Dictionary<string, StorageDevice> ToDictionary(List<StorageDevice> devices)
        {
            var result = new Dictionary<string, StorageDevice>(StringComparer.OrdinalIgnoreCase);

            if (devices == null)
            {
                return result;
            }

            foreach (var device in devices)
            {
                var key = StorageDeviceIdentityMatcher.GetStableKey(device);
                if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key))
                {
                    result.Add(key, device);
                }
            }

            return result;
        }

        #endregion
    }
}
