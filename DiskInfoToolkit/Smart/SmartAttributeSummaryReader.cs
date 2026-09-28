/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;

namespace DiskInfoToolkit.Smart
{
    internal static class SmartAttributeSummaryReader
    {
        #region Fields

        private const ushort NvmeDefaultWarningCompositeTemperatureKelvin = 0x0157;

        private const ushort NvmeDefaultCriticalCompositeTemperatureKelvin = 0x015C;

        private const byte PowerOnCountAttributeId = 0x0C;

        private const byte PowerOnHoursAttributeId = 0x09;

        private const byte TemperatureAttributeId = 0xC2;

        private const byte AirflowTemperatureAttributeId = 0xBE;

        private const byte YmtcTemperatureAttributeId = 0xF3;

        private const byte GenericTemperatureAttributeId = 0xE7;

        private const byte NvmeCompositeTemperatureAttributeId = 0xE1;

        private const byte NvmePercentageUsedAttributeId = 0xE4;

        private const byte NvmeDataUnitsReadAttributeId = 0xE5;

        private const byte NvmeDataUnitsWrittenAttributeId = 0xE6;

        private const byte NvmePowerCyclesAttributeId = 0xEA;

        private const byte NvmePowerOnHoursAttributeId = 0xEB;

        private const ulong BytesPerGigabyte = 1000UL * 1000UL * 1000UL;

        private const ulong BytesPerMiB = 1024UL * 1024UL;

        #endregion

        #region Public

        public static int? GetTemperature(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            if (IsNvme(device, profile))
            {
                var nvmeTemperature = FindAttribute(device, NvmeCompositeTemperatureAttributeId);
                if (nvmeTemperature == null)
                {
                    return null;
                }

                return KelvinToCelsius((ushort)nvmeTemperature.RawValue);
            }

            int? value = ExtractAtaTemperatureFromC2(device, FindAttribute(device, TemperatureAttributeId));
            if (value.HasValue)
            {
                return value;
            }

            value = ExtractAtaTemperatureFromByte0(FindAttribute(device, AirflowTemperatureAttributeId));
            if (value.HasValue)
            {
                return value;
            }

            if (profile == SmartAttributeProfile.Ymtc)
            {
                value = ExtractAtaTemperatureFromByte0(FindAttribute(device, YmtcTemperatureAttributeId));
                if (value.HasValue)
                {
                    return value;
                }
            }

            if (profile == SmartAttributeProfile.Smart || profile == SmartAttributeProfile.Unknown)
            {
                value = ExtractAtaTemperatureFromByte0(FindAttribute(device, GenericTemperatureAttributeId));
                if (value.HasValue)
                {
                    return value;
                }
            }

            return null;
        }

        public static int? GetHealth(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            var settings = SmartAttributeSummarySettingsResolver.Resolve(device);

            if (IsNvme(device, profile))
            {
                var percentageUsed = FindAttribute(device, NvmePercentageUsedAttributeId);
                if (percentageUsed == null)
                {
                    return null;
                }

                int used = (int)percentageUsed.RawValue;
                if (used < 0)
                {
                    return null;
                }

                return ClampPercentage(100 - used);
            }

            int? value = GetLifeFromSpecialAttribute(device, profile, settings);
            if (value.HasValue)
            {
                return value;
            }

            return null;
        }

