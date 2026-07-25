/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Utilities;

namespace DiskInfoToolkit.Smart
{
    public static class SmartAttributeProfileResolver
    {
        #region Public

        public static SmartAttributeProfile Resolve(StorageDevice device)
        {
            if (device == null)
            {
                return SmartAttributeProfile.Unknown;
            }

            if (device.SmartAttributeProfile != SmartAttributeProfile.Unknown)
            {
                return device.SmartAttributeProfile;
            }

            if ((device.Nvme.SmartLogData != null && device.Nvme.SmartLogData.Length > 0)
                || device.TransportKind == StorageTransportKind.Nvme
                || device.BusType == StorageBusType.Nvme)
            {
                return SmartAttributeProfile.NVMe;
            }

            var model    = GetPrimaryModel(device);
            var firmware = StringUtil.TrimStorageString(device.ProductRevision);
            var combined = BuildCombinedText(device);

            var smartIds = GetSmartAttributeIds(device);

            if (IsAdataIndustrial(model, combined))
            {
                return SmartAttributeProfile.AdataIndustrial;
            }

            var profile = ResolveSanDisk(model, combined, smartIds);
            if (profile != SmartAttributeProfile.Unknown)
            {
                return profile;
            }

            if (IsWdc(model, combined))
            {
                return SmartAttributeProfile.WDC;
            }

            profile = ResolveSeagate(model, combined, smartIds);
            if (profile != SmartAttributeProfile.Unknown)
            {
                return profile;
            }

            if (IsMtron(model, smartIds))
            {
                return SmartAttributeProfile.Mtron;
            }

            if (IsToshiba(model, combined))
            {
                return SmartAttributeProfile.Toshiba;
            }

            if (IsJMicron66x(model, smartIds))
            {
                return SmartAttributeProfile.JMicron66x;
            }

            if (IsJMicron61x(smartIds))
            {
                return SmartAttributeProfile.JMicron61x;
            }

            if (IsJMicron60x(smartIds))
            {
                return SmartAttributeProfile.JMicron60x;
            }

            if (IsIndilinx(smartIds))
            {
                return SmartAttributeProfile.Indilinx;
            }

            if (IsIntelDc(model, combined))
            {
                return SmartAttributeProfile.IntelDc;
            }

            if (IsIntel(model, combined, smartIds))
            {
                return SmartAttributeProfile.Intel;
            }

            if (IsSamsung(model, combined, smartIds))
            {
                return SmartAttributeProfile.Samsung;
            }

            if (IsMicronMU03(model, combined, firmware))
            {
                return SmartAttributeProfile.MicronMU03;
            }

            if (IsMicronMU02(model, combined))
            {
                return SmartAttributeProfile.MicronMU02;
            }

            if (IsMicron(model, combined, firmware, smartIds))
            {
                return SmartAttributeProfile.Micron;
            }

            if (IsSandForce(model, combined, smartIds))
            {
                return SmartAttributeProfile.SandForce;
            }

            if (IsOcz(model, combined, smartIds))
            {
                return SmartAttributeProfile.Ocz;
            }

            if (IsOczVector(model, combined, smartIds))
            {
                return SmartAttributeProfile.OczVector;
            }

            if (IsSsstc(model, combined))
            {
                return SmartAttributeProfile.Ssstc;
            }

            if (IsPlextor(model, combined, smartIds))
            {
                return SmartAttributeProfile.Plextor;
            }

            profile = ResolveKingston(model, combined);
            if (profile != SmartAttributeProfile.Unknown)
            {
                return profile;
            }

            if (IsCorsair(model, combined))
            {
                return SmartAttributeProfile.Corsair;
            }

            if (IsRealtek(smartIds))
            {
                return SmartAttributeProfile.Realtek;
            }

            if (IsSkHynix(model, combined))
            {
                return SmartAttributeProfile.SKhynix;
            }

            if (IsKioxia(model, combined))
            {
                return SmartAttributeProfile.Kioxia;
            }

            if (IsSiliconMotionCvc(model, combined))
            {
                return SmartAttributeProfile.SiliconMotionCVC;
            }

            if (IsSiliconMotion(model, combined, smartIds))
            {
                return SmartAttributeProfile.SiliconMotion;
            }

            if (IsPhison(smartIds))
            {
                return SmartAttributeProfile.Phison;
            }

            if (IsMarvell(model, smartIds))
            {
                return SmartAttributeProfile.Marvell;
            }

            if (IsMaxiotek(model, smartIds))
            {
                return SmartAttributeProfile.Maxiotek;
            }

            if (IsApacer(model, combined, firmware))
            {
                return SmartAttributeProfile.Apacer;
            }

            if (IsYmtc(model, combined))
            {
                return SmartAttributeProfile.Ymtc;
            }

            if (IsScy(model))
            {
                return SmartAttributeProfile.Scy;
            }

            if (IsRecadata(model, combined))
            {
                return SmartAttributeProfile.Recadata;
            }

            if (IsGeneralSsdModel(model, combined))
            {
                return SmartAttributeProfile.SSD;
            }

            return SmartAttributeProfile.Smart;
        }

