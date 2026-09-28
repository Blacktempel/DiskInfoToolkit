/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Utilities;
using System.Globalization;

namespace DiskInfoToolkit.Monitoring
{
    internal static class StorageDeviceIdentityMatcher
    {
        #region Public

        public static StorageDevice FindBestMatch(IEnumerable<StorageDevice> candidates, StorageDevice reference)
        {
            if (candidates == null || reference == null)
            {
                return null;
            }

            //Get a stable key for the reference device
            string stableKey = GetStableKey(reference);

            //Try to find a match based on the stable key
            if (!string.IsNullOrWhiteSpace(stableKey))
            {
                foreach (StorageDevice candidate in candidates)
                {
                    if (string.Equals(stableKey, GetStableKey(candidate), StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            //If no stable key match is found, try first matching by DeviceInstanceID
            foreach (StorageDevice candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(reference.DeviceInstanceID)
                    && string.Equals(reference.DeviceInstanceID, candidate.DeviceInstanceID, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            //Now try matching by DevicePath or AlternateDevicePath
            foreach (StorageDevice candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(reference.DevicePath)
                    && string.Equals(reference.DevicePath, candidate.DevicePath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                if (!string.IsNullOrWhiteSpace(reference.AlternateDevicePath)
                    && string.Equals(reference.AlternateDevicePath, candidate.AlternateDevicePath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            //Finally try matching by SerialNumber and ProductName as a last resort
            foreach (StorageDevice candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(reference.SerialNumber)
                    && string.Equals(reference.SerialNumber, candidate.SerialNumber, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(reference.ProductName ?? string.Empty, candidate.ProductName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            //No match found
            return null;
        }

        public static string GetStableKey(StorageDevice device)
        {
            if (device == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(device.DeviceInstanceID))
            {
                return "INSTANCE:" + StringUtil.TrimStorageString(device.DeviceInstanceID).ToUpperInvariant();
            }

            if (device.StorageDeviceNumber.HasValue)
            {
                return "DISK:" + device.StorageDeviceNumber.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(device.SerialNumber))
            {
                return "SERIAL:" + StringUtil.TrimStorageString(device.SerialNumber).ToUpperInvariant() + "|" + StringUtil.TrimStorageString(device.ProductName ?? string.Empty).ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(device.DevicePath))
            {
                return "PATH:" + StringUtil.TrimStorageString(device.DevicePath).ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(device.AlternateDevicePath))
            {
                return "ALT:" + StringUtil.TrimStorageString(device.AlternateDevicePath).ToUpperInvariant();
            }

            return string.Empty;
        }

        #endregion
    }
}