        public static ulong? GetHostReads(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            var settings = SmartAttributeSummarySettingsResolver.Resolve(device);

            if (IsNvme(device, profile))
            {
                var entry = FindAttribute(device, NvmeDataUnitsReadAttributeId);
                return entry == null ? null : ConvertNvmeDataUnitsToBytes(entry.RawValue);
            }

            var attribute = FindAttribute(device, 0xF2);
            if (attribute == null)
            {
                return null;
            }

            if (profile == SmartAttributeProfile.Toshiba && settings.HostReadWriteUnit == SmartHostReadWriteUnit.UnitGigabytes)
            {
                return ConvertUnitsToBytes(GetRawUInt32(attribute), BytesPerGigabyte);
            }

            if (profile == SmartAttributeProfile.SiliconMotionCVC && settings.HostReadWriteUnit == SmartHostReadWriteUnit.UnitGigabytes)
            {
                return ConvertUnitsToBytes(GetRawUInt32(attribute), BytesPerGigabyte);
            }

            if (profile == SmartAttributeProfile.Intel
                || profile == SmartAttributeProfile.Toshiba
                || profile == SmartAttributeProfile.SiliconMotion)
            {
                return Convert32MiBUnitsToBytes(attribute.RawValue);
            }

            if (profile == SmartAttributeProfile.Samsung)
            {
                return ConvertHostUnitsToBytes(attribute, settings.HostReadWriteUnit, true, SmartHostReadWriteUnit.Unit512Bytes);
            }

            if (profile == SmartAttributeProfile.JMicron60x
                || profile == SmartAttributeProfile.JMicron61x
                || profile == SmartAttributeProfile.JMicron66x)
            {
                return ConvertHostUnitsToBytes(attribute, SmartHostReadWriteUnit.Unit512Bytes, false, SmartHostReadWriteUnit.Unit512Bytes);
            }

            if (profile == SmartAttributeProfile.Plextor)
            {
                return Convert32MiBUnitsToBytes(attribute.RawValue);
            }

            if (IsSanDiskGbStyleProfile(profile) && settings.HostReadWriteUnit == SmartHostReadWriteUnit.UnitGigabytes)
            {
                return ConvertUnitsToBytes(attribute.RawValue, BytesPerGigabyte);
            }

            if (IsSanDiskBaseProfile(profile))
            {
                return ConvertHostUnitsToBytes(attribute, settings.HostReadWriteUnit, false, SmartHostReadWriteUnit.Unit512Bytes);
            }

            if (profile == SmartAttributeProfile.SSD)
            {
                return ConvertHostUnitsToBytes(attribute, settings.HostReadWriteUnit, false, SmartHostReadWriteUnit.Unknown);
            }

            if (UsesConfiguredHostUnits(profile))
            {
                return ConvertHostUnitsToBytes(attribute, settings.HostReadWriteUnit, false, SmartHostReadWriteUnit.UnitGigabytes);
            }

            return null;
        }

