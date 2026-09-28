/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Localization;
using DiskInfoToolkit.Utilities;
using System.Globalization;
using System.Text;

namespace DiskInfoToolkit.Smart
{
    internal static class StorageHealthStatusReasonReader
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

        public static string GetHealthStatusReason(StorageDevice device)
        {
            if (device == null || device.SmartAttributes == null || device.SmartAttributes.Count == 0 || !device.SupportsSmart)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            if (IsNvme(device, profile))
            {
                return BuildNvmeReason(device);
            }

            return BuildAtaLikeReason(device, profile);
        }

        #endregion

        #region Private

        private static string BuildNvmeReason(StorageDevice device)
        {
            string model = GetPrimaryModel(device);
            if (StartsWith(model, "Parallels")
                || StartsWith(model, "VMware")
                || StartsWith(model, "QEMU"))
            {
                return null;
            }

            var builder = new StringBuilder();
            var textProvider = Storage.GetTextProvider();

            var criticalWarning = FindAttribute(device, NvmeCriticalWarningAttributeId);
            if (criticalWarning != null)
            {
                int criticalWarningValue = GetRawByte(criticalWarning.RawValue, 0);
                AppendNvmeCriticalWarningReasons(builder, textProvider, criticalWarningValue);
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
                        AppendLine(builder, Format(textProvider, StorageHealthStatusReasonTextKeys.NvmeAvailableSpareBelowThreshold, spare, threshold));
                    }
                    else if (spare == threshold && threshold != 100)
                    {
                        AppendLine(builder, Format(textProvider, StorageHealthStatusReasonTextKeys.NvmeAvailableSpareAtThreshold, spare, threshold));
                    }
                }
            }

            AppendLifeReason(builder, textProvider, SmartAttributeSummaryReader.GetHealth(device));
            return builder.Length == 0 ? null : builder.ToString();
        }

        private static string BuildAtaLikeReason(StorageDevice device, SmartAttributeProfile profile)
        {
            if (HasDuplicateAttributeIds(device))
            {
                return null;
            }

            var builder = new StringBuilder();
            var textProvider = Storage.GetTextProvider();

            bool isSsd = IsSsdLikeProfile(profile);

            foreach (var entry in device.SmartAttributes)
            {
                if (entry == null)
                {
                    continue;
                }

                if (isSsd)
                {
                    if (entry.ID == 0xC2)
                    {
                        continue;
                    }

                    if (entry.ThresholdValue != 0 && entry.CurrentValue < entry.ThresholdValue)
                    {
                        AppendAttributeBelowThresholdReason(builder, textProvider, profile, entry);
                    }

                    continue;
                }

                if (IsHddThresholdMonitoredAttribute(entry.ID)
                    && entry.ThresholdValue != 0
                    && entry.CurrentValue < entry.ThresholdValue)
                {
                    AppendAttributeBelowThresholdReason(builder, textProvider, profile, entry);
                }

                if (IsHddSectorCautionAttribute(entry.ID))
                {
                    uint raw = GetRawUInt32(entry);
                    if (raw != 0 && raw != uint.MaxValue)
                    {
                        AppendAttributeRawValueReason(builder, textProvider, profile, entry, raw);
                    }
                }
            }

            AppendLifeReason(builder, textProvider, SmartAttributeSummaryReader.GetHealth(device));
            return builder.Length == 0 ? null : builder.ToString();
        }

        private static void AppendNvmeCriticalWarningReasons(StringBuilder builder, ILocalizedTextProvider textProvider, int criticalWarningValue)
        {
            if ((criticalWarningValue & 0x01) != 0)
            {
                AppendLine(builder, GetText(textProvider, StorageHealthStatusReasonTextKeys.NvmeAvailableSpareBelowThreshold));
            }

            if ((criticalWarningValue & 0x02) != 0)
            {
                AppendLine(builder, GetText(textProvider, StorageHealthStatusReasonTextKeys.NvmeTemperatureError));
            }

            if ((criticalWarningValue & 0x04) != 0)
            {
                AppendLine(builder, GetText(textProvider, StorageHealthStatusReasonTextKeys.NvmeSubsystemReliabilityDegraded));
            }

            if ((criticalWarningValue & 0x08) != 0)
            {
                AppendLine(builder, GetText(textProvider, StorageHealthStatusReasonTextKeys.NvmeReadOnlyMode));
            }

            if ((criticalWarningValue & 0x10) != 0)
            {
                AppendLine(builder, GetText(textProvider, StorageHealthStatusReasonTextKeys.NvmeVolatileMemoryBackupFailed));
            }

            if ((criticalWarningValue & 0x20) != 0)
            {
                AppendLine(builder, GetText(textProvider, StorageHealthStatusReasonTextKeys.NvmePersistentMemoryReadOnly));
            }
        }

        private static void AppendAttributeBelowThresholdReason(StringBuilder builder, ILocalizedTextProvider textProvider, SmartAttributeProfile profile, SmartAttributeEntry entry)
        {
            var attributeName = GetAttributeName(profile, textProvider, entry);

            var formatted = Format(
                textProvider,
                StorageHealthStatusReasonTextKeys.AttributeBelowThreshold,
                entry.ID.ToString("X2", CultureInfo.InvariantCulture),
                attributeName,
                entry.CurrentValue,
                entry.ThresholdValue);

            AppendLine(builder, formatted);
        }

        private static void AppendAttributeRawValueReason(StringBuilder builder, ILocalizedTextProvider textProvider, SmartAttributeProfile profile, SmartAttributeEntry entry, uint rawValue)
        {
            var attributeName = GetAttributeName(profile, textProvider, entry);

            var formatted = Format(
                textProvider,
                StorageHealthStatusReasonTextKeys.AttributeRawValueNonZero,
                entry.ID.ToString("X2", CultureInfo.InvariantCulture),
                attributeName,
                rawValue);

            AppendLine(builder, formatted);
        }

        private static void AppendLifeReason(StringBuilder builder, ILocalizedTextProvider textProvider, int? life)
        {
            if (!life.HasValue || life.Value < 0)
            {
                return;
            }

            string reasonKey = null;
            if (life.Value <= LifeBadThresholdMax)
            {
                reasonKey = StorageHealthStatusReasonTextKeys.RemainingLifeCritical;
            }
            else if (life.Value >= LifeWarningThresholdMin && life.Value <= LifeWarningThresholdMax)
            {
                reasonKey = StorageHealthStatusReasonTextKeys.RemainingLifeVeryLow;
            }
            else if (life.Value <= LifeCautionThreshold)
            {
                reasonKey = StorageHealthStatusReasonTextKeys.RemainingLifeLow;
            }

            if (string.IsNullOrWhiteSpace(reasonKey))
            {
                return;
            }

            AppendLine(builder, Format(textProvider, reasonKey, life.Value));
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

        private static string GetAttributeName(SmartAttributeProfile profile, ILocalizedTextProvider textProvider, SmartAttributeEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            var name = SmartAttributeCatalog.GetDisplayName(profile, entry.ID, textProvider);

            name = StringUtil.TrimStorageString(name);

            return string.IsNullOrWhiteSpace(name)
                ? SmartAttributeCatalog.BuildUnknownAttributeName(entry.ID)
                : name;
        }

        private static string GetText(ILocalizedTextProvider textProvider, string reasonKey)
        {
            if (textProvider == null || string.IsNullOrWhiteSpace(reasonKey))
            {
                return null;
            }

            return StringUtil.TrimStorageString(textProvider.GetText(reasonKey));
        }

        private static string Format(ILocalizedTextProvider textProvider, string reasonKey, params object[] arguments)
        {
            var format = GetText(textProvider, reasonKey);
            if (string.IsNullOrWhiteSpace(format))
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, format, arguments);
        }

        private static void AppendLine(StringBuilder builder, string value)
        {
            value = StringUtil.TrimStorageString(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(value);
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
