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
    internal static class SmartAttributeSummarySettingsResolver
    {
        #region Public

        public static SmartAttributeSummarySettings Resolve(StorageDevice device)
        {
            var settings = new SmartAttributeSummarySettings();
            if (device == null)
            {
                return settings;
            }

            settings.Profile = SmartAttributeProfileResolver.Resolve(device);

            var model = GetPrimaryModel(device);
            var firmware = StringUtil.TrimStorageString(device.ProductRevision);

            switch (settings.Profile)
            {
                case SmartAttributeProfile.AdataIndustrial:
                case SmartAttributeProfile.Samsung:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
                    break;
                case SmartAttributeProfile.SanDisk:
                case SmartAttributeProfile.SanDiskGb:
                case SmartAttributeProfile.SanDiskHP:
                case SmartAttributeProfile.SanDiskHPVenus:
                case SmartAttributeProfile.SanDiskDell:
                case SmartAttributeProfile.SanDiskLenovo:
                case SmartAttributeProfile.SanDiskLenovoHelenVenus:
                case SmartAttributeProfile.SanDiskCloud:
                    ApplySanDiskSettings(model, settings);
                    break;
                case SmartAttributeProfile.WDC:
                    settings.HostReadWriteUnit = Contains(model, "SA530")
                        ? SmartHostReadWriteUnit.Unit16MiB
                        : SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.Seagate:
                case SmartAttributeProfile.SeagateBarraCuda:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                    settings.LifeFlags |= SmartLifeInterpretationFlags.RawValue;
                    break;
                case SmartAttributeProfile.SeagateIronWolf:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.Toshiba:
                    settings.HostReadWriteUnit = IsToshiba32MiBModel(model)
                        ? SmartHostReadWriteUnit.Unit32MiB
                        : SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.Kioxia:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit32MiB;
                    break;
                case SmartAttributeProfile.Intel:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit32MiB;
                    break;
                case SmartAttributeProfile.SandForce:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.JMicron60x:
                case SmartAttributeProfile.JMicron61x:
                case SmartAttributeProfile.JMicron66x:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
                    break;
                case SmartAttributeProfile.MicronMU03:
                    settings.HostReadWriteUnit = IsMicronLegacy512BModel(model)
                        ? SmartHostReadWriteUnit.Unit512Bytes
                        : SmartHostReadWriteUnit.Unit32MiB;
                    break;
                case SmartAttributeProfile.Kingston:
                case SmartAttributeProfile.KingstonSuv:
                case SmartAttributeProfile.KingstonDC500:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.KingstonKC600:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit32MiB;
                    break;
                case SmartAttributeProfile.KingstonSA400:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                    settings.LifeFlags |= SmartLifeInterpretationFlags.RawValue;
                    break;
                case SmartAttributeProfile.Corsair:
                    settings.HostReadWriteUnit = Contains(model, "Voyager GTX")
                        ? SmartHostReadWriteUnit.Unit1MiB
                        : SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.Realtek:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.SKhynix:
                    ApplySkHynixSettings(model, settings);
                    break;
                case SmartAttributeProfile.SiliconMotionCVC:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.SiliconMotion:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit32MiB;
                    if (!StartsWith(model, "SSD") || !StartsWith(firmware, "FW"))
                    {
                        if (!StartsWith(model, "WT200")
                            && !StartsWith(model, "WT100")
                            && !StartsWith(model, "WT ")
                            && !StartsWith(model, "tecmiyo")
                            && !StartsWith(model, "ADATA SU650")
                            && !StartsWith(model, "XD0R3C0A"))
                        {
                            settings.LifeFlags |= SmartLifeInterpretationFlags.RawValue;
                        }
                    }
                    break;
                case SmartAttributeProfile.Phison:
                    settings.HostReadWriteUnit = StartsWith(firmware, "S9")
                        ? SmartHostReadWriteUnit.Unit1MiB
                        : SmartHostReadWriteUnit.UnitGigabytes;
                    settings.LifeFlags |= SmartLifeInterpretationFlags.RawValue;
                    break;
                case SmartAttributeProfile.Marvell:
                    settings.HostReadWriteUnit = (StartsWith(model, "LEXAR") && (StartsWith(firmware, "SN0") || StartsWith(firmware, "V6")))
                        ? SmartHostReadWriteUnit.Unit32MiB
                        : SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.Maxiotek:
                    settings.HostReadWriteUnit = StartsWith(model, "MAXIO")
                        ? SmartHostReadWriteUnit.UnitGigabytes
                        : SmartHostReadWriteUnit.Unknown;
                    break;
                case SmartAttributeProfile.Apacer:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
                    settings.LifeFlags |= SmartLifeInterpretationFlags.RawValue;
                    break;
                case SmartAttributeProfile.Ymtc:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
                    break;
                case SmartAttributeProfile.Scy:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit32MiB;
                    break;
                case SmartAttributeProfile.Recadata:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                    break;
                case SmartAttributeProfile.SSD:
                    ApplyGenericSsdSettings(model, settings);
                    break;
                case SmartAttributeProfile.Ssstc:
                    settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                    break;
            }

            return settings;
        }

        #endregion

        #region Private

        private static void ApplySanDiskSettings(string model, SmartAttributeSummarySettings settings)
        {
            if ((Contains(model, "X600") && Contains(model, "2280"))
                || Contains(model, "X400")
                || Contains(model, "X300")
                || Contains(model, "X110")
                || Contains(model, "SD5"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
                settings.LifeFlags |= SmartLifeInterpretationFlags.SanDiskByte1Remaining;
            }
            else if (Contains(model, "Z400"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
            }
            else if (Contains(model, "1006"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit16MiB;
            }
            else if (Contains(model, "G1001"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                settings.LifeFlags |= SmartLifeInterpretationFlags.SanDiskCurrentValue;

                if (Contains(model, "SD9SB"))
                {
                    settings.NandWriteUnit = SmartNandWriteUnit.Unit1MiB;
                }
            }
            else if (Contains(model, "G1012") || Contains(model, "Z400s 2.5"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
            }
            else if (Contains(model, "SSD P4")
                  || Contains(model, "SSD U100")
                  || Contains(model, "SSD U110")
                  || Contains(model, "SSD i100")
                  || Contains(model, "SSD i110")
                  || Contains(model, "pSSD"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
                settings.LifeFlags |= SmartLifeInterpretationFlags.SanDiskUsbMemory;
            }
            else if (Contains(model, "iSSD P4"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
            }
            else if (Contains(model, "SDSSDP") || Contains(model, "SDSSDRC"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
                settings.LifeFlags |= SmartLifeInterpretationFlags.SanDiskHundredthsInTwoBytes;
            }
            else if (Contains(model, "SDLF1CRR-")
                  || Contains(model, "SDLF1DAR-")
                  || Contains(model, "SDLF1CRM-")
                  || Contains(model, "SDLF1DAM-"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
            }
            else
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                settings.LifeFlags |= SmartLifeInterpretationFlags.SanDiskByte1Remaining;
            }
        }

        private static void ApplySkHynixSettings(string model, SmartAttributeSummarySettings settings)
        {
            if ((Contains(model, "HFS") && Contains(model, "TND"))
                || (Contains(model, "HFS") && Contains(model, "MND")))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                settings.LifeFlags |= SmartLifeInterpretationFlags.RawValueIncrement;
            }
            else if (Contains(model, "HFS") && Contains(model, "TNF"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
                settings.LifeFlags |= SmartLifeInterpretationFlags.RawValue;
            }
            else if (Contains(model, "SC311") || Contains(model, "SC401"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
                settings.LifeFlags |= SmartLifeInterpretationFlags.RawValue;
            }
            else
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
            }
        }

        private static void ApplyGenericSsdSettings(string model, SmartAttributeSummarySettings settings)
        {
            if (StartsWith(model, "ADATA SP580"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit512Bytes;
            }
            else if (StartsWith(model, "LITEON IT LMT"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.Unit32MiB;
            }
            else if (StartsWith(model, "LITEON S960"))
            {
                settings.HostReadWriteUnit = SmartHostReadWriteUnit.UnitGigabytes;
            }
        }

        private static bool IsMicronLegacy512BModel(string model)
        {
            return StartsWith(model, "MICRON_M600")
                || StartsWith(model, "MICRON_M550")
                || StartsWith(model, "MICRON_M510")
                || StartsWith(model, "MICRON_M500")
                || StartsWith(model, "MICRON_1300")
                || StartsWith(model, "MICRON_1100")
                || StartsWith(model, "MICRON M600")
                || StartsWith(model, "MICRON M550")
                || StartsWith(model, "MICRON M510")
                || StartsWith(model, "MICRON M500")
                || StartsWith(model, "MICRON 1300")
                || StartsWith(model, "MICRON 1100")
                || StartsWith(model, "MTFDDA");
        }

        private static bool IsToshiba32MiBModel(string model)
        {
            return Contains(model, "THNSNC")
                || Contains(model, "THNSNJ")
                || Contains(model, "THNSNK")
                || Contains(model, "KSG60")
                || Contains(model, "TL100")
                || Contains(model, "TR150")
                || Contains(model, "TR200");
        }

        private static string GetPrimaryModel(StorageDevice device)
        {
            string productName = StringUtil.TrimStorageString(device?.ProductName);
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

        private static bool Contains(string value, string fragment)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(fragment)
                && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion
    }
}