        public static ulong? GetHostWrites(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            var settings = SmartAttributeSummarySettingsResolver.Resolve(device);

            if (IsNvme(device, profile))
            {
                var entry = FindAttribute(device, NvmeDataUnitsWrittenAttributeId);
                return entry == null ? null : ConvertNvmeDataUnitsToBytes(entry.RawValue);
            }

            if (profile == SmartAttributeProfile.Ocz)
            {
                var ocz = FindAttribute(device, 0xE8);
                if (ocz != null)
                {
                    return Convert512ByteUnitsToBytes(ocz.RawValue);
                }
            }

            if (profile == SmartAttributeProfile.Intel)
            {
                var intel = FindAttribute(device, 0xE1);
                if (intel != null)
                {
                    return Convert32MiBUnitsToBytes(intel.RawValue);
                }
            }

            if (profile == SmartAttributeProfile.IntelDc)
            {
                var intelDc = FindAttribute(device, 0xEB);
                if (intelDc != null)
                {
                    return Convert32MiBUnitsToBytes(intelDc.RawValue);
                }
            }

            if (profile == SmartAttributeProfile.Micron || profile == SmartAttributeProfile.MicronMU03)
            {
                var micron = FindAttribute(device, 0xF6);
                if (micron != null)
                {
                    return Convert512ByteUnitsToBytes(micron.RawValue);
                }
            }

            var attribute = FindAttribute(device, 0xF1);
            if (attribute == null)
            {
                return null;
            }

            if (profile == SmartAttributeProfile.IntelDc)
            {
                return null;
            }

            if (profile == SmartAttributeProfile.Toshiba && settings.HostReadWriteUnit == SmartHostReadWriteUnit.UnitGigabytes)
            {
                return ConvertUnitsToBytes(GetRawUInt32(attribute), BytesPerGigabyte);
            }

            if (profile == SmartAttributeProfile.SiliconMotionCVC && settings.HostReadWriteUnit == SmartHostReadWriteUnit.UnitGigabytes)
            {
                return ConvertUnitsToBytes(GetRawUInt32(attribute), BytesPerGigabyte);
            }

            if (profile == SmartAttributeProfile.Intel
                || profile == SmartAttributeProfile.Toshiba
                || profile == SmartAttributeProfile.Kioxia
                || profile == SmartAttributeProfile.SiliconMotion)
            {
                return Convert32MiBUnitsToBytes(attribute.RawValue);
            }

            if (profile == SmartAttributeProfile.Samsung)
            {
                return ConvertHostUnitsToBytes(attribute, settings.HostReadWriteUnit, true, SmartHostReadWriteUnit.Unit512Bytes);
            }

            if (profile == SmartAttributeProfile.Apacer)
            {
                return ConvertHostUnitsToBytes(attribute, settings.HostReadWriteUnit, false, SmartHostReadWriteUnit.Unit512Bytes);
            }

            if (profile == SmartAttributeProfile.JMicron60x
                || profile == SmartAttributeProfile.JMicron61x
                || profile == SmartAttributeProfile.JMicron66x)
            {
                return ConvertHostUnitsToBytes(attribute, SmartHostReadWriteUnit.Unit512Bytes, false, SmartHostReadWriteUnit.Unit512Bytes);
            }

            if (profile == SmartAttributeProfile.Plextor)
            {
                return Convert32MiBUnitsToBytes(attribute.RawValue);
            }

            if (IsSanDiskGbStyleProfile(profile) && settings.HostReadWriteUnit == SmartHostReadWriteUnit.UnitGigabytes)
            {
                return ConvertUnitsToBytes(attribute.RawValue, BytesPerGigabyte);
            }

            if (IsSanDiskBaseProfile(profile))
            {
                return ConvertHostUnitsToBytes(attribute, settings.HostReadWriteUnit, false, SmartHostReadWriteUnit.Unit512Bytes);
            }

            if (profile == SmartAttributeProfile.SSD)
            {
                return ConvertHostUnitsToBytes(attribute, settings.HostReadWriteUnit, false, SmartHostReadWriteUnit.Unknown);
            }

            if (UsesConfiguredHostUnits(profile))
            {
                return ConvertHostUnitsToBytes(attribute, settings.HostReadWriteUnit, false, SmartHostReadWriteUnit.UnitGigabytes);
            }

            return null;
        }

