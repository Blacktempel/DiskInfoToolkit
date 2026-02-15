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

using BlackSharp.Core.Converters;
using BlackSharp.Core.Extensions;
using DiskInfoToolkit.Enums.Interop;
using DiskInfoToolkit.HardDrive;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Interop.Enums;
using DiskInfoToolkit.Interop.Realtek;
using DiskInfoToolkit.Interop.Structures;
using DiskInfoToolkit.Logging;
using DiskInfoToolkit.NVMe;
using DiskInfoToolkit.PCI;
using DiskInfoToolkit.Smart;
using DiskInfoToolkit.SSD;
using DiskInfoToolkit.Utilities;
using System.Text;

namespace DiskInfoToolkit.Disk
{
    internal static class DiskHandler
    {
        #region Internal

        internal static void UpdateSmartInfo(Storage storage, IntPtr handle)
        {
            if (storage.IsNVMe)
            {
                var smartReadDataBuffer = new byte[InteropConstants.IDENTIFY_BUFFER_SIZE];

                if (GetSmartAttributes(storage, handle, storage.Command, smartReadDataBuffer))
                {
                    storage.Smart.Temperature = smartReadDataBuffer[0x2] * 256 + smartReadDataBuffer[0x1] - 273;
                    if (storage.Smart.Temperature == -273 || storage.Smart.Temperature > 100)
                    {
                        storage.Smart.Temperature = null;
                    }

                    storage.Smart.Life = (sbyte)(100 - smartReadDataBuffer[0x05]);

                    storage.Smart.HostReads = (BitConverter.ToUInt64(smartReadDataBuffer, 0x20) * 1000) >> 21;
                    storage.Smart.HostWrites = (BitConverter.ToUInt64(smartReadDataBuffer, 0x30) * 1000) >> 21;

                    storage.Smart.PowerOnCount = BitConverter.ToUInt64(smartReadDataBuffer, 0x70);

                    var powerOnHours = BitConverter.ToUInt64(smartReadDataBuffer, 0x80);
                    storage.Smart.MeasuredPowerOnHours = powerOnHours;
                    storage.Smart.DetectedPowerOnHours = powerOnHours;

                    NVMeInterpreter.NVMeSmart(storage.Smart, smartReadDataBuffer);

                    storage.SmartKey = SmartKey.NVMe;

                    storage.Smart.DiskStatus = CheckDiskStatus(storage);

                    return;
                }
            }

            var attributeBuffer = new byte[InteropConstants.READ_ATTRIBUTE_BUFFER_SIZE];

            List<SmartAttributeStructure> smartAttributes = null;

            if (storage.Smart.Status.HasFlag(SmartStatus.IsSmartEnabled | SmartStatus.IsSmartCorrect))
            {
                switch (storage.Command)
                {
                    case COMMAND_TYPE.CMD_TYPE_PHYSICAL_DRIVE:
                        if (!SmartAttributeHandler.GetSmartAttributePd(storage, handle, storage.Target, attributeBuffer, out smartAttributes))
                        {
                            TryWakeUp(storage, handle);

                            if (!SmartAttributeHandler.GetSmartAttributePd(storage, handle, storage.Target, attributeBuffer, out smartAttributes))
                            {
                                return;
                            }
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_SCSI_MINIPORT:
                        if (!SmartAttributeHandler.GetSmartAttributeScsi(storage, handle, storage.Target, attributeBuffer, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_SILICON_IMAGE:
                        if (!SmartAttributeHandler.GetSmartAttributeSi(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_CSMI:
                        if (!SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, null, attributeBuffer, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_CSMI_PHYSICAL_DRIVE:
                        if (!SmartAttributeHandler.GetSmartAttributePd(storage, handle, storage.Target, attributeBuffer, out smartAttributes))
                        {
                            TryWakeUp(storage, handle);

                            if (!SmartAttributeHandler.GetSmartAttributePd(storage, handle, storage.Target, attributeBuffer, out smartAttributes))
                            {
                                if (!SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, null, attributeBuffer, out smartAttributes))
                                {
                                    return;
                                }
                                else
                                {
                                    storage.Command = COMMAND_TYPE.CMD_TYPE_CSMI;
                                }
                            }
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_SAT:
                    case COMMAND_TYPE.CMD_TYPE_SAT_ASM1352R:
                    case COMMAND_TYPE.CMD_TYPE_SUNPLUS:
                    case COMMAND_TYPE.CMD_TYPE_IO_DATA:
                    case COMMAND_TYPE.CMD_TYPE_LOGITEC:
                    case COMMAND_TYPE.CMD_TYPE_PROLIFIC:
                    case COMMAND_TYPE.CMD_TYPE_JMICRON:
                    case COMMAND_TYPE.CMD_TYPE_CYPRESS:
                        TryWakeUp(storage, handle);

                        if (!SmartAttributeHandler.GetSmartAttributeSat(storage, handle, storage.Target, attributeBuffer, storage.Command, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP:
                        TryWakeUp(storage, handle);

                        if (RealtekMethods.RealtekSwitchMode(storage, handle, true, 1))
                        {
                            if (!SmartAttributeHandler.GetSmartAttributeSat(storage, handle, storage.Target, attributeBuffer, storage.Command, out smartAttributes))
                            {
                                return;
                            }

                            RealtekMethods.RealtekSwitchMode(storage, handle, true, 0);

                            storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        }
                        break;
                    case COMMAND_TYPE.CMD_TYPE_WMI:
                        //Not supported
                        return;
                    case COMMAND_TYPE.CMD_TYPE_MEGARAID:
                        if (!SmartAttributeHandler.GetSmartAttributeMegaRAID(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_AMD_RC2:
                        if (!SmartAttributeHandler.GetSmartDataAMD_RC2(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_JMS56X:
                        if (!SmartAttributeHandler.GetSmartInfoJMS56X(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_JMB39X:
                        if (!SmartAttributeHandler.GetSmartInfoJMB39X(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_JMS586_40:
                        if (!SmartAttributeHandler.GetSmartInfoJMS586_40(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    case COMMAND_TYPE.CMD_TYPE_JMS586_20:
                        if (!SmartAttributeHandler.GetSmartInfoJMS586_20(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            return;
                        }

                        storage.Smart.DiskStatus = CheckDiskStatus(storage);
                        break;
                    default:
                        return;
                }

                if (smartAttributes != null)
                {
                    SmartAttributeHandler.CheckSmartAttributeUpdate(storage, smartAttributes);
                }
            }
        }

        internal static void TryWakeUp(Storage storage, IntPtr handle)
        {
            if (storage.ForceWakeup)
            {
                var buffer = new byte[512];

                Kernel32.SetFilePointerEx(handle, 0, IntPtr.Zero, 0);
                Kernel32.ReadFile(handle, buffer, (uint)buffer.Length, out _, IntPtr.Zero);
            }
        }

        internal static bool AddDiskNVMe(Storage storage, IntPtr handle, IdentifyDevice identifyDevice, COMMAND_TYPE command)
        {
            storage.Command = command;

            storage.Smart.Status |= SmartStatus.IsSmartSupported | SmartStatus.IsSmartEnabled | SmartStatus.IsSmartCorrect;

            var nvmeIdentifyDevice = identifyDevice.ToNVME();

            storage.Model        = Encoding.ASCII.GetString(nvmeIdentifyDevice.Model       ).Trim();
            storage.SerialNumber = Encoding.ASCII.GetString(nvmeIdentifyDevice.SerialNumber).Trim();
            storage.FirmwareRev  = Encoding.ASCII.GetString(nvmeIdentifyDevice.FirmwareRev ).Trim();

            storage.VendorID                  = nvmeIdentifyDevice.VendorID;
            storage.Smart.TemperatureWarning  = (int)TemperatureConverter.KelvinToCelsius(nvmeIdentifyDevice.WarningCompositeTemperature  == 0 ? 0x0157 : nvmeIdentifyDevice.WarningCompositeTemperature );
            storage.Smart.TemperatureCritical = (int)TemperatureConverter.KelvinToCelsius(nvmeIdentifyDevice.CriticalCompositeTemperature == 0 ? 0x015C : nvmeIdentifyDevice.CriticalCompositeTemperature);

            //Try to get vendor string
            var vendor = PCIIDReader.Vendors.FirstOrDefault(v => v.ID == storage.VendorID);
            if (vendor != null)
            {
                storage.Vendor = vendor.Name;
            }

            var binIdentifyDevice = identifyDevice.ToBIN();

            if ((binIdentifyDevice.Bin[520] & 0x4) != 0) //for Dataset Management Command support
            {
                storage.IsTrimSupported = true;
            }

            if ((binIdentifyDevice.Bin[525] & 0x1) != 0) // for Volatile Write Cache
            {
                storage.IsVolatileWriteCachePresent = true;
            }

            if (storage.Model.Length == 0)
            {
                return false;
            }

            var smartReadDataBuffer = new byte[InteropConstants.IDENTIFY_BUFFER_SIZE];

            if (GetSmartAttributes(storage, handle, command, smartReadDataBuffer))
            {
                storage.Smart.Temperature = smartReadDataBuffer[0x2] * 256 + smartReadDataBuffer[0x1] - 273;
                if (storage.Smart.Temperature == -273 || storage.Smart.Temperature > 100)
                {
                    storage.Smart.Temperature = null;
                }

                storage.Smart.Life = (sbyte)(100 - smartReadDataBuffer[0x05]);

                storage.Smart.HostReads  = (BitConverter.ToUInt64(smartReadDataBuffer, 0x20) * 1000) >> 21;
                storage.Smart.HostWrites = (BitConverter.ToUInt64(smartReadDataBuffer, 0x30) * 1000) >> 21;

                storage.Smart.PowerOnCount = BitConverter.ToUInt64(smartReadDataBuffer, 0x70);

                var powerOnHours = BitConverter.ToUInt64(smartReadDataBuffer, 0x80);
                storage.Smart.MeasuredPowerOnHours = powerOnHours;
                storage.Smart.DetectedPowerOnHours = powerOnHours;

                NVMeInterpreter.NVMeSmart(storage.Smart, smartReadDataBuffer);

                storage.SmartKey = SmartKey.NVMe;
            }

            storage.IsSSD = storage.IsNVMe = true;
            return true;
        }

        internal static bool AddDisk(Storage storage, IntPtr handle, byte target, COMMAND_TYPE commandType, IdentifyDevice identify, CSMI_SAS_PHY_ENTITY? sasPhyEntity = null)
        {
            LogSimple.LogTrace($"{nameof(AddDisk)} - with {nameof(COMMAND_TYPE)} = '{commandType}'.");

            storage.Command = commandType;
            storage.Target  = target;

            if (identify == null)
            {
                return false;
            }

            var ataIdentify = identify.ToATA();

            string commandTypeString = null;

            if (commandType == COMMAND_TYPE.CMD_TYPE_PHYSICAL_DRIVE
             || COMMAND_TYPE.CMD_TYPE_SAT <= commandType && commandType <= COMMAND_TYPE.CMD_TYPE_SAT_REALTEK9220DP)
            {
                if (target == 0xB0)
                {
                    commandTypeString = Mappings.CommandTypeStringMapping[commandType] + '2';
                }
                else
                {
                    commandTypeString = Mappings.CommandTypeStringMapping[commandType] + '1';
                }
            }
            else
            {
                if (commandType >= COMMAND_TYPE.CMD_TYPE_UNKNOWN && commandType <= COMMAND_TYPE.CMD_TYPE_DEBUG)
                {
                    commandTypeString = Mappings.CommandTypeStringMapping[commandType];
                }
                else
                {
                    commandTypeString = string.Empty;
                }
            }

            var smartReadDataBuffer = new byte[InteropConstants.IDENTIFY_BUFFER_SIZE];

            var serialNumber = Encoding.ASCII.GetString(ataIdentify.SerialNumber).Trim();
            var firmwareRev  = Encoding.ASCII.GetString(ataIdentify.FirmwareRev ).Trim();
            var model        = Encoding.ASCII.GetString(ataIdentify.Model       ).Trim();

            storage.SerialNumber = ByteHandler.ChangeByteOrder(serialNumber);
            storage.FirmwareRev  = ByteHandler.ChangeByteOrder(firmwareRev );
            storage.Model        = ByteHandler.ChangeByteOrder(model       );

            if (string.IsNullOrEmpty(storage.Model) || string.IsNullOrEmpty(storage.FirmwareRev))
            {
                LogSimple.LogWarn($"{nameof(storage.Model)} = '{storage.Model}' | {nameof(storage.FirmwareRev)} = '{storage.FirmwareRev}'");
                return false;
            }

            if (Storage.ModelStartsWith(storage, "ADATA SSD")
             && int.TryParse(storage.FirmwareRev, out var fw)
             && fw == 346)
            {
                storage.TemperatureMultiplier = 0.5f;
            }

            storage.Smart.Status = SmartStatus.Nothing;

            var ataInfo = new ATAInfo();

            uint major = 0;

            switch (commandType)
            {
                case COMMAND_TYPE.CMD_TYPE_AMD_RC2:
                    major = 3;
                    storage.IsSSD = (ataIdentify.SerialAtaCapabilities & 1) != 0;
                    storage.Smart.Status |= SmartStatus.IsSmartSupported;
                    break;
                case COMMAND_TYPE.CMD_TYPE_JMS56X:
                    storage.Smart.Status |= SmartStatus.IsSmartSupported;
                    break;
                case COMMAND_TYPE.CMD_TYPE_JMB39X:
                    storage.Smart.Status |= SmartStatus.IsSmartSupported;
                    break;
                case COMMAND_TYPE.CMD_TYPE_JMS586_40:
                    storage.Smart.Status |= SmartStatus.IsSmartSupported;
                    break;
                case COMMAND_TYPE.CMD_TYPE_JMS586_20:
                    storage.Smart.Status |= SmartStatus.IsSmartSupported;
                    break;
                default:
                    storage.Smart.Status |= SmartStatus.IsSmartSupported;

                    major = UtilityMethods.GetAtaMajorVersion(ataIdentify.MajorVersion);

                    ataInfo.TransferMode = UtilityMethods.GetTransferMode(ataIdentify.MultiWordDma,
                                                                          ataIdentify.SerialAtaCapabilities,
                                                                          ataIdentify.SerialAtaAdditionalCapabilities,
                                                                          ataIdentify.UltraDmaMode);
                    break;
            }

            storage.DetectedTimeUnitType = UtilityMethods.GetTimeUnitType(storage, major, ataInfo.TransferMode);

            if (storage.DetectedTimeUnitType == TimeUnitType.PowerOnMilliSeconds)
            {
                storage.MeasuredTimeUnitType = TimeUnitType.PowerOnMilliSeconds;
            }
            else if (storage.DetectedTimeUnitType == TimeUnitType.PowerOn10Minutes)
            {
                storage.MeasuredTimeUnitType = TimeUnitType.PowerOn10Minutes;
            }

            //Feature
            if (major >= 3 && (ataIdentify.CommandSetSupported1 & (1 << 0)) != 0)
            {
                storage.Smart.Status |= SmartStatus.IsSmartSupported;
            }

            //Add rest of features here, if necessary
            //[...]

            // "NominalMediaRotationRate" is supported by ATA8-ACS but a part of ATA/ATAPI-7 devices support storage field.
            if (major >= 7 && ataIdentify.NominalMediaRotationRate == 0x01)
            {
                storage.IsSSD = true;
                //asi.NominalMediaRotationRate = 1;
            }

            bool isIDInfoCorrect = false;
            bool isLba48Supported = false;
            bool is9126MB = false;

            //DiskSize & BufferSize
            if (ataIdentify.LogicalCylinders > 16383)
            {
                ataIdentify.LogicalCylinders = 16383;
                isIDInfoCorrect = true;
            }

            if (ataIdentify.LogicalHeads > 16)
            {
                ataIdentify.LogicalHeads = 16;
                isIDInfoCorrect = true;
            }

            if (ataIdentify.LogicalSectors > 63)
            {
                ataIdentify.LogicalSectors = 63;
                isIDInfoCorrect = true;
            }

            ataInfo.Cylinders = ataIdentify.LogicalCylinders;
            ataInfo.Heads     = ataIdentify.LogicalHeads;
            ataInfo.Sectors   = ataIdentify.LogicalSectors;
            ataInfo.Sector28  = 0x0FFFFFFF & ataIdentify.TotalAddressableSectors;
            ataInfo.Sector48  = 0x0000FFFFFFFFFFFF & ataIdentify.MaxUserLba;

            if ((ataIdentify.SectorSize & 0xC000) == 0x4000) // bit 14-15, bit14=1, bit15=0
            {
                if ((ataIdentify.SectorSize & 0x000F) == 0x3) // bit 0-3
                {
                    ataInfo.LogicalSectorSize = 512;
                    ataInfo.PhysicalSectorSize = 4096;
                }
                else if ((ataIdentify.SectorSize & 0x1000) == 0x1000) // bit 12=1
                {
                    if (ataIdentify.WordsPerLogicalSector == 256 || ataIdentify.WordsPerLogicalSector == 0)
                    {
                        ataInfo.LogicalSectorSize = 512;
                    }
                    else
                    {
                        ataInfo.LogicalSectorSize = (ushort)(ataIdentify.WordsPerLogicalSector * 2);
                    }
                }
            }

            if (ataInfo.PhysicalSectorSize < ataInfo.LogicalSectorSize)
            {
                ataInfo.PhysicalSectorSize = ataInfo.LogicalSectorSize;
            }

            if (ataIdentify.TotalAddressableSectors == 0x01100003) // 9126807040 bytes
            {
                is9126MB = true;
                ataInfo.DiskSizeChs = 0;
            }

            ulong numOfSectors = (ulong)(ataIdentify.LogicalCylinders * ataIdentify.LogicalHeads * ataIdentify.LogicalSectors);
            ulong diskSizeChsTemp = numOfSectors * 512;

            if (commandType == COMMAND_TYPE.CMD_TYPE_AMD_RC2)
            {
                isLba48Supported = true;
                ataInfo.DiskSizeChs = 0;
            }
            else if (commandType == COMMAND_TYPE.CMD_TYPE_JMS56X
                  || commandType == COMMAND_TYPE.CMD_TYPE_JMB39X
                  || commandType == COMMAND_TYPE.CMD_TYPE_JMS586_20
                  || commandType == COMMAND_TYPE.CMD_TYPE_JMS586_40)
            {
                isLba48Supported = true;
            }
            else if (ataIdentify.LogicalCylinders == 0
                  || ataIdentify.LogicalHeads     == 0
                  || ataIdentify.LogicalSectors   == 0)
            {
                // Realteck RTL9210 support (2024/01/19)
                if (ataIdentify.Capabilities1 == 0
                 && ataIdentify.Capabilities2 == 0
                 && commandType == COMMAND_TYPE.CMD_TYPE_SAT)
                {
                    return false;
                }
                else
                {
                    ataInfo.DiskSizeChs = 0;
                }
            }
            else if ((diskSizeChsTemp / 1000 / 1000) > 1000)
            {
                ataInfo.DiskSizeChs = (uint)(diskSizeChsTemp / 1000 / 1000 - 49);
            }
            else
            {
                ataInfo.DiskSizeChs = (uint)(diskSizeChsTemp / 1000 / 1000);
            }

            ataInfo.NumberOfSectors = numOfSectors;

            if (ataInfo.Sector28 > 0 && (ataInfo.Sector28 * 512 / 1000 / 1000) > 49)
            {
                ataInfo.DiskSizeLba28 = ataInfo.Sector28 * 512 / 1000 / 1000 - 49;
                ataInfo.NumberOfSectors = ataInfo.Sector28;
            }
            else
            {
                ataInfo.DiskSizeLba28 = 0;
            }

            if (isLba48Supported && (ataInfo.Sector48 * ataInfo.LogicalSectorSize / 1000 / 1000) > 49)
            {
                ataInfo.DiskSizeLba48 = ataInfo.Sector48 * ataInfo.LogicalSectorSize / 1000 / 1000 - 49;
                ataInfo.NumberOfSectors = ataInfo.Sector48;
            }
            else
            {
                ataInfo.DiskSizeLba48 = 0;
            }

            // Error Check for External ATA Controller
            if (isLba48Supported
             &&
                (
                    ataIdentify.TotalAddressableSectors < 268435455
                 && ataInfo.DiskSizeLba28 != ataInfo.DiskSizeLba48
                )
               )
            {
                ataInfo.DiskSizeLba48 = 0;
            }

            storage.ATAInfo = ataInfo;

            var attributeBuffer = new byte[InteropConstants.READ_ATTRIBUTE_BUFFER_SIZE];
            var thresholdBuffer = new byte[InteropConstants.READ_THRESHOLD_BUFFER_SIZE];

            List<SmartAttributeStructure> smartAttributes = null;
            List<SmartAttributeStructure> smartAttributesCheck = null;

            LogSimple.LogTrace($"{nameof(SmartStatus)} = {storage.Smart.Status}.");

            // Check S.M.A.R.T. Enabled or Disabled
            if (storage.Smart.Status.HasFlag(SmartStatus.IsSmartSupported) || is9126MB)
            {
                switch (commandType)
                {
                    case COMMAND_TYPE.CMD_TYPE_PHYSICAL_DRIVE:
                        if (SmartAttributeHandler.GetSmartAttributePd(storage, handle, target, attributeBuffer, out smartAttributes))
                        {
                            SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                            SmartAttributeHandler.GetSmartAttributePd(storage, handle, target, attributeBuffer, out smartAttributesCheck);

                            if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                            {
                                storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                            }

                            if (SmartAttributeHandler.GetSmartThresholdPd(storage, handle, target, thresholdBuffer, smartAttributes))
                            {
                                storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                            }

                            storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                        }

                        if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect) && SmartAttributeHandler.ControlSmartStatusPd(storage, handle, target, InteropConstants.ENABLE_SMART))
                        {
                            if (SmartAttributeHandler.GetSmartAttributePd(storage, handle, target, attributeBuffer, out smartAttributes))
                            {
                                SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                                SmartAttributeHandler.GetSmartAttributePd(storage, handle, target, attributeBuffer, out smartAttributesCheck);

                                if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                                {
                                    storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                                }

                                if (SmartAttributeHandler.GetSmartThresholdPd(storage, handle, target, thresholdBuffer, smartAttributes))
                                {
                                    storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                                }

                                storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                            }
                        }

                        if (attributeBuffer.SequenceEqual(thresholdBuffer) && storage.ATAInfo.VendorID != VendorIDs.SSDVendorIndilinx)
                        {
                            storage.Smart.Status &= ~(SmartStatus.IsSmartEnabled | SmartStatus.IsSmartCorrect | SmartStatus.IsThresholdCorrect);
                        }

                        break;
                    case COMMAND_TYPE.CMD_TYPE_SCSI_MINIPORT:
                        if (SmartAttributeHandler.GetSmartAttributeScsi(storage, handle, target, attributeBuffer, out smartAttributes))
                        {
                            SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                            SmartAttributeHandler.GetSmartAttributeScsi(storage, handle, target, attributeBuffer, out smartAttributesCheck);

                            if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                            {
                                storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                            }

                            if (SmartAttributeHandler.GetSmartThresholdScsi(storage, handle, target, thresholdBuffer, smartAttributes))
                            {
                                storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                            }

                            storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                        }

                        if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect) && SmartAttributeHandler.ControlSmartStatusScsi(storage, handle, target, InteropConstants.ENABLE_SMART))
                        {
                            if (SmartAttributeHandler.GetSmartAttributeScsi(storage, handle, target, attributeBuffer, out smartAttributes))
                            {
                                SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                                SmartAttributeHandler.GetSmartAttributeScsi(storage, handle, target, attributeBuffer, out smartAttributesCheck);

                                if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                                {
                                    storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                                }

                                if (SmartAttributeHandler.GetSmartThresholdScsi(storage, handle, target, thresholdBuffer, smartAttributes))
                                {
                                    storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                                }

                                storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                            }
                        }
                        break;
                    case COMMAND_TYPE.CMD_TYPE_SILICON_IMAGE:
                        if (SmartAttributeHandler.GetSmartAttributeSi(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                            SmartAttributeHandler.GetSmartAttributeSi(storage, handle, attributeBuffer, out smartAttributesCheck);

                            if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                            {
                                storage.Smart.Status |= SmartStatus.IsSmartCorrect;

                                //Compare Si and Pd
                                SmartAttributeHandler.GetSmartAttributePd(storage, handle, 0xA0, attributeBuffer, out smartAttributesCheck);

                                if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                                {
                                    //Does not support GetSmartThresholdSi
                                    SmartAttributeHandler.GetSmartThresholdPd(storage, handle, 0xA0, thresholdBuffer, smartAttributes);

                                    storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                                }
                            }

                            storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                        }
                        break;
                    case COMMAND_TYPE.CMD_TYPE_CSMI:
                        if (SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, sasPhyEntity, attributeBuffer, out smartAttributes))
                        {
                            SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                            SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, sasPhyEntity, attributeBuffer, out smartAttributesCheck);

                            if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                            {
                                storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                            }

                            if (SmartAttributeHandler.GetSmartThresholdCsmi(storage, handle, sasPhyEntity, thresholdBuffer, smartAttributes))
                            {
                                storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                            }

                            storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                        }

                        if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect) && SmartAttributeHandler.ControlSmartStatusCsmi(storage, handle, sasPhyEntity, InteropConstants.ENABLE_SMART))
                        {
                            if (SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, sasPhyEntity, attributeBuffer, out smartAttributes))
                            {
                                SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                                SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, sasPhyEntity, attributeBuffer, out smartAttributesCheck);

                                if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                                {
                                    storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                                }

                                if (SmartAttributeHandler.GetSmartThresholdCsmi(storage, handle, sasPhyEntity, thresholdBuffer, smartAttributes))
                                {
                                    storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                                }

                                storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                            }
                        }
                        break;
                    case COMMAND_TYPE.CMD_TYPE_CSMI_PHYSICAL_DRIVE:
                        if (SmartAttributeHandler.GetSmartAttributePd(storage, handle, target, attributeBuffer, out smartAttributes))
                        {
                            SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                            SmartAttributeHandler.GetSmartAttributePd(storage, handle, target, attributeBuffer, out smartAttributesCheck);

                            if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                            {
                                storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                            }

                            if (SmartAttributeHandler.GetSmartThresholdPd(storage, handle, target, thresholdBuffer, smartAttributes))
                            {
                                storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                            }

                            storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                        }

                        if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartEnabled)
                         || !storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect)
                         || !storage.Smart.Status.HasFlag(SmartStatus.IsThresholdCorrect))
                        {
                            if (SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, sasPhyEntity, attributeBuffer, out smartAttributes))
                            {
                                SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                                SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, sasPhyEntity, attributeBuffer, out smartAttributesCheck);

                                if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                                {
                                    storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                                }

                                if (SmartAttributeHandler.GetSmartThresholdCsmi(storage, handle, sasPhyEntity, thresholdBuffer, smartAttributes))
                                {
                                    storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                                }

                                storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                            }

                            if (storage.Smart.Status.HasFlag(SmartStatus.IsSmartEnabled)
                             && storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect)
                             && storage.Smart.Status.HasFlag(SmartStatus.IsThresholdCorrect))
                            {
                                commandType = COMMAND_TYPE.CMD_TYPE_CSMI;
                            }

                            if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect) && SmartAttributeHandler.ControlSmartStatusCsmi(storage, handle, sasPhyEntity, InteropConstants.ENABLE_SMART))
                            {
                                if (SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, sasPhyEntity, attributeBuffer, out smartAttributes))
                                {
                                    SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                                    SmartAttributeHandler.GetSmartAttributeCsmi(storage, handle, sasPhyEntity, attributeBuffer, out smartAttributesCheck);

                                    if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                                    {
                                        storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                                    }

                                    if (SmartAttributeHandler.GetSmartThresholdCsmi(storage, handle, sasPhyEntity, thresholdBuffer, smartAttributes))
                                    {
                                        storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                                    }

                                    storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                                    commandType = COMMAND_TYPE.CMD_TYPE_CSMI;
                                }
                            }
                        }
                        break;
                    case COMMAND_TYPE.CMD_TYPE_SAT:
                    case COMMAND_TYPE.CMD_TYPE_SAT_ASM1352R:
                    case COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP:
                    case COMMAND_TYPE.CMD_TYPE_SUNPLUS:
                    case COMMAND_TYPE.CMD_TYPE_IO_DATA:
                    case COMMAND_TYPE.CMD_TYPE_LOGITEC:
                    case COMMAND_TYPE.CMD_TYPE_PROLIFIC:
                    case COMMAND_TYPE.CMD_TYPE_JMICRON:
                    case COMMAND_TYPE.CMD_TYPE_CYPRESS:
                        if (SmartAttributeHandler.GetSmartAttributeSat(storage, handle, target, attributeBuffer, commandType, out smartAttributes))
                        {
                            SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                            SmartAttributeHandler.GetSmartAttributeSat(storage, handle, target, attributeBuffer, commandType, out smartAttributesCheck);

                            if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                            {
                                storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                            }

                            if (SmartAttributeHandler.GetSmartThresholdSat(storage, handle, target, thresholdBuffer, commandType, smartAttributes))
                            {
                                storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                            }

                            storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                        }

                        if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect) && SmartAttributeHandler.ControlSmartStatusSat(storage, handle, target, InteropConstants.ENABLE_SMART, commandType))
                        {
                            if (SmartAttributeHandler.GetSmartAttributeSat(storage, handle, target, attributeBuffer, commandType, out smartAttributes))
                            {
                                SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                                SmartAttributeHandler.GetSmartAttributeSat(storage, handle, target, attributeBuffer, commandType, out smartAttributesCheck);

                                if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                                {
                                    storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                                }

                                if (SmartAttributeHandler.GetSmartThresholdSat(storage, handle, target, thresholdBuffer, commandType, smartAttributes))
                                {
                                    storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                                }

                                storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                            }
                        }
                        break;
                    //case COMMAND_TYPE.CMD_TYPE_WMI: //WMI is unsupported
                        //break;
                    case COMMAND_TYPE.CMD_TYPE_MEGARAID:
                        if (SmartAttributeHandler.GetSmartAttributeMegaRAID(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                            SmartAttributeHandler.GetSmartAttributeMegaRAID(storage, handle, attributeBuffer, out smartAttributesCheck);

                            if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                            {
                                storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                            }

                            if (SmartAttributeHandler.GetSmartThresholdMegaRAID(storage, handle, thresholdBuffer, smartAttributes))
                            {
                                storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                            }

                            storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                        }

                        if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect) && SmartAttributeHandler.ControlSmartStatusMegaRAID(storage, handle, InteropConstants.ENABLE_SMART))
                        {
                            if (SmartAttributeHandler.GetSmartAttributeMegaRAID(storage, handle, attributeBuffer, out smartAttributes))
                            {
                                SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                                SmartAttributeHandler.GetSmartAttributeMegaRAID(storage, handle, attributeBuffer, out smartAttributesCheck);

                                if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                                {
                                    storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                                }

                                if (SmartAttributeHandler.GetSmartThresholdMegaRAID(storage, handle, thresholdBuffer, smartAttributes))
                                {
                                    storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                                }

                                storage.Smart.Status |= SmartStatus.IsSmartEnabled;
                            }
                        }
                        break;
                    case COMMAND_TYPE.CMD_TYPE_AMD_RC2:
                        if (SmartAttributeHandler.GetSmartDataAMD_RC2(storage, handle, attributeBuffer, out smartAttributes))
                        {
                            SSDChecker.CheckSSDSupport(storage, ref ataIdentify, attributeBuffer, smartAttributes);
                            SmartAttributeHandler.GetSmartDataAMD_RC2(storage, handle, attributeBuffer, out smartAttributesCheck);

                            if (SmartAttributeHandler.CheckSmartAttributeCorrect(smartAttributes, smartAttributesCheck))
                            {
                                storage.Smart.Status |= SmartStatus.IsSmartCorrect;
                            }

                            if (SmartAttributeHandler.GetSmartThresholdAMD_RC2(storage, handle, thresholdBuffer))
                            {
                                storage.Smart.Status |= SmartStatus.IsThresholdCorrect;
                            }

                            storage.Smart.Status |= SmartStatus.IsSmartSupported | SmartStatus.IsSmartEnabled;
                        }
                        break;
                    case COMMAND_TYPE.CMD_TYPE_JMS56X:
                        //Support could be added later
                        break;
                    case COMMAND_TYPE.CMD_TYPE_JMB39X:
                        //Support could be added later
                        break;
                    case COMMAND_TYPE.CMD_TYPE_JMS586_40:
                        //Support could be added later
                        break;
                    case COMMAND_TYPE.CMD_TYPE_JMS586_20:
                        //Support could be added later
                        break;
                    default:
                        return false;
                }