        #endregion

        #region Private

        private static string BuildCombinedText(StorageDevice device)
        {
            return string.Join(" ",
                StringUtil.TrimStorageString(device.VendorName),
                StringUtil.TrimStorageString(device.ProductName),
                StringUtil.TrimStorageString(device.DisplayName),
                StringUtil.TrimStorageString(device.ProductRevision));
        }

        private static List<byte> GetSmartAttributeIds(StorageDevice device)
        {
            var result = new List<byte>();
            if (device == null || device.SmartAttributes == null)
            {
                return result;
            }

            foreach (var entry in device.SmartAttributes)
            {
                if (entry == null)
                {
                    continue;
                }

                result.Add(entry.ID);
            }

            return result;
        }

        private static string GetPrimaryModel(StorageDevice device)
        {
            var product = StringUtil.TrimStorageString(device.ProductName);
            if (!string.IsNullOrWhiteSpace(product))
            {
                return product;
            }

            var display = StringUtil.TrimStorageString(device.DisplayName);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return display;
            }

            return StringUtil.TrimStorageString(device.VendorName);
        }

        private static byte? GetIdAt(List<byte> smartIds, int index)
        {
            if (smartIds == null || index < 0 || index >= smartIds.Count)
            {
                return null;
            }

            return smartIds[index];
        }

        private static bool IsAdataIndustrial(string model, string combined)
        {
            return ModelStartsWith(model, "ADATA_IM2S")
                || ModelStartsWith(model, "ADATA_IMSS")
                || ModelStartsWith(model, "ADATA_ISSS")
                || ModelStartsWith(model, "IM2S")
                || ModelStartsWith(model, "IMSS")
                || ModelStartsWith(model, "ISSS")
                || TextContains(combined, "ADATA INDUSTRIAL");
        }

        private static bool IsApacer(string model, string combined, string firmware)
        {
            return ModelStartsWith(model, "Apacer")
                || ModelStartsWith(model, "ZADAK")
                || FirmwareStartsWith(firmware, "AP")
                || FirmwareStartsWith(firmware, "SF")
                || FirmwareStartsWith(firmware, "PN")
                || TextContains(combined, "APACER");
        }

        private static bool IsCorsair(string model, string combined)
        {
            return ModelStartsWith(model, "Corsair")
                || TextContains(combined, "CORSAIR");
        }

        private static bool IsGeneralSsdModel(string model, string combined)
        {
            return ModelStartsWith(model, "ADATA SP580")
                || ModelStartsWith(model, "OCZ")
                || ModelStartsWith(model, "SPCC")
                || ModelStartsWith(model, "PATRIOT")
                || ModelStartsWith(model, "PHOTOFAST")
                || ModelStartsWith(model, "STT_FTM")
                || ModelStartsWith(model, "Super Talent")
                || TextContains(combined, " SSD ")
                || TextContains(combined, "SSD")
                || TextContains(combined, "SOLID")
                || TextContains(combined, "SILICONHARDDISK");
        }

        private static bool IsIndilinx(List<byte> smartIds)
        {
            return MatchPrefix(smartIds, 0x01, 0x09, 0x0C, 0xB8, 0xC3, 0xC4);
        }