        public static ulong? GetNandWrites(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            var settings = SmartAttributeSummarySettingsResolver.Resolve(device);

            if (IsNvme(device, profile))
            {
                return null;
            }

            if (profile == SmartAttributeProfile.IntelDc)
            {
                var f1 = FindAttribute(device, 0xF1);
                if (f1 != null)
                {
                    return Convert32MiBUnitsToBytes(f1.RawValue);
                }
            }

            var f9 = FindAttribute(device, 0xF9);
            if (f9 != null)
            {
                if (profile == SmartAttributeProfile.Intel
                    || profile == SmartAttributeProfile.Realtek
                    || profile == SmartAttributeProfile.WDC
                    || profile == SmartAttributeProfile.SanDiskHP
                    || profile == SmartAttributeProfile.SanDiskHPVenus
                    || profile == SmartAttributeProfile.SanDiskLenovoHelenVenus
                    || (IsSanDiskGbStyleProfile(profile) && settings.HostReadWriteUnit == SmartHostReadWriteUnit.UnitGigabytes))
                {
                    return ConvertUnitsToBytes(GetRawUInt32(f9), BytesPerGigabyte);
                }

                if (profile == SmartAttributeProfile.OczVector)
                {
                    return ConvertUnitsToBytes(f9.RawValue, 16UL * 1024UL);
                }
            }

            var e9 = FindAttribute(device, 0xE9);
            if (e9 != null)
            {
                if ((profile == SmartAttributeProfile.SanDisk
                     || profile == SmartAttributeProfile.SanDiskGb
                     || profile == SmartAttributeProfile.SanDiskLenovo
                     || profile == SmartAttributeProfile.SanDiskCloud)
                    && settings.HostReadWriteUnit == SmartHostReadWriteUnit.UnitGigabytes)
                {
                    ulong raw = GetRawUInt32(e9);
                    if (settings.NandWriteUnit == SmartNandWriteUnit.Unit1MiB)
                    {
                        return ConvertUnitsToBytes(raw, BytesPerMiB);
                    }

                    return ConvertUnitsToBytes(raw, BytesPerGigabyte);
                }

                if (profile == SmartAttributeProfile.Plextor
                    || profile == SmartAttributeProfile.Kingston
                    || profile == SmartAttributeProfile.KingstonSuv
                    || profile == SmartAttributeProfile.KingstonKC600
                    || profile == SmartAttributeProfile.KingstonDC500
                    || profile == SmartAttributeProfile.WDC
                    || profile == SmartAttributeProfile.Ssstc
                    || profile == SmartAttributeProfile.Seagate
                    || profile == SmartAttributeProfile.SeagateIronWolf
                    || profile == SmartAttributeProfile.SeagateBarraCuda
                    || profile == SmartAttributeProfile.Ymtc
                    || profile == SmartAttributeProfile.SiliconMotionCVC)
                {
                    return ConvertUnitsToBytes(GetRawUInt32(e9), BytesPerGigabyte);
                }

                if (profile == SmartAttributeProfile.JMicron60x
                    || profile == SmartAttributeProfile.JMicron61x
                    || profile == SmartAttributeProfile.JMicron66x
                    || profile == SmartAttributeProfile.AdataIndustrial)
                {
                    return Convert512ByteUnitsToBytes(e9.RawValue);
                }

                if (profile == SmartAttributeProfile.Maxiotek)
                {
                    return settings.HostReadWriteUnit == SmartHostReadWriteUnit.Unit512Bytes
                        ? Convert512ByteUnitsToBytes(e9.RawValue)
                        : ConvertUnitsToBytes(GetRawUInt32(e9), BytesPerGigabyte);
                }
            }

            var ea = FindAttribute(device, 0xEA);
            if (ea != null)
            {
                if (profile == SmartAttributeProfile.Kingston
                    || profile == SmartAttributeProfile.Seagate
                    || (profile == SmartAttributeProfile.SKhynix && settings.HostReadWriteUnit == SmartHostReadWriteUnit.UnitGigabytes))
                {
                    return ConvertUnitsToBytes(GetRawUInt32(ea), BytesPerGigabyte);
                }
            }

            var f5 = FindAttribute(device, 0xF5);
            if (f5 != null)
            {
                if (profile == SmartAttributeProfile.Micron)
                {
                    return ConvertUnitsToBytes(f5.RawValue, 8UL * 1024UL);
                }

                if (profile == SmartAttributeProfile.MicronMU03
                    || profile == SmartAttributeProfile.KingstonKC600
                    || profile == SmartAttributeProfile.SiliconMotion
                    || profile == SmartAttributeProfile.Scy)
                {
                    return Convert32MiBUnitsToBytes(f5.RawValue);
                }

                if (profile == SmartAttributeProfile.Recadata)
                {
                    return ConvertUnitsToBytes(f5.RawValue, BytesPerGigabyte);
                }
            }

            var fa = FindAttribute(device, 0xFA);
            if (fa != null && profile == SmartAttributeProfile.Realtek)
            {
                return ConvertUnitsToBytes(GetRawUInt32(fa), BytesPerGigabyte);
            }

            return null;
        }

        public static ulong? GetBytesErased(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            if (profile != SmartAttributeProfile.SandForce)
            {
                return null;
            }

            var entry = FindAttribute(device, 0x64);
            return entry == null ? null : ConvertUnitsToBytes(GetRawUInt32(entry), BytesPerGigabyte);
        }

        public static int? GetWearLevelingCount(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            if (profile != SmartAttributeProfile.Samsung)
            {
                return null;
            }

            var entry = FindAttribute(device, 0xB1);
            if (entry == null)
            {
                return null;
            }

            return (int)GetRawUInt32(entry);
        }

