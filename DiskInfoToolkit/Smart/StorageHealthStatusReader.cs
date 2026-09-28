/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Utilities;

namespace DiskInfoToolkit.Smart
{
    internal static class StorageHealthStatusReader
    {
        #region Fields

        private const int LifeCautionThreshold = 10;

        private const int LifeWarningThresholdMin = 5;

        private const int LifeWarningThresholdMax = 9;

        private const int LifeBadThresholdMax = 4;

        private const byte NvmeCriticalWarningAttributeId = 0xE0;

        private const byte NvmeAvailableSpareAttributeId = 0xE2;

        private const byte NvmeAvailableSpareThresholdAttributeId = 0xE3;

        #endregion

        #region Public

        public static StorageHealthStatus? GetHealthStatus(StorageDevice device)
        {
            if (device == null || device.SmartAttributes == null || device.SmartAttributes.Count == 0 || !device.SupportsSmart)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            if (IsNvme(device, profile))
            {
                return GetNvmeStatus(device);
            }

            return GetAtaLikeStatus(device, profile);
        }

        #endregion

        #region Private

        private static StorageHealthStatus? GetNvmeStatus(StorageDevice device)
        {
            string model = GetPrimaryModel(device);
            if (StartsWith(model, "Parallels")
                || StartsWith(model, "VMware")
                || StartsWith(model, "QEMU"))
            {
                return null;
            }

            StorageHealthStatus? result = null;

            var criticalWarning = FindAttribute(device, NvmeCriticalWarningAttributeId);
            if (criticalWarning != null && GetRawByte(criticalWarning.RawValue, 0) > 0)
            {
                return StorageHealthStatus.Bad;
            }

            var availableSpare          = FindAttribute(device, NvmeAvailableSpareAttributeId);
            var availableSpareThreshold = FindAttribute(device, NvmeAvailableSpareThresholdAttributeId);

            if (availableSpare != null && availableSpareThreshold != null)
            {
                int spare     = GetRawByte(availableSpare.RawValue, 0);
                int threshold = GetRawByte(availableSpareThreshold.RawValue, 0);

                if (threshold > 0 && threshold <= 100)
                {
                    if (spare < threshold)
                    {
                        return StorageHealthStatus.Bad;
                    }

                    if (spare == threshold && threshold != 100)
                    {
                        result = MaxStatus(result, StorageHealthStatus.Caution);
                    }
                }
            }

            result = MaxStatus(result, MapLifeToStatus(SmartAttributeSummaryReader.GetHealth(device)));
            return result;
        }

        private static StorageHealthStatus? GetAtaLikeStatus(StorageDevice device, SmartAttributeProfile profile)
        {
            if (HasDuplicateAttributeIds(device))
            {
                return null;
            }

            bool isSsd = IsSsdLikeProfile(profile);

            bool hasKnownBasis = false;
            StorageHealthStatus? result = null;

            if (isSsd)
            {
                if (HasSsdThresholdFailure(device))
                {
                    return StorageHealthStatus.Bad;
                }

                if (HasAnySsdThresholdAttribute(device))
                {
                    hasKnownBasis = true;
                }
            }
            else
            {
                if (HasHddThresholdFailure(device))
                {
                    return StorageHealthStatus.Bad;
                }

                if (HasHddSectorCaution(device))
                {
                    result = MaxStatus(result, StorageHealthStatus.Caution);
                    hasKnownBasis = true;
                }

                if (HasAnyHddThresholdAttribute(device) || HasAnyHddSectorAttribute(device))
                {
                    hasKnownBasis = true;
                }
            }

            int? life = SmartAttributeSummaryReader.GetHealth(device);
            var lifeStatus = MapLifeToStatus(life);

            if (lifeStatus.HasValue)
            {
                result = MaxStatus(result, lifeStatus);
                hasKnownBasis = true;
            }

            if (!hasKnownBasis)
            {
                return null;
            }

            return result ?? StorageHealthStatus.Good;
        }

        private static bool IsNvme(StorageDevice device, SmartAttributeProfile profile)
        {
            return profile == SmartAttributeProfile.NVMe
                || (device != null && device.Nvme != null && device.Nvme.SmartLogData != null && device.Nvme.SmartLogData.Length > 0);
        }

        private static bool IsSsdLikeProfile(SmartAttributeProfile profile)
        {
            return profile != SmartAttributeProfile.Unknown
                && profile != SmartAttributeProfile.Smart
                && profile != SmartAttributeProfile.NVMe;
        }

        private static StorageHealthStatus? MapLifeToStatus(int? life)
        {
            if (!life.HasValue)
            {
                return null;
            }

            int value = life.Value;
            if (value < 0)
            {
                return null;
            }

            if (value <= LifeBadThresholdMax)
            {
                return StorageHealthStatus.Bad;
            }

            if (value >= LifeWarningThresholdMin && value <= LifeWarningThresholdMax)
            {
                return StorageHealthStatus.Warning;
            }

            if (value <= LifeCautionThreshold)
            {
                return StorageHealthStatus.Caution;
            }

            return StorageHealthStatus.Good;
        }