        private static bool IsIntel(string model, string combined, List<byte> smartIds)
        {
            if (MatchPrefix(smartIds, 0x03, 0x04, 0x05, 0x09, 0x0C))
            {
                var attr5 = GetIdAt(smartIds, 5);
                var attr6 = GetIdAt(smartIds, 6);
                var attr7 = GetIdAt(smartIds, 7);

                if ((attr5 == 0xC0 && attr6 == 0xE8 && attr7 == 0xE9)
                    || (attr5 == 0xC0 && attr6 == 0xE1)
                    || (attr5 == 0xAA && attr6 == 0xAB && attr7 == 0xAC))
                {
                    return true;
                }
            }

            return TextContains(combined, "INTEL")
                || TextContains(combined, "SOLIDIGM")
                || ModelStartsWith(model, "INTEL");
        }

        private static bool IsIntelDc(string model, string combined)
        {
            return ModelContains(model, "INTEL SSDSCKHB")
                || TextContains(combined, "INTEL SSD DC")
                || TextContains(combined, "INTEL DC");
        }

        private static bool IsJMicron60x(List<byte> smartIds)
        {
            return MatchPrefix(smartIds, 0x0C, 0x09, 0xC2, 0xE5, 0xE8, 0xE9);
        }

        private static bool IsJMicron61x(List<byte> smartIds)
        {
            return MatchPrefix(smartIds, 0x01, 0x02, 0x03, 0x05, 0x07, 0x08, 0x09, 0x0A, 0x0C, 0xA8, 0xAF, 0xC0, 0xC2);
        }

        private static bool IsJMicron66x(string model, List<byte> smartIds)
        {
            return MatchPrefix(smartIds, 0x01, 0x02, 0x03, 0x05, 0x07, 0x08, 0x09, 0x0A, 0x0C, 0xA7, 0xA8, 0xA9, 0xAA, 0xAD, 0xAF)
                || ModelStartsWith(model, "ADATA SU700");
        }

        private static bool IsKioxia(string model, string combined)
        {
            return TextContains(combined, "KIOXIA")
                || ModelContains(model, "KIOXIA");
        }

        private static bool IsMarvell(string model, List<byte> smartIds)
        {
            if (ModelStartsWith(model, "HANYE-Q55"))
            {
                return false;
            }

            return MatchPrefix(smartIds, 0x05, 0x09, 0x0C, 0xA1, 0xA4, 0xA5, 0xA6, 0xA7)
                || MatchPrefix(smartIds, 0x05, 0x09, 0x0C, 0xA4, 0xA5, 0xA6, 0xA7);
        }

        private static bool IsMaxiotek(string model, List<byte> smartIds)
        {
            return ModelStartsWith(model, "MAXIO")
                || (ModelStartsWith(model, "HANYE-Q55") && MatchPrefix(smartIds, 0x05, 0x09, 0x0C, 0xA4, 0xA5, 0xA6, 0xA7))
                || MatchPrefix(smartIds, 0x05, 0x09, 0x0C, 0xA7, 0xA8, 0xA9);
        }

        private static bool IsMicron(string model, string combined, string firmware, List<byte> smartIds)
        {
            if (MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xB5, 0xB7))
            {
                return true;
            }