        public static ulong? GetPowerOnCount(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            if (IsNvme(device, profile))
            {
                var nvmePowerCycles = FindAttribute(device, NvmePowerCyclesAttributeId);
                return nvmePowerCycles?.RawValue;
            }

            if (profile == SmartAttributeProfile.SanDiskCloud)
            {
                ulong count = 0;
                bool found = false;

                var cleanShutdowns = FindAttribute(device, 0xBF);
                if (cleanShutdowns != null)
                {
                    count += GetRawUInt32(cleanShutdowns) + 1UL;
                    found = true;
                }

                var uncleanShutdowns = FindAttribute(device, 0xC0);
                if (uncleanShutdowns != null)
                {
                    count += GetRawUInt32(uncleanShutdowns);
                    found = true;
                }

                if (found)
                {
                    return count;
                }
            }

            var entry = FindAttribute(device, PowerOnCountAttributeId, 0xC0);
            if (entry == null)
            {
                return null;
            }

            if (profile == SmartAttributeProfile.Indilinx)
            {
                return (ulong)((entry.WorstValue << 8) + entry.CurrentValue);
            }

            return GetRawUInt32(entry);
        }

        public static ulong? GetPowerOnHours(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            if (IsNvme(device, profile))
            {
                var nvmePowerOnHours = FindAttribute(device, NvmePowerOnHoursAttributeId);
                return nvmePowerOnHours?.RawValue;
            }

            var entry = FindAttribute(device, PowerOnHoursAttributeId);
            if (entry == null)
            {
                return null;
            }

            if (profile == SmartAttributeProfile.Indilinx)
            {
                return (ulong)((entry.WorstValue << 8) + entry.CurrentValue);
            }

            return GetRawUInt32(entry);
        }

        public static int? GetTemperatureWarning(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            if (!IsNvme(device, profile))
            {
                return null;
            }

            ushort kelvin = GetNvmeIdentifyTemperatureThresholdKelvin(device, 266, NvmeDefaultWarningCompositeTemperatureKelvin);
            return KelvinToCelsius(kelvin);
        }

        public static int? GetTemperatureCritical(StorageDevice device)
        {
            if (device == null)
            {
                return null;
            }

            var profile = SmartAttributeProfileResolver.Resolve(device);
            if (!IsNvme(device, profile))
            {
                return null;
            }

            ushort kelvin = GetNvmeIdentifyTemperatureThresholdKelvin(device, 268, NvmeDefaultCriticalCompositeTemperatureKelvin);
            return KelvinToCelsius(kelvin);
        }

        #endregion

        #region Private

        private static bool IsNvme(StorageDevice device, SmartAttributeProfile profile)
        {
            return profile == SmartAttributeProfile.NVMe
                || (device != null && device.Nvme.SmartLogData != null && device.Nvme.SmartLogData.Length > 0);
        }

