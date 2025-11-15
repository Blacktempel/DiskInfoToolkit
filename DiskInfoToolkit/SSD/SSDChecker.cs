/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Florian K.
 *
 * Code inspiration, improvements and fixes are from, but not limited to, following projects:
 * CrystalDiskInfo
 */

using BlackSharp.Core.Extensions;
using DiskInfoToolkit.Enums.Interop;
using DiskInfoToolkit.Interop.Enums;
using DiskInfoToolkit.Interop.Structures;
using DiskInfoToolkit.Smart;

namespace DiskInfoToolkit.SSD
{
    internal static class SSDChecker
    {
        #region Public

        public static void CheckSSDSupport(Storage storage, ref ATA_IDENTIFY_DEVICE ataIdentify, byte[] smartReadBuffer, List<SmartAttributeStructure> smartAttributes)
        {
            if (IsSSDOld(storage))
            {
                storage.IsSSD = true;
            }

            if (!storage.IsSSD) //HDD
            {
                storage.SmartKey = SmartKey.Smart;
                storage.ATAInfo.VendorID = VendorIDs.HDDGeneral;
            }
            else if (IsSSDAdataIndustrial(storage))
            {
                storage.SmartKey = SmartKey.AdataIndustrial;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorAdataIndustrial;
            }
            else if (IsSSDSanDisk(storage, smartAttributes))
            {
                //Empty
            }
            else if (IsSSDWDC(storage))
            {
                storage.SmartKey = SmartKey.WDC;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorWdc;
            }
            else if (IsSSDSeagate(storage, smartAttributes))
            {
                //Empty
            }
            else if (IsSSDMtron(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Mtron;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorMtron;
            }
            else if (IsSSDToshiba(storage))
            {
                storage.SmartKey = SmartKey.Toshiba;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorToshiba;
            }
            else if (IsSSDJMicron66x(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.JMicron66x;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorJMicron;
            }
            else if (IsSSDJMicron61x(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.JMicron61x;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorJMicron;
            }
            else if (IsSSDJMicron60x(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.JMicron60x;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorJMicron;
            }
            else if (IsSSDIndilinx(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Indilinx;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorIndilinx;
            }
            else if (IsSSDIntelDc(storage))
            {
                storage.SmartKey = SmartKey.IntelDc;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorIntelDC;
            }
            else if (IsSSDIntel(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Intel;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorIntel;
            }
            else if (IsSSDSamsung(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Samsung;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSamsung;
            }
            else if (IsSSDMicronMU03(storage))
            {
                storage.SmartKey = SmartKey.MicronMU03;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorMicronMU03;
            }
            else if (IsSSDMicron(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Micron;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorMicron;
            }
            else if (IsSSDSandForce(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.SandForce;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSandforce;
            }
            else if (IsSSDOcz(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Ocz;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorOcz;
            }
            else if (IsSSDOczVector(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.OczVector;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorOczVector;
            }
            else if (IsSSDSsstc(storage))
            {
                storage.SmartKey = SmartKey.Ssstc;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSSSTC;
            }
            else if (IsSSDPlextor(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Plextor;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorPlextor;
            }
            else if (IsSSDKingston(storage))
            {
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorKingston;
            }
            else if (IsSSDCorsair(storage))
            {
                storage.SmartKey = SmartKey.Corsair;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorCorsair;
            }
            else if (IsSSDRealtek(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Realtek;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorRealtek;
            }
            else if (IsSSDSKHynix(storage))
            {
                storage.SmartKey = SmartKey.SKhynix;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSkhynix;
            }
            else if (IsSSDKioxia(storage))
            {
                storage.SmartKey = SmartKey.Kioxia;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorKioxia;
            }
            else if (IsSSDSiliconMotionCVC(storage))
            {
                storage.SmartKey = SmartKey.SiliconMotionCVC;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSiliconMotionCVC;
            }
            else if (IsSSDSiliconMotion(storage, smartAttributes, smartReadBuffer))
            {
                storage.SmartKey = SmartKey.SiliconMotion;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSiliconMotion;
            }
            else if (IsSSDPhison(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Phison;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorPhison;
            }
            else if (IsSSDMarvell(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Marvell;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorMarvell;
            }
            else if (IsSSDMaxiotek(storage, smartAttributes))
            {
                storage.SmartKey = SmartKey.Maxiotek;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorMaxiotek;
            }
            else if (IsSSDApacer(storage))
            {
                storage.SmartKey = SmartKey.Apacer;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorAPACER;
            }
            else if (IsSSDYmtc(storage))
            {
                storage.SmartKey = SmartKey.Ymtc;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorYMTC;
            }
            else if (IsSSDScy(storage))
            {
                storage.SmartKey = SmartKey.Scy;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSCY;
            }
            else if (IsSSDRecadata(storage))
            {
                storage.SmartKey = SmartKey.Recadata;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorRecadata;
            }
            else if (IsSSDGeneral(storage))
            {
                storage.SmartKey = SmartKey.SSD;
                storage.ATAInfo.VendorID = VendorIDs.SSDGeneral;
            }
            else
            {
                storage.SmartKey = SmartKey.Smart;
                storage.ATAInfo.VendorID = VendorIDs.HDDGeneral;
            }

            var smart = storage.Smart;

            //Update life
            foreach (var attribute in smartAttributes)
            {
                SmartAttributeHandler.HandleAttribute(storage, smart, attribute);
            }
        }

        public static bool IsSSDOld(Storage storage)
        {
            return ModelStartsWith(storage, "OCZ"            )
                || ModelStartsWith(storage, "SPCC"           )
                || ModelStartsWith(storage, "PATRIOT"        )
                || ModelContains  (storage, "Solid"          )
                || ModelContains  (storage, "SSD"            )
                || ModelContains  (storage, "SiliconHardDisk")
                || ModelStartsWith(storage, "PHOTOFAST"      )
                || ModelStartsWith(storage, "STT_FTM"        )
                || ModelStartsWith(storage, "Super Talent"   )
                ;
        }

        public static bool IsSSDAdataIndustrial(Storage storage)
        {
            bool flagSmartType = false;

            if ( ModelStartsWith(storage, "ADATA_IM2S")
              || ModelStartsWith(storage, "ADATA_IMSS")
              || ModelStartsWith(storage, "ADATA_ISSS")
              || ModelStartsWith(storage, "IM2S"      )
              || ModelStartsWith(storage, "IMSS"      )
              || ModelStartsWith(storage, "ISSS"      )
                )
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;
            }

            return flagSmartType;
        }

        public static bool IsSSDSanDisk(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (ModelContains(storage, "SanDisk" )
             || ModelContains(storage, "SD Ultra")
             || ModelContains(storage, "SDLF1"   ))
            {
                //Default Vendor ID for SanDisk
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSandisk;

                flagSmartType = true;

                if ( (
                        ModelContains(storage, "X600")
                     && ModelContains(storage, "2280")
                     )
                 || ModelContains(storage, "X400")
                 || ModelContains(storage, "X300")
                 || ModelContains(storage, "X110")
                 || ModelContains(storage, "SD5" )
                   )
                {
                    if (GetAt(smartAttributes, 2)?.ID == 0xAF || GetAt(smartAttributes, 3)?.ID == 0xAF)
                    {
                        storage.SmartKey = SmartKey.SanDiskDell;
                    }
                    else
                    {
                        storage.SmartKey = SmartKey.SanDiskGb;
                    }

                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;

                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeSanDisk1;
                }
                else if (ModelContains(storage, "Z400"))
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                    storage.SmartKey = SmartKey.SanDiskDell;
                }
                else if (ModelContains(storage, "1006")) //HP OEM
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites16MB;

                    if (ModelContains(storage, "8U"))
                    {
                        storage.ATAInfo.VendorID = VendorIDs.SSDVendorSandiskHPVenus;
                        storage.SmartKey = SmartKey.SanDiskHPVenus;
                    }
                    else
                    {
                        storage.ATAInfo.VendorID = VendorIDs.SSDVendorSandiskHP;
                        storage.SmartKey = SmartKey.SanDiskHP;
                    }
                }
                else if (ModelContains(storage, "G1001")) //Lenovo OEM
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;

                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeSanDiskLenovo;

                    if (ModelContains(storage, "6S")
                     || ModelContains(storage, "7S")
                     || ModelContains(storage, "8U"))
                    {
                        storage.ATAInfo.VendorID = VendorIDs.SSDVendorSandiskLenovoHelenVenus;
                        storage.SmartKey = SmartKey.SanDiskLenovoHelenVenus;
                    }
                    else if (ModelContains(storage, "SD9SB"))
                    {
                        storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                        storage.NandWritesUnit = NandWritesUnit.NandWrites1MB;
                        storage.SmartKey = SmartKey.SanDiskGb;
                    }
                    else
                    {
                        storage.ATAInfo.VendorID = VendorIDs.SSDVendorSandiskLenovo;
                        storage.SmartKey = SmartKey.SanDiskLenovo;
                    }
                }
                else if (ModelContains(storage, "G1012"    )
                      || ModelContains(storage, "Z400s 2.5")) //Dell OEM
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;

                    storage.ATAInfo.VendorID = VendorIDs.SSDVendorSandiskDell;
                    storage.SmartKey = SmartKey.SanDiskDell;
                }
                else if (ModelContains(storage, "SSD P4"))
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;

                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeSanDiskUsbMemory;
                    storage.SmartKey = SmartKey.SanDisk;
                }
                else if (ModelContains(storage, "iSSD P4"))
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;
                    storage.SmartKey = SmartKey.SanDiskGb;
                }
                else if (ModelContains(storage, "SDSSDP" )
                      || ModelContains(storage, "SDSSDRC"))
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;

                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeSanDisk0_1;
                    storage.SmartKey = SmartKey.SanDisk;
                }
                else if (ModelContains(storage, "SSD U100")
                      || ModelContains(storage, "SSD U110")
                      || ModelContains(storage, "SSD i100")
                      || ModelContains(storage, "SSD i110")
                      || ModelContains(storage, "pSSD"    ))
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;

                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeSanDiskUsbMemory;
                    storage.SmartKey = SmartKey.SanDisk;
                }
                else if (
                         //CloudSpeed ECO Gen II Eco SSD
                         ModelContains(storage, "SDLF1CRR-")
                      || ModelContains(storage, "SDLF1DAR-")
                         //CloudSpeed ECO Gen II Ultra SSD
                      || ModelContains(storage, "SDLF1CRM-")
                      || ModelContains(storage, "SDLF1DAM-"))
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;

                    storage.ATAInfo.VendorID = VendorIDs.SSDVendorSandiskCloud;

                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeSanDiskCloud;
                    storage.SmartKey = SmartKey.SanDiskCloud;
                }
                else
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;

                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeSanDisk1;
                    storage.SmartKey = SmartKey.SanDiskGb;
                }
            }

            return flagSmartType;
        }

        public static bool IsSSDWDC(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelStartsWith(storage, "WDC ")
             || ModelStartsWith(storage, "WD " ))
            {
                flagSmartType = true;

                if (ModelContains(storage, "SA530"))
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites16MB;
                }
                else
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                }
            }

            return flagSmartType;
        }

        public static bool IsSSDSeagate(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x05
             && GetAt(smartAttributes,  2)?.ID == 0x09
             && GetAt(smartAttributes,  3)?.ID == 0x0C
             && GetAt(smartAttributes,  4)?.ID == 0x64
             && GetAt(smartAttributes,  5)?.ID == 0x66
             && GetAt(smartAttributes,  6)?.ID == 0x67
             && GetAt(smartAttributes,  7)?.ID == 0xAA
             && GetAt(smartAttributes,  8)?.ID == 0xAB
             && GetAt(smartAttributes,  9)?.ID == 0xAC
             && GetAt(smartAttributes, 10)?.ID == 0xAD
             && GetAt(smartAttributes, 11)?.ID == 0xAE
             && GetAt(smartAttributes, 12)?.ID == 0xB1
             && GetAt(smartAttributes, 13)?.ID == 0xB7
             && GetAt(smartAttributes, 14)?.ID == 0xBB)
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;

                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSeagate;
                storage.SmartKey = SmartKey.SeagateIronWolf;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x01
                  && GetAt(smartAttributes,  1)?.ID == 0x09
                  && GetAt(smartAttributes,  2)?.ID == 0x0C
                  && GetAt(smartAttributes,  3)?.ID == 0x10
                  && GetAt(smartAttributes,  4)?.ID == 0x11
                  && GetAt(smartAttributes,  5)?.ID == 0xA8
                  && GetAt(smartAttributes,  6)?.ID == 0xAA
                  && GetAt(smartAttributes,  7)?.ID == 0xAD
                  && GetAt(smartAttributes,  8)?.ID == 0xAE
                  && GetAt(smartAttributes,  9)?.ID == 0xB1
                  && GetAt(smartAttributes, 10)?.ID == 0xC0
                  && GetAt(smartAttributes, 11)?.ID == 0xC2
                  && GetAt(smartAttributes, 12)?.ID == 0xDA
                  && GetAt(smartAttributes, 13)?.ID == 0xE7
                  && GetAt(smartAttributes, 14)?.ID == 0xE8
                  && GetAt(smartAttributes, 15)?.ID == 0xE9
                  && GetAt(smartAttributes, 16)?.ID == 0xEB
                  && GetAt(smartAttributes, 17)?.ID == 0xF1
                  && GetAt(smartAttributes, 18)?.ID == 0xF2)
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                storage.SmartKey = SmartKey.Seagate;

                storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSeagate;
            }
            else if (ModelStartsWith(storage, "Seagate")
                  ||
                  (
                       ModelIndexOf(storage, "STT") != 0
                    && ModelStartsWith(storage, "ST")
                  )
                  || ModelContains(storage, "ZA"))
            {
                flagSmartType = true;

                if (ModelContains(storage, "BarraCuda"))
                {
                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
                    storage.SmartKey = SmartKey.SeagateBarraCuda;
                }
                else if (ModelContains(storage, "HM")
                      || ModelContains(storage, "FP"))
                {
                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
                    storage.SmartKey = SmartKey.Seagate;
                }
                else
                {
                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
                    storage.SmartKey = SmartKey.Seagate;
                }

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;

                storage.ATAInfo.VendorID = VendorIDs.SSDVendorSeagate;
            }

            return flagSmartType;
        }

        public static bool IsSSDMtron(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            return
                (
                    smartAttributes.Count == 1
                 && GetAt(smartAttributes, 0)?.ID == 0xBB
                )
              || ModelStartsWith(storage, "MTRON");
        }

        public static bool IsSSDToshiba(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelContains(storage, "TOSHIBA") && storage.IsSSD)
            {
                flagSmartType = true;

                if (ModelContains(storage, "THNSNC")
                 || ModelContains(storage, "THNSNJ")
                 || ModelContains(storage, "THNSNK")
                 || ModelContains(storage, "KSG60" )
                 || ModelContains(storage, "TL100" )
                 || ModelContains(storage, "TR150" )
                 || ModelContains(storage, "TR200" ))
                {
                    //TOSHIBA HG3
                    //TOSHIBA KSG60ZMV

                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites32MB;
                }
                else
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                }
            }

            return flagSmartType;
        }

        public static bool IsSSDJMicron66x(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x02
             && GetAt(smartAttributes,  2)?.ID == 0x03
             && GetAt(smartAttributes,  3)?.ID == 0x05
             && GetAt(smartAttributes,  4)?.ID == 0x07
             && GetAt(smartAttributes,  5)?.ID == 0x08
             && GetAt(smartAttributes,  6)?.ID == 0x09
             && GetAt(smartAttributes,  7)?.ID == 0x0A
             && GetAt(smartAttributes,  8)?.ID == 0x0C
             && GetAt(smartAttributes,  9)?.ID == 0xA7
             && GetAt(smartAttributes, 10)?.ID == 0xA8
             && GetAt(smartAttributes, 11)?.ID == 0xA9
             && GetAt(smartAttributes, 12)?.ID == 0xAA
             && GetAt(smartAttributes, 13)?.ID == 0xAD
             && GetAt(smartAttributes, 14)?.ID == 0xAF)
            {
                flagSmartType = true;
            }
            else if (ModelStartsWith(storage, "ADATA SU700"))
            {
                flagSmartType = true;
            }

            return flagSmartType;
        }

        public static bool IsSSDJMicron61x(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x02
             && GetAt(smartAttributes,  2)?.ID == 0x03
             && GetAt(smartAttributes,  3)?.ID == 0x05
             && GetAt(smartAttributes,  4)?.ID == 0x07
             && GetAt(smartAttributes,  5)?.ID == 0x08
             && GetAt(smartAttributes,  6)?.ID == 0x09
             && GetAt(smartAttributes,  7)?.ID == 0x0A
             && GetAt(smartAttributes,  8)?.ID == 0x0C
             && GetAt(smartAttributes,  9)?.ID == 0xA8
             && GetAt(smartAttributes, 10)?.ID == 0xAF
             && GetAt(smartAttributes, 11)?.ID == 0xC0
             && GetAt(smartAttributes, 12)?.ID == 0xC2
             )
            {
                flagSmartType = true;
            }

            return flagSmartType;
        }

        public static bool IsSSDJMicron60x(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x0C
             && GetAt(smartAttributes,  1)?.ID == 0x09
             && GetAt(smartAttributes,  2)?.ID == 0xC2
             && GetAt(smartAttributes,  3)?.ID == 0xE5
             && GetAt(smartAttributes,  4)?.ID == 0xE8
             && GetAt(smartAttributes,  5)?.ID == 0xE9
             )
            {
                flagSmartType = true;
            }

            return flagSmartType;
        }

        public static bool IsSSDIndilinx(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x09
             && GetAt(smartAttributes,  2)?.ID == 0x0C
             && GetAt(smartAttributes,  3)?.ID == 0xB8
             && GetAt(smartAttributes,  4)?.ID == 0xC3
             && GetAt(smartAttributes,  5)?.ID == 0xC4
             )
            {
                flagSmartType = true;
            }

            return flagSmartType;
        }

        public static bool IsSSDIntelDc(Storage storage)
        {
            return ModelContains(storage, "INTEL SSDSCKHB");
        }

        public static bool IsSSDIntel(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x03
             && GetAt(smartAttributes,  1)?.ID == 0x04
             && GetAt(smartAttributes,  2)?.ID == 0x05
             && GetAt(smartAttributes,  3)?.ID == 0x09
             && GetAt(smartAttributes,  4)?.ID == 0x0C
             )
            {
                var attr5 = GetAt(smartAttributes, 5);
                var attr6 = GetAt(smartAttributes, 6);
                var attr7 = GetAt(smartAttributes, 7);

                if (attr5?.ID == 0xC0 && attr6?.ID == 0xE8 && attr7?.ID == 0xE9)
                {
                    flagSmartType = true;
                }
                else if (attr5?.ID == 0xC0 && attr6?.ID == 0xE1)
                {
                    flagSmartType = true;
                }
                else if (attr5?.ID == 0xAA && attr6?.ID == 0xAB && attr7?.ID == 0xAC)
                {
                    flagSmartType = true;
                }
            }

            return flagSmartType
                || ModelContains(storage, "INTEL"   )
                || ModelContains(storage, "SOLIDIGM");
        }

        public static bool IsSSDSamsung(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            //SM951
            if (GetAt(smartAttributes,  0)?.ID == 0x05
             && GetAt(smartAttributes,  1)?.ID == 0x09
             && GetAt(smartAttributes,  2)?.ID == 0x0C
             && GetAt(smartAttributes,  3)?.ID == 0xAA
             && GetAt(smartAttributes,  4)?.ID == 0xAB
             && GetAt(smartAttributes,  5)?.ID == 0xAC
             && GetAt(smartAttributes,  6)?.ID == 0xAD
             && GetAt(smartAttributes,  7)?.ID == 0xAE
             && GetAt(smartAttributes,  8)?.ID == 0xB2
             && GetAt(smartAttributes,  9)?.ID == 0xB4
             )
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x09
                  && GetAt(smartAttributes,  1)?.ID == 0x0C
                  && GetAt(smartAttributes,  2)?.ID == 0xB2
                  && GetAt(smartAttributes,  3)?.ID == 0xB3
                  && GetAt(smartAttributes,  4)?.ID == 0xB4
                  )
            {
                flagSmartType = true;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x09
                  && GetAt(smartAttributes,  1)?.ID == 0x0C
                  && GetAt(smartAttributes,  2)?.ID == 0xB1
                  && GetAt(smartAttributes,  3)?.ID == 0xB2
                  && GetAt(smartAttributes,  4)?.ID == 0xB3
                  && GetAt(smartAttributes,  5)?.ID == 0xB4
                  && GetAt(smartAttributes,  6)?.ID == 0xB7
                  )
            {
                flagSmartType = true;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x09
                  && GetAt(smartAttributes,  1)?.ID == 0x0C
                  && GetAt(smartAttributes,  2)?.ID == 0xAF
                  && GetAt(smartAttributes,  3)?.ID == 0xB0
                  && GetAt(smartAttributes,  4)?.ID == 0xB1
                  && GetAt(smartAttributes,  5)?.ID == 0xB2
                  && GetAt(smartAttributes,  6)?.ID == 0xB3
                  && GetAt(smartAttributes,  7)?.ID == 0xB4
                  )
            {
                flagSmartType = true;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x05
                  && GetAt(smartAttributes,  1)?.ID == 0x09
                  && GetAt(smartAttributes,  2)?.ID == 0x0C
                  && GetAt(smartAttributes,  3)?.ID == 0xB1
                  && GetAt(smartAttributes,  4)?.ID == 0xB3
                  && GetAt(smartAttributes,  5)?.ID == 0xB5
                  && GetAt(smartAttributes,  6)?.ID == 0xB6
                  )
            {
                flagSmartType = true;
            }

            return flagSmartType
                ||
                (
                    ModelContains(storage, "SAMSUNG")
                 || ModelContains(storage, "MZ-"    )
                 && storage.IsSSD
                );
        }

        public static bool IsSSDMicronMU03(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelStartsWith(storage, "MICRON_M600")
             || ModelStartsWith(storage, "MICRON_M550")
             || ModelStartsWith(storage, "MICRON_M510")
             || ModelStartsWith(storage, "MICRON_M500")
             || ModelStartsWith(storage, "MICRON_1300")
             || ModelStartsWith(storage, "MICRON_1100")

             || ModelStartsWith(storage, "MICRON M600")
             || ModelStartsWith(storage, "MICRON M550")
             || ModelStartsWith(storage, "MICRON M510")
             || ModelStartsWith(storage, "MICRON M500")
             || ModelStartsWith(storage, "MICRON 1300")
             || ModelStartsWith(storage, "MICRON 1100")

             || ModelStartsWith(storage, "MTFDDA"))
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;
            }
            else if (ModelContains(storage, "M500SSD" )
                  || ModelContains(storage, "MX500SSD")
                  || ModelContains(storage, "MX300SSD")
                  || ModelContains(storage, "MX200SSD")
                  || ModelContains(storage, "MX100SSD")

                  || ModelContains(storage, "BX500SSD")
                  || ModelContains(storage, "BX300SSD")
                  || ModelContains(storage, "BX200SSD")
                  || ModelContains(storage, "BX100SSD")

                  || ModelStartsWith(storage, "MTFD"  )
                  && !FirmwareRevContains(storage, "MU01"))
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites32MB;
            }

            return flagSmartType;
        }

        public static bool IsSSDMicron(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            //Micron RealSSD & Crucial

            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x05
             && GetAt(smartAttributes,  2)?.ID == 0x09
             && GetAt(smartAttributes,  3)?.ID == 0x0C
             && GetAt(smartAttributes,  4)?.ID == 0xAA
             && GetAt(smartAttributes,  5)?.ID == 0xAB
             && GetAt(smartAttributes,  6)?.ID == 0xAC
             && GetAt(smartAttributes,  7)?.ID == 0xAD
             && GetAt(smartAttributes,  8)?.ID == 0xAE
             && GetAt(smartAttributes,  9)?.ID == 0xB5
             && GetAt(smartAttributes, 10)?.ID == 0xB7
             )
            {
                flagSmartType = true;
            }

            return flagSmartType
                || ModelStartsWith(storage, "P600")
                || ModelStartsWith(storage, "C600")
                || ModelStartsWith(storage, "M6-" )
                || ModelStartsWith(storage, "M600")
                || ModelStartsWith(storage, "P500")
                //workaround for Maxiotek C500
                || (ModelStartsWith(storage, "C500") && FirmwareRevIndexOf(storage, "H") != 0)
                || ModelStartsWith(storage, "M5-" )
                || ModelStartsWith(storage, "M500")
                || ModelStartsWith(storage, "P400")
                || ModelStartsWith(storage, "C400")
                || ModelStartsWith(storage, "M4-" )
                || ModelStartsWith(storage, "M400")
                || ModelStartsWith(storage, "P300")
                || ModelStartsWith(storage, "C300")
                || ModelStartsWith(storage, "M3-" )
                || ModelStartsWith(storage, "M300")
                || (ModelStartsWith(storage, "CT" ) && ModelContains(storage, "SSD"))
                || ModelStartsWith(storage, "CRUCIAL")
                || ModelStartsWith(storage, "MICRON" )
                || ModelStartsWith(storage, "MTFD"   )
                ;
        }

        public static bool IsSSDSandForce(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x05
             && GetAt(smartAttributes,  2)?.ID == 0x09
             && GetAt(smartAttributes,  3)?.ID == 0x0C
             && GetAt(smartAttributes,  4)?.ID == 0x0D
             && GetAt(smartAttributes,  5)?.ID == 0x64
             && GetAt(smartAttributes,  6)?.ID == 0xAA
             )
            {
                flagSmartType = true;
            }

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x05
             && GetAt(smartAttributes,  2)?.ID == 0x09
             && GetAt(smartAttributes,  3)?.ID == 0x0C
             && GetAt(smartAttributes,  4)?.ID == 0xAB
             && GetAt(smartAttributes,  5)?.ID == 0xAC
             )
            {
                flagSmartType = true;
            }

            //TOSHIBA + SandForce
            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x02
             && GetAt(smartAttributes,  2)?.ID == 0x03
             && GetAt(smartAttributes,  3)?.ID == 0x05
             && GetAt(smartAttributes,  4)?.ID == 0x07
             && GetAt(smartAttributes,  5)?.ID == 0x08
             && GetAt(smartAttributes,  6)?.ID == 0x09
             && GetAt(smartAttributes,  7)?.ID == 0x0A
             && GetAt(smartAttributes,  8)?.ID == 0x0C
             && GetAt(smartAttributes,  9)?.ID == 0xA7
             && GetAt(smartAttributes, 10)?.ID == 0xA8
             && GetAt(smartAttributes, 11)?.ID == 0xA9
             && GetAt(smartAttributes, 12)?.ID == 0xAA
             && GetAt(smartAttributes, 13)?.ID == 0xAD
             && GetAt(smartAttributes, 14)?.ID == 0xAF
             && GetAt(smartAttributes, 15)?.ID == 0xB1
             )
            {
                flagSmartType = true;
            }

            return flagSmartType
                || ModelContains(storage, "SandForce");
        }

        public static bool IsSSDOcz(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            //OCZ-TRION100
            if (ModelStartsWith(storage, "OCZ-TRION"))
            {
                flagSmartType = true;
            }

            //OCZ-PETROL
            //OCZ-OCTANE S2
            //OCZ-VERTEX 4
            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x03
             && GetAt(smartAttributes,  2)?.ID == 0x04
             && GetAt(smartAttributes,  3)?.ID == 0x05
             && GetAt(smartAttributes,  4)?.ID == 0x09
             && GetAt(smartAttributes,  5)?.ID == 0x0C
             && GetAt(smartAttributes,  6)?.ID == 0xE8
             && GetAt(smartAttributes,  7)?.ID == 0xE9
             )
            {
                flagSmartType = true;
            }

            return flagSmartType
                || ModelStartsWith(storage, "OCZ");
        }

        public static bool IsSSDOczVector(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            //OCZ-TRION100
            if (ModelStartsWith(storage, "RADEON R7"))
            {
                flagSmartType = true;

                return flagSmartType;
            }

            //PANASONIC RP-SSB240GAK
            if (GetAt(smartAttributes,  0)?.ID == 0x05
             && GetAt(smartAttributes,  1)?.ID == 0x09
             && GetAt(smartAttributes,  2)?.ID == 0x0C
             && GetAt(smartAttributes,  3)?.ID == 0xAB
             && GetAt(smartAttributes,  4)?.ID == 0xAE
             && GetAt(smartAttributes,  5)?.ID == 0xC3
             && GetAt(smartAttributes,  6)?.ID == 0xC4
             && GetAt(smartAttributes,  7)?.ID == 0xC5
             && GetAt(smartAttributes,  8)?.ID == 0xC6
             )
            {
                flagSmartType = true;
            }

            if (ModelStartsWith(storage, "PANASONIC RP-SSB"))
            {
                flagSmartType = true;
            }

            return flagSmartType
                || ModelStartsWith(storage, "OCZ");
        }

        public static bool IsSSDSsstc(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelContains(storage, "CV8-")
             || ModelContains(storage, "CVB-")
             || ModelContains(storage, "ER2-"))
            {
                flagSmartType = true;
            }

            return flagSmartType;
        }

        public static bool IsSSDPlextor(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x05
             && GetAt(smartAttributes,  2)?.ID == 0x09
             && GetAt(smartAttributes,  3)?.ID == 0x0C
             && GetAt(smartAttributes,  4)?.ID == 0xB1
             && GetAt(smartAttributes,  5)?.ID == 0xB2
             && GetAt(smartAttributes,  6)?.ID == 0xB5
             && GetAt(smartAttributes,  7)?.ID == 0xB6
             )
            {
                flagSmartType = true;
            }

            //CFD's SSD
            //LITEON CV6-CQ
            return flagSmartType
                || ModelStartsWith(storage, "PLEXTOR"         )
                || ModelStartsWith(storage, "LITEON"          )
                || ModelStartsWith(storage, "CV6-CQ"          )
                || ModelStartsWith(storage, "CSSD-S6T128NM3PQ")
                || ModelStartsWith(storage, "CSSD-S6T256NM3PQ");
        }

        public static bool IsSSDKingston(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelContains(storage, "KINGSTON"))
            {
                if (ModelContains(storage, "SM2280" )
                 || ModelContains(storage, "SEDC400")
                 || ModelContains(storage, "SKC310" )
                 || ModelContains(storage, "SHSS"   )
                 || ModelContains(storage, "SUV300" )
                 || ModelContains(storage, "SKC400" ))
                {
                    flagSmartType = true;

                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                    storage.SmartKey = SmartKey.Kingston;
                }
                else if (ModelContains(storage, "SA400"))
                {
                    flagSmartType = true;

                    if (FirmwareStartsWith(storage, "03070009"))
                    {
                        storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
                    }
                    else if (FirmwareStartsWith(storage, "SBFK62C3"))
                    {
                        storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
                    }
                    else
                    {
                        storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
                    }

                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                    storage.SmartKey = SmartKey.KingstonSA400;
                }
                else if (ModelContains(storage, "KC600"))
                {
                    flagSmartType = true;

                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites32MB;
                    storage.SmartKey = SmartKey.KingstonKC600;
                }
                else if (ModelContains(storage, "DC500"))
                {
                    flagSmartType = true;

                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                    storage.SmartKey = SmartKey.KingstonDC500;
                }
                else if (ModelContains(storage, "SUV400")
                      || ModelContains(storage, "SUV500"))
                {
                    flagSmartType = true;

                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                    storage.SmartKey = SmartKey.KingstonSuv;
                }
            }

            return flagSmartType;
        }

        public static bool IsSSDCorsair(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelStartsWith(storage, "Corsair"))
            {
                flagSmartType = true;

                if (ModelContains(storage, "Voyager GTX"))
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites1MB;
                }
            }

            return flagSmartType;
        }

        public static bool IsSSDRealtek(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x05
             && GetAt(smartAttributes,  2)?.ID == 0x09
             && GetAt(smartAttributes,  3)?.ID == 0x0C
             && GetAt(smartAttributes,  4)?.ID == 0xA1
             && GetAt(smartAttributes,  5)?.ID == 0xA2
             && GetAt(smartAttributes,  6)?.ID == 0xA3
             && GetAt(smartAttributes,  7)?.ID == 0xA4
             && GetAt(smartAttributes,  8)?.ID == 0xA6
             && GetAt(smartAttributes,  9)?.ID == 0xA7
             )
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
            }

            return flagSmartType;
        }

        public static bool IsSSDSKHynix(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelContains  (storage, "SK hynix")
             || ModelStartsWith(storage, "HFS")
             || ModelStartsWith(storage, "SHG"))
            {
                flagSmartType = true;
            }

            if (
                    (ModelContains(storage, "HFS") && ModelContains(storage, "TND")) //SL300
                 || (ModelContains(storage, "HFS") && ModelContains(storage, "MND")) //SC210
               )
            {
                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;

                storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValueIncrement;
            }
            else if (ModelContains(storage, "HFS")
                  && ModelContains(storage, "TNF"))
            {
                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;

                storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
            }
            else if (ModelContains(storage, "SC311")
                  || ModelContains(storage, "SC401"))
            {
                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;

                storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
            }
            else
            {
                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
            }

            return flagSmartType;
        }

        public static bool IsSSDKioxia(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelContains(storage, "KIOXIA"))
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites32MB;
            }

            return flagSmartType;
        }

        public static bool IsSSDSiliconMotionCVC(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelContains(storage, "CVC-"))
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
            }

            return flagSmartType;
        }

        public static bool IsSSDSiliconMotion(Storage storage, List<SmartAttributeStructure> smartAttributes, byte[] smartReadData)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x05
             && GetAt(smartAttributes,  2)?.ID == 0x09
             && GetAt(smartAttributes,  3)?.ID == 0x0C
             && GetAt(smartAttributes,  4)?.ID == 0xA0
             && GetAt(smartAttributes,  5)?.ID == 0xA1
             && GetAt(smartAttributes,  6)?.ID == 0xA3
             && GetAt(smartAttributes,  7)?.ID == 0xA4
             && GetAt(smartAttributes,  8)?.ID == 0xA5
             && GetAt(smartAttributes,  9)?.ID == 0xA6
             && GetAt(smartAttributes, 10)?.ID == 0xA7
             && GetAt(smartAttributes, 11)?.ID == 0xA8
             && GetAt(smartAttributes, 12)?.ID == 0xA9
             && GetAt(smartAttributes, 13)?.ID == 0xAF
             && GetAt(smartAttributes, 14)?.ID == 0xB0
             && GetAt(smartAttributes, 15)?.ID == 0xB1
             && GetAt(smartAttributes, 16)?.ID == 0xB2
             && GetAt(smartAttributes, 17)?.ID == 0xB5
             && GetAt(smartAttributes, 18)?.ID == 0xB6
             && GetAt(smartAttributes, 19)?.ID == 0xC0
             )
            {
                flagSmartType = true;
            }
            //ADATA SX950
            else if (GetAt(smartAttributes,  0)?.ID == 0x01
                  && GetAt(smartAttributes,  1)?.ID == 0x05
                  && GetAt(smartAttributes,  2)?.ID == 0x09
                  && GetAt(smartAttributes,  3)?.ID == 0x0C
                  && GetAt(smartAttributes,  4)?.ID == 0xA0
                  && GetAt(smartAttributes,  5)?.ID == 0xA1
                  && GetAt(smartAttributes,  6)?.ID == 0xA3
                  && GetAt(smartAttributes,  7)?.ID == 0xA4
                  && GetAt(smartAttributes,  8)?.ID == 0xA5
                  && GetAt(smartAttributes,  9)?.ID == 0xA6
                  && GetAt(smartAttributes, 10)?.ID == 0xA7
                  && GetAt(smartAttributes, 11)?.ID == 0x94
                  && GetAt(smartAttributes, 12)?.ID == 0x95
                  && GetAt(smartAttributes, 13)?.ID == 0x96
                  && GetAt(smartAttributes, 14)?.ID == 0x97
                  && GetAt(smartAttributes, 15)?.ID == 0xA9
                  && GetAt(smartAttributes, 16)?.ID == 0xB1
                  && GetAt(smartAttributes, 17)?.ID == 0xB5
                  && GetAt(smartAttributes, 18)?.ID == 0xB6
                  && GetAt(smartAttributes, 19)?.ID == 0xBB
                  )
            {
                flagSmartType = true;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x01
                  && GetAt(smartAttributes,  1)?.ID == 0x05
                  && GetAt(smartAttributes,  2)?.ID == 0x09
                  && GetAt(smartAttributes,  3)?.ID == 0x0C
                  && GetAt(smartAttributes,  4)?.ID == 0x94
                  && GetAt(smartAttributes,  5)?.ID == 0x95
                  && GetAt(smartAttributes,  6)?.ID == 0x96
                  && GetAt(smartAttributes,  7)?.ID == 0x97
                  && GetAt(smartAttributes,  8)?.ID == 0x9F
                  && GetAt(smartAttributes,  9)?.ID == 0xA0
                  && GetAt(smartAttributes, 10)?.ID == 0xA1
                  )
            {
                flagSmartType = true;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x01
                  && GetAt(smartAttributes,  1)?.ID == 0x05
                  && GetAt(smartAttributes,  2)?.ID == 0x09
                  && GetAt(smartAttributes,  3)?.ID == 0x0C
                  && GetAt(smartAttributes,  4)?.ID == 0xA0
                  && GetAt(smartAttributes,  5)?.ID == 0xA1
                  && GetAt(smartAttributes,  6)?.ID == 0xA3
                  && GetAt(smartAttributes,  7)?.ID == 0xA4
                  && GetAt(smartAttributes,  8)?.ID == 0xA5
                  && GetAt(smartAttributes,  9)?.ID == 0xA6
                  && GetAt(smartAttributes, 10)?.ID == 0xA7
                  )
            {
                flagSmartType = true;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x01
                  && GetAt(smartAttributes,  1)?.ID == 0x05
                  && GetAt(smartAttributes,  2)?.ID == 0x09
                  && GetAt(smartAttributes,  3)?.ID == 0x0C
                  && GetAt(smartAttributes,  4)?.ID == 0xA0
                  && GetAt(smartAttributes,  5)?.ID == 0xA1
                  && GetAt(smartAttributes,  6)?.ID == 0xA3
                  && GetAt(smartAttributes,  7)?.ID == 0x94
                  && GetAt(smartAttributes,  8)?.ID == 0x95
                  && GetAt(smartAttributes,  9)?.ID == 0x96
                  && GetAt(smartAttributes, 10)?.ID == 0x97
                  )
            {
                flagSmartType = true;
            }
            else if (ModelStartsWith(storage, "TS")) //Transcend
            {
                if ( (smartReadData[400] == 'T' && smartReadData[401] == 'S') //Transcend
                  || (smartReadData[400] == 'S' && smartReadData[401] == 'M') //Silicon Motion
                   )
                {
                    flagSmartType = true;
                }
            }
            else if (ModelStartsWith(storage, "ADATA SX950"))
            {
                flagSmartType = true;
            }

            if (flagSmartType)
            {
                if (ModelStartsWith   (storage, "SSD")
                 && FirmwareStartsWith(storage, "FW" )) //Goldenfir SSD
                {
                    //Empty
                }
                else if (ModelStartsWith(storage, "WT200")
                      || ModelStartsWith(storage, "WT100")
                      || ModelStartsWith(storage, "WT "  ))
                {
                    //Empty
                }
                else if (ModelStartsWith(storage, "tecmiyo"))
                {
                    //Empty
                }
                else if (ModelStartsWith(storage, "ADATA SU650")
                      || ModelStartsWith(storage, "XD0R3C0A"   ))
                {
                    //Empty
                }
                else
                {
                    storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
                }
            }

            return flagSmartType;
        }

        public static bool IsSSDPhison(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x01
             && GetAt(smartAttributes,  1)?.ID == 0x09
             && GetAt(smartAttributes,  2)?.ID == 0x0C
             && GetAt(smartAttributes,  3)?.ID == 0xA8
             && GetAt(smartAttributes,  4)?.ID == 0xAA
             && GetAt(smartAttributes,  5)?.ID == 0xAD
             && GetAt(smartAttributes,  6)?.ID == 0xC0
             && GetAt(smartAttributes,  7)?.ID == 0xC2 //with Temperature Sensor
             && GetAt(smartAttributes,  8)?.ID == 0xDA
             && GetAt(smartAttributes,  9)?.ID == 0xE7
             && GetAt(smartAttributes, 10)?.ID == 0xF1
             )
            {
                flagSmartType = true;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x01
                  && GetAt(smartAttributes,  1)?.ID == 0x09
                  && GetAt(smartAttributes,  2)?.ID == 0x0C
                  && GetAt(smartAttributes,  3)?.ID == 0xA8
                  && GetAt(smartAttributes,  4)?.ID == 0xAA
                  && GetAt(smartAttributes,  5)?.ID == 0xAD
                  && GetAt(smartAttributes,  6)?.ID == 0xC0
                  && GetAt(smartAttributes,  7)?.ID == 0xDA
                  && GetAt(smartAttributes,  8)?.ID == 0xE7
                  && GetAt(smartAttributes,  9)?.ID == 0xF1
                  )
            {
                flagSmartType = true;
            }

            if (flagSmartType)
            {
                storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;

                if (FirmwareRevStartsWith(storage, "S9"))
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites1MB;
                }
                else
                {
                    storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
                }
            }

            return flagSmartType;
        }

        public static bool IsSSDMarvell(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (GetAt(smartAttributes,  0)?.ID == 0x05
             && GetAt(smartAttributes,  1)?.ID == 0x09
             && GetAt(smartAttributes,  2)?.ID == 0x0C
             && GetAt(smartAttributes,  3)?.ID == 0xA1
             && GetAt(smartAttributes,  4)?.ID == 0xA4
             && GetAt(smartAttributes,  5)?.ID == 0xA5
             && GetAt(smartAttributes,  6)?.ID == 0xA6
             && GetAt(smartAttributes,  7)?.ID == 0xA7
             )
            {
                flagSmartType = true;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x05
                  && GetAt(smartAttributes,  1)?.ID == 0x09
                  && GetAt(smartAttributes,  2)?.ID == 0x0C
                  && GetAt(smartAttributes,  3)?.ID == 0xA4
                  && GetAt(smartAttributes,  4)?.ID == 0xA5
                  && GetAt(smartAttributes,  5)?.ID == 0xA6
                  && GetAt(smartAttributes,  6)?.ID == 0xA7
                  )
            {
                flagSmartType = true;
            }

            if (flagSmartType)
            {
                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
            }

            if (ModelStartsWith      (storage, "LEXAR")
             && (FirmwareRevStartsWith(storage, "SN0") || FirmwareRevStartsWith(storage, "V6"))
               )
            {
                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites32MB;
            }

            if (ModelStartsWith(storage, "HANYE-Q55"))
            {
                return false;
            }

            return flagSmartType;
        }

        public static bool IsSSDMaxiotek(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            bool flagSmartType = false;

            if (ModelStartsWith(storage, "MAXIO"))
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
            }
            else if (ModelStartsWith(storage, "HANYE-Q55")
                  && GetAt(smartAttributes,  0)?.ID == 0x05
                  && GetAt(smartAttributes,  1)?.ID == 0x09
                  && GetAt(smartAttributes,  2)?.ID == 0x0C
                  && GetAt(smartAttributes,  3)?.ID == 0xA4
                  && GetAt(smartAttributes,  4)?.ID == 0xA5
                  && GetAt(smartAttributes,  5)?.ID == 0xA6
                  && GetAt(smartAttributes,  6)?.ID == 0xA7
                  )
            {
                flagSmartType = true;
            }
            else if (GetAt(smartAttributes,  0)?.ID == 0x05
                  && GetAt(smartAttributes,  1)?.ID == 0x09
                  && GetAt(smartAttributes,  2)?.ID == 0x0C
                  && GetAt(smartAttributes,  3)?.ID == 0xA7
                  && GetAt(smartAttributes,  4)?.ID == 0xA8
                  && GetAt(smartAttributes,  5)?.ID == 0xA9
                  )
            {
                flagSmartType = true;
            }

            return flagSmartType;
        }

        public static bool IsSSDApacer(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelStartsWith      (storage, "Apacer")
             || ModelStartsWith      (storage, "ZADAK")
             || FirmwareRevStartsWith(storage, "AP")
             || FirmwareRevStartsWith(storage, "SF")
             || FirmwareRevStartsWith(storage, "PN"))
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;

                storage.ATAInfo.FlagLife |= FlagLife.FlagLifeRawValue;
            }

            return flagSmartType;
        }

        public static bool IsSSDYmtc(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelContains(storage, "ZHITAI"))
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;
            }

            return flagSmartType;
        }

        public static bool IsSSDScy(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelStartsWith(storage, "SCY")) //SCY S500
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites32MB;
            }

            return flagSmartType;
        }

        public static bool IsSSDRecadata(Storage storage)
        {
            bool flagSmartType = false;

            if (ModelStartsWith(storage, "RECADATA"))
            {
                flagSmartType = true;

                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
            }

            return flagSmartType;
        }

        public static bool IsSSDGeneral(Storage storage)
        {
            storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesUnknown;

            if (ModelStartsWith(storage, "ADATA SP580"))
            {
                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites512B;
            }
            else if (ModelStartsWith(storage, "LITEON IT LMT"))
            {
                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWrites32MB;
            }
            else if (ModelStartsWith(storage, "LITEON S960"))
            {
                storage.HostReadsWritesUnit = HostReadsWritesUnit.HostReadsWritesGB;
            }

            return storage.IsSSD;
        }

        #endregion

        #region Private

        static SmartAttributeStructure? GetAt(List<SmartAttributeStructure> smartAttributes, int index)
        {
            if (index >= smartAttributes.Count || index < 0)
            {
                return null;
            }
            else
            {
                return smartAttributes[index];
            }
        }

        static bool ModelContains(Storage storage, string text)
        {
            return Storage.ModelContains(storage, text);
        }

        static bool ModelStartsWith(Storage storage, string text)
        {
            return Storage.ModelStartsWith(storage, text);
        }

        static int ModelIndexOf(Storage storage, string text)
        {
            return storage.Model.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        }

        static bool FirmwareStartsWith(Storage storage, string text)
        {
            return storage.Firmware.StartsWith(text, StringComparison.OrdinalIgnoreCase);
        }

        static bool FirmwareRevContains(Storage storage, string text)
        {
            return storage.FirmwareRev.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        static bool FirmwareRevStartsWith(Storage storage, string text)
        {
            return storage.FirmwareRev.StartsWith(text, StringComparison.OrdinalIgnoreCase);
        }

        static int FirmwareRevIndexOf(Storage storage, string text)
        {
            return storage.FirmwareRev.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