            return ModelStartsWith(model, "P600")
                || ModelStartsWith(model, "C600")
                || ModelStartsWith(model, "M6-")
                || ModelStartsWith(model, "M600")
                || ModelStartsWith(model, "P500")
                //Workaround for Maxiotek C500
                || (ModelStartsWith(model, "C500") && FirmwareIndexOf(firmware, "H") != 0)
                || ModelStartsWith(model, "M5-")
                || ModelStartsWith(model, "M500")
                || ModelStartsWith(model, "P400")
                || ModelStartsWith(model, "C400")
                || ModelStartsWith(model, "M4-")
                || ModelStartsWith(model, "M400")
                || ModelStartsWith(model, "P300")
                || ModelStartsWith(model, "C300")
                || ModelStartsWith(model, "M3-")
                || ModelStartsWith(model, "M300")
                || (ModelStartsWith(model, "CT") && TextContains(combined, "SSD"))
                || ModelStartsWith(model, "CRUCIAL")
                || ModelStartsWith(model, "MICRON")
                || ModelStartsWith(model, "MTFD");
        }

        private static bool IsMicronMU02(string model, string combined)
        {
            return ModelContains(model, "MU02") || TextContains(combined, "MICRON MU02");
        }

        private static bool IsMicronMU03(string model, string combined, string firmware)
        {
            return ModelStartsWith(model, "MICRON_M600")
                || ModelStartsWith(model, "MICRON_M550")
                || ModelStartsWith(model, "MICRON_M510")
                || ModelStartsWith(model, "MICRON_M500")
                || ModelStartsWith(model, "MICRON_1300")
                || ModelStartsWith(model, "MICRON_1100")
                || ModelStartsWith(model, "MICRON M600")
                || ModelStartsWith(model, "MICRON M550")
                || ModelStartsWith(model, "MICRON M510")
                || ModelStartsWith(model, "MICRON M500")
                || ModelStartsWith(model, "MICRON 1300")
                || ModelStartsWith(model, "MICRON 1100")
                || ModelStartsWith(model, "MTFDDA")
                || ModelContains(model, "M500SSD")
                || ModelContains(model, "MX500SSD")
                || ModelContains(model, "MX300SSD")
                || ModelContains(model, "MX200SSD")
                || ModelContains(model, "MX100SSD")
                || ModelContains(model, "BX500SSD")
                || ModelContains(model, "BX300SSD")
                || ModelContains(model, "BX200SSD")
                || ModelContains(model, "BX100SSD")
                || (ModelStartsWith(model, "MTFD") && !FirmwareContains(firmware, "MU01"))
                || TextContains(combined, "MICRON M600")
                || TextContains(combined, "MICRON M550");
        }

        private static bool IsMtron(string model, List<byte> smartIds)
        {
            return (smartIds.Count == 1 && GetIdAt(smartIds, 0) == 0xBB)
                || ModelStartsWith(model, "MTRON");
        }

        private static bool IsOcz(string model, string combined, List<byte> smartIds)
        {
            //OCZ-TRION100
            return ModelStartsWith(model, "OCZ-TRION")
                //OCZ-PETROL
                //OCZ-OCTANE S2
                //OCZ-VERTEX 4
                || MatchPrefix(smartIds, 0x01, 0x03, 0x04, 0x05, 0x09, 0x0C, 0xE8, 0xE9)
                || ModelStartsWith(model, "OCZ")
                || TextContains(combined, "OCZ");
        }

        private static bool IsOczVector(string model, string combined, List<byte> smartIds)
        {
            return ModelStartsWith(model, "RADEON R7")
                //PANASONIC RP-SSB240GAK
                || MatchPrefix(smartIds, 0x05, 0x09, 0x0C, 0xAB, 0xAE, 0xC3, 0xC4, 0xC5, 0xC6)
                || ModelStartsWith(model, "PANASONIC RP-SSB")
                || TextContains(combined, "OCZ VECTOR")
                || TextContains(combined, "VECTOR");
        }

        private static bool IsPhison(List<byte> smartIds)
        {
            //0xC2 = with Temperature Sensor
            return MatchPrefix(smartIds, 0x01, 0x09, 0x0C, 0xA8, 0xAA, 0xAD, 0xC0, 0xC2, 0xDA, 0xE7, 0xF1)
                || MatchPrefix(smartIds, 0x01, 0x09, 0x0C, 0xA8, 0xAA, 0xAD, 0xC0, 0xDA, 0xE7, 0xF1);
        }

        private static bool IsPlextor(string model, string combined, List<byte> smartIds)
        {
            return MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0xB1, 0xB2, 0xB5, 0xB6)
                //CFD's SSD
                //LITEON CV6-CQ
                || ModelStartsWith(model, "PLEXTOR")
                || ModelStartsWith(model, "LITEON")
                || ModelStartsWith(model, "CV6-CQ")
                || ModelStartsWith(model, "CSSD-S6T128NM3PQ")
                || ModelStartsWith(model, "CSSD-S6T256NM3PQ")
                || TextContains(combined, "PLEXTOR");
        }

        private static bool IsRealtek(List<byte> smartIds)
        {
            return MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0xA1, 0xA2, 0xA3, 0xA4, 0xA6, 0xA7);
        }

        private static bool IsRecadata(string model, string combined)
        {
            return ModelStartsWith(model, "RECADATA") || TextContains(combined, "RECADATA");
        }

        private static SmartAttributeProfile ResolveKingston(string model, string combined)
        {
            if (!TextContains(combined, "KINGSTON") && !ModelContains(model, "KINGSTON"))
            {
                return SmartAttributeProfile.Unknown;
            }

            if (ModelContains(model, "KC600"))
            {
                return SmartAttributeProfile.KingstonKC600;
            }

            if (ModelContains(model, "DC500"))
            {
                return SmartAttributeProfile.KingstonDC500;
            }

            if (ModelContains(model, "SA400"))
            {
                return SmartAttributeProfile.KingstonSA400;
            }

            if (ModelContains(model, "SUV400") || ModelContains(model, "SUV500"))
            {
                return SmartAttributeProfile.KingstonSuv;
            }

            if (ModelContains(model, "SM2280")
                || ModelContains(model, "SEDC400")
                || ModelContains(model, "SKC310")
                || ModelContains(model, "SHSS")
                || ModelContains(model, "SUV300")
                || ModelContains(model, "SKC400"))
            {
                return SmartAttributeProfile.Kingston;
            }

            return SmartAttributeProfile.Kingston;
        }

        private static SmartAttributeProfile ResolveSanDisk(string model, string combined, List<byte> smartIds)
        {
            if (!ModelContains(model, "SanDisk")
                && !ModelContains(model, "SD Ultra")
                && !ModelContains(model, "SDLF1")
                && !TextContains(combined, "SANDISK")
                && !TextContains(combined, "SDLF1"))
            {
                return SmartAttributeProfile.Unknown;
            }

            if ((ModelContains(model, "X600") && ModelContains(model, "2280"))
                || ModelContains(model, "X400")
                || ModelContains(model, "X300")
                || ModelContains(model, "X110")
                || ModelContains(model, "SD5"))
            {
                return GetIdAt(smartIds, 2) == 0xAF || GetIdAt(smartIds, 3) == 0xAF
                    ? SmartAttributeProfile.SanDiskDell
                    : SmartAttributeProfile.SanDiskGb;
            }

            if (ModelContains(model, "Z400"))
            {
                return SmartAttributeProfile.SanDiskDell;
            }

            if (ModelContains(model, "1006")) //HP OEM
            {
                return ModelContains(model, "8U")
                    ? SmartAttributeProfile.SanDiskHPVenus
                    : SmartAttributeProfile.SanDiskHP;
            }

            if (ModelContains(model, "G1001")) //Lenovo OEM
            {
                if (ModelContains(model, "6S") || ModelContains(model, "7S") || ModelContains(model, "8U"))
                {
                    return SmartAttributeProfile.SanDiskLenovoHelenVenus;
                }

                if (ModelContains(model, "SD9SB"))
                {
                    return SmartAttributeProfile.SanDiskGb;
                }

                return SmartAttributeProfile.SanDiskLenovo;
            }

            if (ModelContains(model, "G1012")
             || ModelContains(model, "Z400s 2.5")) //Dell OEM
            {
                return SmartAttributeProfile.SanDiskDell;
            }

            //CloudSpeed ECO Gen II Eco SSD
            if (ModelContains(model, "SDLF1CRR-")
                || ModelContains(model, "SDLF1DAR-")
                //CloudSpeed ECO Gen II Ultra SSD
                || ModelContains(model, "SDLF1CRM-")
                || ModelContains(model, "SDLF1DAM-"))
            {
                return SmartAttributeProfile.SanDiskCloud;
            }

            if (ModelContains(model, "SSD P4")
                || ModelContains(model, "iSSD P4")
                || ModelContains(model, "SDSSDP")
                || ModelContains(model, "SDSSDRC")
                || ModelContains(model, "SSD U100")
                || ModelContains(model, "SSD U110")
                || ModelContains(model, "SSD i100")
                || ModelContains(model, "SSD i110")
                || ModelContains(model, "pSSD"))
            {
                return SmartAttributeProfile.SanDisk;
            }

            return SmartAttributeProfile.SanDiskGb;
        }

        private static SmartAttributeProfile ResolveSeagate(string model, string combined, List<byte> smartIds)
        {
            if (MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0x64, 0x66, 0x67, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xB1, 0xB7, 0xBB))
            {
                return SmartAttributeProfile.SeagateIronWolf;
            }

            if (MatchPrefix(smartIds, 0x01, 0x09, 0x0C, 0x10, 0x11, 0xA8, 0xAA, 0xAD, 0xAE, 0xB1, 0xC0, 0xC2, 0xDA, 0xE7, 0xE8, 0xE9, 0xEB, 0xF1, 0xF2))
            {
                return SmartAttributeProfile.Seagate;
            }

            if (ModelContains(model, "BarraCuda") || TextContains(combined, "BARRACUDA"))
            {
                return SmartAttributeProfile.SeagateBarraCuda;
            }

            if (ModelContains(model, "IronWolf") || TextContains(combined, "IRONWOLF"))
            {
                return SmartAttributeProfile.SeagateIronWolf;
            }

            if (LooksLikeSsdModel(model, combined)
                && (ModelStartsWith(model, "Seagate")
                    || (ModelIndexOf(model, "STT") != 0 && ModelStartsWith(model, "ST"))
                    || ModelContains(model, "ZA")
                    || TextContains(combined, "SEAGATE")))
            {
                return SmartAttributeProfile.Seagate;
            }

            return SmartAttributeProfile.Unknown;
        }

        private static bool IsSandForce(string model, string combined, List<byte> smartIds)
        {
            return MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0x0D, 0x64, 0xAA)
                || MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0xAB, 0xAC)
                //TOSHIBA + SandForce
                || MatchPrefix(smartIds, 0x01, 0x02, 0x03, 0x05, 0x07, 0x08, 0x09, 0x0A, 0x0C, 0xA7, 0xA8, 0xA9, 0xAA, 0xAD, 0xAF, 0xB1)
                || TextContains(combined, "SANDFORCE")
                || ModelContains(model, "SandForce");
        }

        private static bool IsSamsung(string model, string combined, List<byte> smartIds)
        {
            return MatchPrefix(smartIds, 0x05, 0x09, 0x0C, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xB2, 0xB4)
                || MatchPrefix(smartIds, 0x09, 0x0C, 0xB2, 0xB3, 0xB4)
                || MatchPrefix(smartIds, 0x09, 0x0C, 0xB1, 0xB2, 0xB3, 0xB4, 0xB7)
                || MatchPrefix(smartIds, 0x09, 0x0C, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3, 0xB4)
                || MatchPrefix(smartIds, 0x05, 0x09, 0x0C, 0xB1, 0xB3, 0xB5, 0xB6)
                || ModelContains(model, "MZ-")
                || (LooksLikeSsdModel(model, combined) && TextContains(combined, "SAMSUNG"));
        }

        private static bool IsScy(string model)
        {
            return ModelStartsWith(model, "SCY");
        }

        private static bool IsSiliconMotion(string model, string combined, List<byte> smartIds)
        {
            return MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0xA0, 0xA1, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAF, 0xB0, 0xB1, 0xB2, 0xB5, 0xB6, 0xC0)
                //ADATA SX950
                || MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0xA0, 0xA1, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0x94, 0x95, 0x96, 0x97, 0xA9, 0xB1, 0xB5, 0xB6, 0xBB)
                || MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0x94, 0x95, 0x96, 0x97, 0x9F, 0xA0, 0xA1)
                || MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0xA0, 0xA1, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7)
                || MatchPrefix(smartIds, 0x01, 0x05, 0x09, 0x0C, 0xA0, 0xA1, 0xA3, 0x94, 0x95, 0x96, 0x97)
                || ModelStartsWith(model, "ADATA SX950")
                || (LooksLikeSsdModel(model, combined) && TextContains(combined, "SILICON MOTION"));
        }

        private static bool IsSiliconMotionCvc(string model, string combined)
        {
            return ModelContains(model, "CVC-") || TextContains(combined, "CVC-");
        }

        private static bool IsSkHynix(string model, string combined)
        {
            return TextContains(combined, "SK HYNIX")
                || TextContains(combined, "SKHYNIX")
                || ModelStartsWith(model, "HFS")
                || ModelStartsWith(model, "SHG")
                || (LooksLikeSsdModel(model, combined) && TextContains(combined, "HYNIX"));
        }

        private static bool IsSsstc(string model, string combined)
        {
            return ModelContains(model, "CV8-")
                || ModelContains(model, "CVB-")
                || ModelContains(model, "ER2-")
                || TextContains(combined, "SSSTC");
        }

        private static bool IsToshiba(string model, string combined)
        {
            return ModelContains(model, "THNSNC")
                || ModelContains(model, "THNSNJ")
                || ModelContains(model, "THNSNK")
                || ModelContains(model, "KSG60")
                || ModelContains(model, "TL100")
                || ModelContains(model, "TR150")
                || ModelContains(model, "TR200")
                || (LooksLikeSsdModel(model, combined) && TextContains(combined, "TOSHIBA"));
        }

        private static bool IsWdc(string model, string combined)
        {
            if (ModelStartsWith(model, "WDS")
             || ModelStartsWith(model, "WDC ")
             || ModelStartsWith(model, "WD ")
             || ModelContains(model, "SA530")
             || ModelContains(model, "WD BLUE")
             || ModelContains(model, "WD GREEN")
             || ModelContains(model, "WD RED")
             || TextContains(combined, "WD SSD"))
            {
                return true;
            }

            return LooksLikeSsdModel(model, combined)
                && TextContains(combined, "WESTERN DIGITAL");
        }

        private static bool IsYmtc(string model, string combined)
        {
            return ModelContains(model, "ZHITAI") || TextContains(combined, "YMTC");
        }

        private static bool LooksLikeSsdModel(string model, string combined)
        {
            return IsGeneralSsdModel(model, combined)
                || ModelContains(model, "MZ-")
                || ModelContains(model, "NVME")
                || ModelContains(model, "SATA SSD")
                || ModelContains(model, "SOLID STATE")
                || ModelContains(model, "TRION")
                || ModelContains(model, "VECTOR")
                || ModelContains(model, "KC600")
                || ModelContains(model, "DC500")
                || ModelContains(model, "SA400")
                || ModelContains(model, "SUV400")
                || ModelContains(model, "SUV500")
                || ModelContains(model, "BX500")
                || ModelContains(model, "MX500")
                || ModelContains(model, "TL100")
                || ModelContains(model, "TR150")
                || ModelContains(model, "TR200")
                || ModelContains(model, "SC311")
                || ModelContains(model, "SC401")
                || TextContains(combined, "SSD");
        }

        private static bool MatchPrefix(List<byte> smartIds, params byte[] prefix)
        {
            if (smartIds == null || prefix == null || smartIds.Count < prefix.Length)
            {
                return false;
            }

            for (int i = 0; i < prefix.Length; ++i)
            {
                if (smartIds[i] != prefix[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ModelContains(string model, string value)
        {
            return ContainsOrdinalIgnoreCase(model, value);
        }

        private static int ModelIndexOf(string model, string value)
        {
            if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(value))
            {
                return -1;
            }

            return model.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ModelStartsWith(string model, string value)
        {
            if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(value))
            {
                return false;
            }

            return model.StartsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool FirmwareContains(string firmware, string value)
        {
            return ContainsOrdinalIgnoreCase(firmware, value);
        }

        private static int FirmwareIndexOf(string firmware, string value)
        {
            if (string.IsNullOrEmpty(firmware) || string.IsNullOrEmpty(value))
            {
                return -1;
            }

            return firmware.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool FirmwareStartsWith(string firmware, string value)
        {
            if (string.IsNullOrEmpty(firmware) || string.IsNullOrEmpty(value))
            {
                return false;
            }

            return firmware.StartsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TextContains(string text, string value)
        {
            return ContainsOrdinalIgnoreCase(text, value);
        }

        private static bool ContainsOrdinalIgnoreCase(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            {
                return false;
            }

            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion
    }
}