        private static int? GetLifeFromSpecialAttribute(StorageDevice device, SmartAttributeProfile profile, SmartAttributeSummarySettings settings)
        {
            SmartAttributeEntry entry;

            if (profile == SmartAttributeProfile.Samsung)
            {
                entry = FindAttribute(device, 0xB1);
                if (entry != null)
                {
                    return ClampPercentage(entry.CurrentValue);
                }

                entry = FindAttribute(device, 0xE9);
                if (entry != null)
                {
                    return ClampPercentage(entry.CurrentValue);
                }
            }

            if (profile == SmartAttributeProfile.Toshiba || profile == SmartAttributeProfile.Kioxia)
            {
                entry = FindAttribute(device, 0xAD);
                if (entry != null)
                {
                    int current = entry.CurrentValue;
                    if (current >= 100 && current <= 200)
                    {
                        return ClampPercentage(current - 100);
                    }

                    return ClampPercentage(current);
                }
            }

            if (profile == SmartAttributeProfile.Micron
                || profile == SmartAttributeProfile.MicronMU02
                || profile == SmartAttributeProfile.MicronMU03
                || profile == SmartAttributeProfile.IntelDc
                || profile == SmartAttributeProfile.SiliconMotionCVC)
            {
                entry = FindAttribute(device, 0xCA);
                if (entry != null)
                {
                    return ClampPercentage(entry.CurrentValue);
                }
            }

            if (profile == SmartAttributeProfile.Plextor)
            {
                entry = FindAttribute(device, 0xE8);
                if (entry != null)
                {
                    return ClampPercentage(entry.CurrentValue);
                }
            }

            if (profile == SmartAttributeProfile.Indilinx)
            {
                entry = FindAttribute(device, 0xD1);
                if (entry != null)
                {
                    return ClampPercentage(entry.CurrentValue);
                }
            }

            if (profile == SmartAttributeProfile.SanDiskHP
                || profile == SmartAttributeProfile.SanDiskHPVenus)
            {
                entry = FindAttribute(device, 0xC9);
                if (entry != null)
                {
                    return ClampPercentage(entry.CurrentValue);
                }
            }

            if (profile == SmartAttributeProfile.Realtek
                || profile == SmartAttributeProfile.SiliconMotion
                || ((profile == SmartAttributeProfile.Kingston
                    || profile == SmartAttributeProfile.KingstonKC600)
                    && settings.HostReadWriteUnit == SmartHostReadWriteUnit.Unit32MiB))
            {
                entry = FindAttribute(device, 0xA9);
                if (entry != null)
                {
                    return EvaluateLifeWithFlags(entry, settings.LifeFlags);
                }
            }

            if (profile == SmartAttributeProfile.WDC
                || IsSanDiskBaseProfile(profile))
            {
                int? sandiskLife = EvaluateSanDiskAndWdcLife(device, profile, settings);
                if (sandiskLife.HasValue)
                {
                    return sandiskLife;
                }
            }

            if (profile == SmartAttributeProfile.Intel
                || profile == SmartAttributeProfile.Ocz
                || profile == SmartAttributeProfile.OczVector
                || profile == SmartAttributeProfile.SKhynix)
            {
                entry = FindAttribute(device, 0xE9);
                if (entry != null)
                {
                    return EvaluateLifeWithFlags(entry, settings.LifeFlags);
                }
            }

            if (profile == SmartAttributeProfile.JMicron60x
                || profile == SmartAttributeProfile.JMicron61x
                || profile == SmartAttributeProfile.JMicron66x
                || profile == SmartAttributeProfile.MicronMU03)
            {
                entry = FindAttribute(device, 0xF2);
                if (entry != null)
                {
                    return ClampPercentage(entry.CurrentValue);
                }
            }

            entry = FindAttribute(device, 0xE7);
            if (entry != null)
            {
                return EvaluateLifeWithFlags(entry, settings.LifeFlags);
            }

            return null;
        }

        private static int? EvaluateSanDiskAndWdcLife(StorageDevice device, SmartAttributeProfile profile, SmartAttributeSummarySettings settings)
        {
            if (profile == SmartAttributeProfile.SanDiskCloud)
            {
                var f5 = FindAttribute(device, 0xF5);
                if (f5 != null)
                {
                    return ClampPercentage(f5.CurrentValue);
                }

                return null;
            }

            if (profile == SmartAttributeProfile.SanDiskLenovoHelenVenus)
            {
                var e9 = FindAttribute(device, 0xE9);
                if (e9 != null)
                {
                    return ClampPercentage(e9.CurrentValue);
                }
            }

            var e6 = FindAttribute(device, 0xE6);
            if (e6 == null)
            {
                return null;
            }

            if (profile == SmartAttributeProfile.SanDiskDell)
            {
                return ClampPercentage(e6.CurrentValue);
            }

            if (settings.LifeFlags.HasFlag(SmartLifeInterpretationFlags.SanDiskUsbMemory))
            {
                return null;
            }

            if (settings.LifeFlags.HasFlag(SmartLifeInterpretationFlags.SanDiskHundredthsInTwoBytes))
            {
                int value = 100 - ((GetRawByte(e6.RawValue, 1) * 256) + GetRawByte(e6.RawValue, 0)) / 100;
                return ClampPercentage(value);
            }

            if (settings.LifeFlags.HasFlag(SmartLifeInterpretationFlags.SanDiskCurrentValue))
            {
                return ClampPercentage(e6.CurrentValue);
            }

            int defaultValue = 100 - GetRawByte(e6.RawValue, 1);
            return ClampPercentage(defaultValue);
        }