                //Set all attributes
                if (storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect) && smartAttributes != null)
                {
                    SmartAttributeHandler.SetAttributes(storage, smartAttributes);
                }
            }

            //Might have changed in switch
            storage.Command = commandType;

            // OCZ-VERTEX3 2.02 Firmware Bug
            // OCZ-VERTEX2 1.27 Firmware Bug
            if (ataInfo.VendorID == VendorIDs.SSDVendorSandforce
             && ( Storage.ModelStartsWith(storage, "OCZ-VERTEX3") && storage.FirmwareRev.StartsWith("2.02") )
             || ( Storage.ModelStartsWith(storage, "OCZ-VERTEX2") && storage.FirmwareRev.StartsWith("1.27") )
               )
            {
                storage.Smart.Status |= SmartStatus.IsThresholdBug;
            }
            // SSD G2 Series Firmware Bug
            else if (Storage.ModelStartsWith(storage, "SSD G2 Series") && storage.FirmwareRev.StartsWith("3.6.5"))
            {
                storage.Smart.Status |= SmartStatus.IsThresholdBug;
            }

            if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect))
            {
                storage.Smart.Clear();
            }

            //Workaround for Intel SSD
            if (Storage.ModelStartsWith(storage, "Intel") && storage.Smart.MeasuredPowerOnHours > 0x0DA753)
            {
                storage.Smart.DetectedPowerOnHours -= 0x0DA753;
                storage.Smart.MeasuredPowerOnHours -= 0x0DA753;
            }

            if (Storage.ModelStartsWith(storage, "JMicron RAlD"))
            {
                return false;
            }

            if (isIDInfoCorrect && commandType >= COMMAND_TYPE.CMD_TYPE_SAT)
            {
                return false;
            }

            if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect))
            {
                return false;
            }

            return true;
        }

        #endregion

        #region Private

        static bool GetSmartAttributes(Storage storage, IntPtr handle, COMMAND_TYPE command, byte[] buffer)
        {
            return
                ((!Storage.IsARM && command == COMMAND_TYPE.CMD_TYPE_AMD_RC2 && SmartAttributeHandler.GetSmartDataAMD_RC2(storage, handle, buffer, out var attr))
             || (Storage.HasNVMeStorageQuery && command == COMMAND_TYPE.CMD_TYPE_NVME_STORAGE_QUERY && NVMeSmartAttributes.GetSmartAttributeNVMeStorageQuery(storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_NVME_INTEL         && NVMeSmartAttributes.GetSmartAttributeNVMeIntel        (storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_NVME_INTEL_RST     && NVMeSmartAttributes.GetSmartAttributeNVMeIntelRst     (storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_NVME_INTEL_VROC    && NVMeSmartAttributes.GetSmartAttributeNVMeIntelVroc    (storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_NVME_SAMSUNG       && NVMeSmartAttributes.GetSmartAttributeNVMeSamsung      (storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_NVME_SAMSUNG       && NVMeSmartAttributes.GetSmartAttributeNVMeSamsung951   (storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_JMICRON            && NVMeSmartAttributes.GetSmartAttributeNVMeJMicron      (storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_NVME_ASMEDIA       && NVMeSmartAttributes.GetSmartAttributeNVMeASMedia      (storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_NVME_REALTEK       && NVMeSmartAttributes.GetSmartAttributeNVMeRealtek      (storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP && NVMeSmartAttributes.GetSmartAttributeNVMeRealtek9220DP(storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_JMS586_40          && NVMeSmartAttributes.GetSmartAttributeNVMeJMS586_40    (storage, handle, buffer))
             || (command == COMMAND_TYPE.CMD_TYPE_JMS586_20          && NVMeSmartAttributes.GetSmartAttributeNVMeJMS586_20    (storage, handle, buffer))
                );
        }

        static DiskStatus CheckDiskStatus(Storage storage)
        {
            var smartAttributes = storage.Smart.SmartAttributes;

            if (smartAttributes == null
             || smartAttributes.Count == 0)
            {
                return DiskStatus.Unknown;
            }

            if (storage.IsNVMe)
            {
                if (Storage.ModelStartsWith(storage, "Parallels")
                 || Storage.ModelStartsWith(storage, "VMware")
                 || Storage.ModelStartsWith(storage, "QEMU"))
                {
                    return DiskStatus.Unknown;
                }

                var crit = GetAttribute(smartAttributes, SmartAttributeType.CriticalWarning);
                if (crit != null
                 && crit.Attribute.RawValue[0] > 0)
                {
                    return DiskStatus.Bad;
                }

                var spare          = GetAttribute(smartAttributes, SmartAttributeType.AvailableSpare);
                var spareThreshold = GetAttribute(smartAttributes, SmartAttributeType.AvailableSpareThreshold);

                if (spareThreshold != null
                 && spareThreshold.Attribute.RawValue[0] == 0
                 || spareThreshold.Attribute.RawValue[0] > 100)
                {
                    //Empty
                }
                else if (spare != null && spareThreshold != null
                      && spare.Attribute.RawValue[0] < spareThreshold.Attribute.RawValue[0])
                {
                    return DiskStatus.Bad;
                }
                else if (spare != null && spareThreshold != null
                      && spare.Attribute.RawValue[0] == spareThreshold.Attribute.RawValue[0]
                      && spareThreshold.Attribute.RawValue[0] != 100)
                {
                    return DiskStatus.Caution;
                }

                if (storage.Smart.Life > 0)
                {
                    return DiskStatus.Good;
                }
                else if (storage.Smart.Life <= 0)
                {
                    return DiskStatus.Caution;
                }
            }

            if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect))
            {
                return DiskStatus.Unknown;
            }
            else if (!storage.IsSSD
                  && !storage.Smart.Status.HasFlag(SmartStatus.IsThresholdCorrect)) //HDD
            {
                return DiskStatus.Unknown;
            }
            else if (storage.Smart.Status.HasFlag(SmartStatus.IsThresholdBug))
            {
                return DiskStatus.Unknown;
            }

            int error = 0;
            int caution = 0;
            bool flagUnknown = true;

            foreach (var attribute in smartAttributes)
            {
                //Read Error Rate Bug
                if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandforce
                 && attribute.Info.ID == 0x01
                 && attribute.Attribute.CurrentValue == 0
                 && attribute.Attribute.RawValue[0] == 0
                 && attribute.Attribute.RawValue[1] == 0)
                {
                    //Empty
                }
                //[2021/12/15] Workaround for SanDisk USB Memory
                else if (attribute.Info.ID == 0xE8
                      && storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeSanDiskUsbMemory))
                {
                    //Empty
                }
                //Temperature Threshold Bug
                else if (attribute.Info.ID == 0xC2)
                {
                    //Empty
                }
                else if (storage.IsSSD && IsRawValues8(storage))
                {
                    //Empty
                }
                else if (storage.IsSSD && !IsRawValues8(storage)
                      && attribute.Attribute.Threshold != 0
                      && attribute.Attribute.CurrentValue < attribute.Attribute.Threshold)
                {
                    ++error;
                }
                else if (
                    (
                        (0x01 <= attribute.Info.ID && attribute.Info.ID <= 0x0D)
                     || attribute.Info.ID == 0x16
                     || (0xBB <= attribute.Info.ID && attribute.Info.ID <= 0xBD)
                     || (0xBF <= attribute.Info.ID && attribute.Info.ID <= 0xC1)
                     || (0xC3 <= attribute.Info.ID && attribute.Info.ID <= 0xD1)
                     || (0xD3 <= attribute.Info.ID && attribute.Info.ID <= 0xD4)
                     || (0xDC <= attribute.Info.ID && attribute.Info.ID <= 0xE4)
                     || (0xE6 <= attribute.Info.ID && attribute.Info.ID <= 0xE7)
                     || attribute.Info.ID == 0xF0
                     || attribute.Info.ID == 0xFA
                     || attribute.Info.ID == 0xFE
                    )
                  && attribute.Attribute.Threshold != 0
                  && attribute.Attribute.CurrentValue < attribute.Attribute.Threshold
                  )
                {
                    ++error;
                }

                if (storage.IsSSD && attribute.Attribute.Threshold != 0)
                {
                    flagUnknown = false;
                }

                if (attribute.Info.ID == 0x05 //Reallocated Sectors Count
                 && attribute.Info.ID == 0xC5 //Current Pending Sector Count
                 && attribute.Info.ID == 0xC6) //Offline Scan Uncorrectable Sector Count
                {
                    if (attribute.Attribute.RawValue[0] == 0xFF
                     && attribute.Attribute.RawValue[1] == 0xFF
                     && attribute.Attribute.RawValue[2] == 0xFF
                     && attribute.Attribute.RawValue[3] == 0xFF)
                    {
                        //Empty
                    }
                    else
                    {
                        if (attribute.Attribute.Threshold > 0
                         && attribute.Attribute.RawValueUInt >= attribute.Attribute.Threshold
                         && !storage.IsSSD)
                        {
                            ++caution;
                        }
                    }

                    if (!storage.IsSSD)
                    {
                        flagUnknown = false;
                    }
                }
                else if
                (
                    (
                        attribute.Info.ID == 0xA9 //Remaining life (or similar name)
                     && (
                            storage.ATAInfo.VendorID == VendorIDs.SSDVendorRealtek
                         || (storage.ATAInfo.VendorID == VendorIDs.SSDVendorKingston && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWrites32MB)
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotion
                        )
                    )
                 || (attribute.Info.Type == SmartAttributeType.TotalEraseCount   && storage.ATAInfo.VendorID == VendorIDs.SSDVendorKioxia )
                 || (attribute.Info.Type == SmartAttributeType.WearLevelingCount && storage.ATAInfo.VendorID == VendorIDs.SSDVendorSamsung)
                 || (attribute.Info.Type == SmartAttributeType.TotalEraseCount   && storage.ATAInfo.VendorID == VendorIDs.SSDVendorMtron  )
                 || (
                        attribute.Info.ID == 0xCA //Lifetime used / Remaining life
                     && (
                            storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicron
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicronMU03
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntelDC
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotionCVC
                        )
                    )
                 || (attribute.Info.Type == SmartAttributeType.RemainingLife && storage.ATAInfo.VendorID == VendorIDs.SSDVendorIndilinx)
                 || (
                        attribute.Info.ID == 0xE7 //Remaining life (or similar name)
                     && (
                            storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandforce
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorCorsair
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorKingston
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSkhynix
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorRealtek
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandisk
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSSSTC
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorAPACER
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorPhison
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorJMicron
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMaxiotek
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorYMTC
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSCY
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorRecadata
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorAdataIndustrial
                        )
                    )
                 || (attribute.Info.Type == SmartAttributeType.AvailableReservedSpace && storage.ATAInfo.VendorID == VendorIDs.SSDVendorPlextor)
                 || (
                        attribute.Info.ID == 0xE9 //Remaining life (or similar name)
                     && (
                            storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntel
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorOcz
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorOczVector
                         || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSkhynix
                        )
                    )
                 || (attribute.Info.Type == SmartAttributeType.MediaWearoutIndicator && storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovoHelenVenus)
                )
                {
                    if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeRawValueIncrement))
                    {
                        storage.Smart.Life = (sbyte)(100 - attribute.Attribute.RawValue[0]);
                    }
                    else if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeRawValue))
                    {
                        storage.Smart.Life = (sbyte)(attribute.Attribute.RawValue[0]);
                    }
                    else
                    {
                        storage.Smart.Life = (sbyte)attribute.Attribute.CurrentValue;
                    }

                    if (storage.Smart.Life > 100)
                    {
                        storage.Smart.Life = 100;
                    }

                    if (storage.Smart.Life < 0)
                    {
                        //Empty
                    }
                    else if (storage.Smart.Life == 0
                          || (
                                 storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeRawValue)
                              || storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeRawValueIncrement)
                             )
                          && storage.Smart.Life < attribute.Attribute.Threshold)
                    {
                        ++error;
                    }
                }
                else if (attribute.Info.ID == 0xE6 //Wearout
                      && (
                             storage.ATAInfo.VendorID == VendorIDs.SSDVendorWdc
                         ))
                {
                    if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeSanDisk0_1))
                    {
                        var temp = (sbyte)(attribute.Attribute.RawValue[1] * 256 + attribute.Attribute.RawValue[0]);
                        storage.Smart.Life = (sbyte)(100 - (temp == 0 ? 0 : temp / 100));
                    }
                    else if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeSanDisk1))
                    {
                        storage.Smart.Life = (sbyte)(100 - attribute.Attribute.RawValue[1]);
                    }
                    else
                    {
                        storage.Smart.Life = (sbyte)(100 - attribute.Attribute.RawValue[1]);
                    }

                    if (storage.Smart.Life < 0)
                    {
                        storage.Smart.Life = 0;
                    }

                    if (storage.Smart.Life > 100)
                    {
                        storage.Smart.Life = 100;
                    }

                    if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeSanDiskUsbMemory))
                    {
                        //Empty
                    }
                    else if (storage.Smart.Life == 0)
                    {
                        ++error;
                    }
                }
                else if
                (
                    (
                         attribute.Info.ID == 0xE6 //Wearout
                      && (
                             storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovo
                          || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskDell
                         )
                    )
                 || (
                         attribute.Info.ID == 0xC9 //Remaining life (or similar name)
                      && (
                             storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHP
                          || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHPVenus
                         )
                    )
                )
                {
                    flagUnknown = false;

                    if (storage.Smart.Life < 0)
                    {
                        storage.Smart.Life = 0;
                    }

                    if (storage.Smart.Life > 100)
                    {
                        storage.Smart.Life = 100;
                    }

                    if (storage.Smart.Life == 0)
                    {
                        ++error;
                    }
                }
            }

            if (error > 0)
            {
                return DiskStatus.Bad;
            }
            else if (flagUnknown)
            {
                return DiskStatus.Unknown;
            }
            else if (caution > 0)
            {
                return DiskStatus.Caution;
            }
            else
            {
                return DiskStatus.Good;
            }
        }

        static SmartAttribute GetAttribute(List<SmartAttribute> smartAttributes, SmartAttributeType smartAttributeType)
        {
            return smartAttributes.Find(sa => sa.Info.Type == smartAttributeType);
        }

        static bool IsRawValues8(Storage storage)
        {
            return storage.ATAInfo.VendorID.AnyOf(VendorIDs.SSDVendorJMicron60X, VendorIDs.SSDVendorIndilinx);
        }

        #endregion
    }
}