        private static StorageHealthStatus? MaxStatus(StorageHealthStatus? left, StorageHealthStatus? right)
        {
            if (!left.HasValue)
            {
                return right;
            }

            if (!right.HasValue)
            {
                return left;
            }

            return GetSeverity(right.Value) > GetSeverity(left.Value) ? right : left;
        }

        private static int GetSeverity(StorageHealthStatus status)
        {
            switch (status)
            {
                case StorageHealthStatus.Good:
                    return 0;
                case StorageHealthStatus.Caution:
                    return 1;
                case StorageHealthStatus.Warning:
                    return 2;
                case StorageHealthStatus.Bad:
                    return 3;
                default:
                    return -1;
            }
        }

        private static bool HasDuplicateAttributeIds(StorageDevice device)
        {
            var ids = new HashSet<byte>();
            foreach (var entry in device.SmartAttributes)
            {
                if (entry == null)
                {
                    continue;
                }

                if (!ids.Add(entry.ID))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSsdThresholdFailure(StorageDevice device)
        {
            foreach (var entry in device.SmartAttributes)
            {
                if (entry == null)
                {
                    continue;
                }

                if (entry.ID == 0xC2)
                {
                    continue;
                }

                if (entry.ThresholdValue != 0 && entry.CurrentValue < entry.ThresholdValue)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnySsdThresholdAttribute(StorageDevice device)
        {
            foreach (var entry in device.SmartAttributes)
            {
                if (entry == null)
                {
                    continue;
                }

                if (entry.ID == 0xC2)
                {
                    continue;
                }

                if (entry.ThresholdValue != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasHddThresholdFailure(StorageDevice device)
        {
            foreach (var entry in device.SmartAttributes)
            {
                if (entry == null)
                {
                    continue;
                }

                if (IsHddThresholdMonitoredAttribute(entry.ID)
                    && entry.ThresholdValue != 0
                    && entry.CurrentValue < entry.ThresholdValue)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyHddThresholdAttribute(StorageDevice device)
        {
            foreach (var entry in device.SmartAttributes)
            {
                if (entry == null)
                {
                    continue;
                }

                if (IsHddThresholdMonitoredAttribute(entry.ID) && entry.ThresholdValue != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasHddSectorCaution(StorageDevice device)
        {
            foreach (var entry in device.SmartAttributes)
            {
                if (entry == null || !IsHddSectorCautionAttribute(entry.ID))
                {
                    continue;
                }

                uint raw = GetRawUInt32(entry);
                if (raw != 0 && raw != uint.MaxValue)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyHddSectorAttribute(StorageDevice device)
        {
            foreach (var entry in device.SmartAttributes)
            {
                if (entry != null && IsHddSectorCautionAttribute(entry.ID))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsHddSectorCautionAttribute(byte id)
        {
            return id == 0x05 || id == 0xC5 || id == 0xC6;
        }

        private static bool IsHddThresholdMonitoredAttribute(byte id)
        {
            return (id >= 0x01 && id <= 0x0D)
                || id == 0x16
                || (id >= 0xBB && id <= 0xBD)
                || (id >= 0xBF && id <= 0xC1)
                || (id >= 0xC3 && id <= 0xD1)
                || (id >= 0xD3 && id <= 0xD4)
                || (id >= 0xDC && id <= 0xE4)
                || (id >= 0xE6 && id <= 0xE7)
                || id == 0xF0
                || id == 0xFA
                || id == 0xFE;
        }

        private static SmartAttributeEntry FindAttribute(StorageDevice device, byte id)
        {
            if (device == null || device.SmartAttributes == null)
            {
                return null;
            }

            foreach (var entry in device.SmartAttributes)
            {
                if (entry != null && entry.ID == id)
                {
                    return entry;
                }
            }

            return null;
        }

        private static uint GetRawUInt32(SmartAttributeEntry entry)
        {
            return entry == null ? 0U : (uint)(entry.RawValue & 0xFFFFFFFFUL);
        }

        private static int GetRawByte(ulong rawValue, int index)
        {
            if (index < 0 || index > 7)
            {
                return 0;
            }

            return (int)((rawValue >> (index * 8)) & 0xFFUL);
        }

        private static string GetPrimaryModel(StorageDevice device)
        {
            var productName = StringUtil.TrimStorageString(device?.ProductName);
            if (!string.IsNullOrWhiteSpace(productName))
            {
                return productName;
            }

            return StringUtil.TrimStorageString(device?.DisplayName);
        }

        private static bool StartsWith(string value, string prefix)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(prefix)
                && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