        private static int? EvaluateLifeWithFlags(SmartAttributeEntry entry, SmartLifeInterpretationFlags flags)
        {
            if (entry == null)
            {
                return null;
            }

            if (flags.HasFlag(SmartLifeInterpretationFlags.RawValueIncrement))
            {
                return ClampPercentage(100 - GetRawByte(entry.RawValue, 0));
            }

            if (flags.HasFlag(SmartLifeInterpretationFlags.RawValue))
            {
                return ClampPercentage(GetRawByte(entry.RawValue, 0));
            }

            return ClampPercentage(entry.CurrentValue);
        }

        private static ushort GetNvmeIdentifyTemperatureThresholdKelvin(StorageDevice device, int offset, ushort defaultValue)
        {
            ushort? value = TryReadNvmeIdentifyThresholdKelvin(device?.Nvme.IdentifyControllerData, offset);
            if (!value.HasValue)
            {
                value = TryReadNvmeIdentifyThresholdKelvin(device?.Nvme.IntelIdentifyControllerData, offset);
            }

            ushort threshold = value.GetValueOrDefault();
            return threshold == 0 ? defaultValue : threshold;
        }

        private static ushort? TryReadNvmeIdentifyThresholdKelvin(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 2 > data.Length)
            {
                return null;
            }

            return BitConverter.ToUInt16(data, offset);
        }

        private static SmartAttributeEntry FindAttribute(StorageDevice device, params byte[] ids)
        {
            if (device == null || device.SmartAttributes == null || ids == null || ids.Length == 0)
            {
                return null;
            }

            for (int idIndex = 0; idIndex < ids.Length; ++idIndex)
            {
                byte id = ids[idIndex];

                foreach (SmartAttributeEntry entry in device.SmartAttributes)
                {
                    if (entry != null && entry.ID == id)
                    {
                        return entry;
                    }
                }
            }

            return null;
        }

        private static int? ExtractAtaTemperatureFromC2(StorageDevice device, SmartAttributeEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            int raw0 = GetRawByte(entry.RawValue, 0);
            int raw1 = GetRawByte(entry.RawValue, 1);

            uint rawUInt = (uint)(entry.RawValue & 0xFFFFFFFFUL);

            string text = (device?.ProductName ?? string.Empty) + " " + (device?.DisplayName ?? string.Empty);

            int? value = null;
            if (text.IndexOf("SAMSUNG SV", StringComparison.OrdinalIgnoreCase) >= 0 && (raw1 != 0 || raw0 > 70))
            {
                value = (int)(rawUInt / 10U);
            }
            else if (raw0 > 0)
            {
                value = raw0;
            }
            else if (rawUInt > 0 && rawUInt < 1000)
            {
                value = (int)(rawUInt / 10U);
            }

            if (!value.HasValue || value.Value >= 100)
            {
                return null;
            }

            return value;
        }

        private static int? ExtractAtaTemperatureFromByte0(SmartAttributeEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            int value = GetRawByte(entry.RawValue, 0);
            if (value <= 0 || value >= 100)
            {
                return null;
            }

            return value;
        }

        private static int GetRawByte(ulong rawValue, int index)
        {
            if (index < 0 || index > 7)
            {
                return 0;
            }

            return (int)((rawValue >> (index * 8)) & 0xFFUL);
        }

        private static ulong GetRawUInt32(SmartAttributeEntry entry)
        {
            return entry == null ? 0UL : (entry.RawValue & 0xFFFFFFFFUL);
        }

        private static ulong? ConvertHostUnitsToBytes(
            SmartAttributeEntry entry,
            SmartHostReadWriteUnit unit,
            bool preferUInt32ForGigabytes,
            SmartHostReadWriteUnit defaultUnit)
        {
            if (entry == null)
            {
                return null;
            }

            var effectiveUnit = unit == SmartHostReadWriteUnit.Unknown ? defaultUnit : unit;
            switch (effectiveUnit)
            {
                case SmartHostReadWriteUnit.Unit512Bytes:
                    return Convert512ByteUnitsToBytes(entry.RawValue);
                case SmartHostReadWriteUnit.Unit1MiB:
                    return ConvertUnitsToBytes(entry.RawValue, BytesPerMiB);
                case SmartHostReadWriteUnit.Unit16MiB:
                    return ConvertUnitsToBytes(entry.RawValue, 16UL * BytesPerMiB);
                case SmartHostReadWriteUnit.Unit32MiB:
                    return Convert32MiBUnitsToBytes(entry.RawValue);
                case SmartHostReadWriteUnit.UnitGigabytes:
                    return ConvertUnitsToBytes(preferUInt32ForGigabytes ? GetRawUInt32(entry) : entry.RawValue, BytesPerGigabyte);
                default:
                    return null;
            }
        }

        private static bool UsesConfiguredHostUnits(SmartAttributeProfile profile)
        {
            switch (profile)
            {
                case SmartAttributeProfile.SandForce:
                case SmartAttributeProfile.OczVector:
                case SmartAttributeProfile.Corsair:
                case SmartAttributeProfile.Kingston:
                case SmartAttributeProfile.KingstonSuv:
                case SmartAttributeProfile.KingstonKC600:
                case SmartAttributeProfile.KingstonDC500:
                case SmartAttributeProfile.KingstonSA400:
                case SmartAttributeProfile.Realtek:
                case SmartAttributeProfile.WDC:
                case SmartAttributeProfile.Ssstc:
                case SmartAttributeProfile.SKhynix:
                case SmartAttributeProfile.Phison:
                case SmartAttributeProfile.Seagate:
                case SmartAttributeProfile.SeagateIronWolf:
                case SmartAttributeProfile.SeagateBarraCuda:
                case SmartAttributeProfile.Marvell:
                case SmartAttributeProfile.Maxiotek:
                case SmartAttributeProfile.Ymtc:
                case SmartAttributeProfile.Scy:
                case SmartAttributeProfile.Recadata:
                case SmartAttributeProfile.MicronMU03:
                case SmartAttributeProfile.AdataIndustrial:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSanDiskBaseProfile(SmartAttributeProfile profile)
        {
            return profile == SmartAttributeProfile.SanDisk
                || profile == SmartAttributeProfile.SanDiskGb
                || profile == SmartAttributeProfile.SanDiskHP
                || profile == SmartAttributeProfile.SanDiskHPVenus
                || profile == SmartAttributeProfile.SanDiskDell
                || profile == SmartAttributeProfile.SanDiskLenovo
                || profile == SmartAttributeProfile.SanDiskLenovoHelenVenus
                || profile == SmartAttributeProfile.SanDiskCloud;
        }

        private static bool IsSanDiskGbStyleProfile(SmartAttributeProfile profile)
        {
            return profile == SmartAttributeProfile.SanDiskGb
                || profile == SmartAttributeProfile.SanDiskHP
                || profile == SmartAttributeProfile.SanDiskHPVenus
                || profile == SmartAttributeProfile.SanDiskDell
                || profile == SmartAttributeProfile.SanDiskLenovo
                || profile == SmartAttributeProfile.SanDiskLenovoHelenVenus
                || profile == SmartAttributeProfile.SanDiskCloud;
        }

        private static ulong? ConvertNvmeDataUnitsToBytes(ulong rawValue)
        {
            return ConvertUnitsToBytes(rawValue, 1000UL * 512UL);
        }

        private static ulong? Convert512ByteUnitsToBytes(ulong rawValue)
        {
            return ConvertUnitsToBytes(rawValue, 512UL);
        }

        private static ulong? Convert32MiBUnitsToBytes(ulong rawValue)
        {
            return ConvertUnitsToBytes(rawValue, 32UL * BytesPerMiB);
        }

        private static ulong? ConvertUnitsToBytes(ulong rawValue, ulong bytesPerUnit)
        {
            if (rawValue > ulong.MaxValue / bytesPerUnit)
            {
                return null;
            }

            return rawValue * bytesPerUnit;
        }

        private static int? KelvinToCelsius(ushort kelvin)
        {
            if (kelvin == 0)
            {
                return null;
            }

            int celsius = kelvin - 273;
            if (celsius <= -273 || celsius > 200)
            {
                return null;
            }

            return celsius;
        }

        private static int? ClampPercentage(int value)
        {
            if (value < 0 || value > 100)
            {
                return null;
            }

            return value;
        }

        #endregion
    }
}
