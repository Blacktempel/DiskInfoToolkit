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
using DiskInfoToolkit.Disk;
using DiskInfoToolkit.Enums;
using DiskInfoToolkit.Enums.Interop;
using DiskInfoToolkit.HardDrive;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Interop.Enums;
using DiskInfoToolkit.Interop.Realtek;
using DiskInfoToolkit.Interop.Structures;
using DiskInfoToolkit.NVMe;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.Identifiers
{
    internal static class DeviceIdentifier
    {
        #region Internal

        internal static bool IdentifyDisk(Storage storage, IntPtr handle)
        {
            IdentifyDevice identifyDevice = null;

            try
            {
                if (storage.BusType.Any(StorageBusType.BusTypeUnknown, StorageBusType.BusTypeAta, StorageBusType.BusTypeSata))
                {
                    if (storage.SiliconImageType != 0)
                    {
                        DiskHandler.WakeUp(handle);

                        if (DoIdentifyDeviceSi(storage, handle, 0, out identifyDevice))
                        {
                            return DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SILICON_IMAGE, identifyDevice);
                        }
                    }

                    if (storage.DriveNumber >= 0)
                    {
                        if (!DoIdentifyDevicePd(storage, handle, 0xA0, out identifyDevice))
                        {
                            DiskHandler.WakeUp(handle);

                            if (!DoIdentifyDevicePd(storage, handle, 0xB0, out identifyDevice))
                            {
                                if (!DoIdentifyDevicePd(storage, handle, 0xB0, out identifyDevice))
                                {
                                    if (storage.StorageControllerType.Any(StorageControllerType.Nvidia, StorageControllerType.Marvell)
                                     && DoIdentifyDeviceScsi(storage, handle, out identifyDevice))
                                    {
                                        return DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SCSI_MINIPORT, identifyDevice);
                                    }
                                    else
                                    {
                                        return false;
                                    }
                                }
                            }
                        }

                        return DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_PHYSICAL_DRIVE, identifyDevice);
                    }
                    else
                    {
                        if (DoIdentifyDeviceScsi(storage, handle, out identifyDevice))
                        {
                            return DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SCSI_MINIPORT, identifyDevice);
                        }
                    }
                }
                else if (storage.BusType == StorageBusType.BusTypeNvme)
                {
                    if (Storage.HasNVMeStorageQuery && NVMeSmartIdentifier.DoIdentifyDeviceNVMeStorageQuery(storage, handle, out identifyDevice))
                    {
                        if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_STORAGE_QUERY))
                        {
                            return true;
                        }
                    }

                    if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeIntelVroc(storage, handle, out identifyDevice))
                    {
                        if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_INTEL_VROC))
                        {
                            return true;
                        }
                    }

                    if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeIntelRst(storage, handle, out identifyDevice))
                    {
                        if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_INTEL_RST))
                        {
                            return true;
                        }
                    }

                    if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeSamsung(storage, handle, out identifyDevice))
                    {
                        if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_SAMSUNG))
                        {
                            return true;
                        }
                    }

                    if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeIntel(storage, handle, out identifyDevice))
                    {
                        if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_INTEL))
                        {
                            return true;
                        }
                    }
                }

                if (storage.DriveNumber >= 0)
                {
                    DiskHandler.WakeUp(handle);

                    if (storage.BusType == StorageBusType.BusTypeUsb && storage.VendorID == (ushort)VendorIDs.USB_VENDOR_LOGITEC && storage.ProductID == 0x00D9)
                    {
                        return false;
                    }
                    if (storage.BusType == StorageBusType.BusTypeUsb && storage.VendorID == 0x05E3 && storage.ProductID == 0x0702)
                    {
                        return false;
                    }

                    if (storage.BusType == StorageBusType.BusTypeUsb && storage.VendorID == (ushort)VendorIDs.USB_VENDOR_JMICRON)
                    {
                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_JMICRON, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_JMICRON, identifyDevice))
                            {
                                return true;
                            }
                        }
                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT, identifyDevice))
                            {
                                return true;
                            }
                        }
                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_JMICRON, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_JMICRON, identifyDevice))
                            {
                                return true;
                            }
                        }
                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SAT, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SAT, identifyDevice))
                            {
                                return true;
                            }
                        }
                        if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeJMicron(storage, handle, out identifyDevice))
                        {
                            if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_JMICRON))
                            {
                                return true;
                            }
                        }

                        return false;
                    }

                    if (storage.BusType == StorageBusType.BusTypeUsb && storage.VendorID == (ushort)VendorIDs.USB_VENDOR_REALTEK)
                    {
                        if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeRealtek(storage, handle, out identifyDevice))
                        {
                            var ok = DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_REALTEK);

                            if (RealtekMethods.RealtekRAIDMode(storage, handle))
                            {
                                if (RealtekMethods.RealtekSwitchMode(storage, handle, true, 1))
                                {
                                    if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeRealtek(storage, handle, out identifyDevice))
                                    {
                                        ok = DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP);
                                    }

                                    RealtekMethods.RealtekSwitchMode(storage, handle, true, 0);
                                }
                            }

                            if (ok)
                            {
                                return true;
                            }
                        }
                    }

                    if (storage.BusType == StorageBusType.BusTypeUsb && storage.VendorID == (ushort)VendorIDs.USB_VENDOR_IO_DATA && storage.ProductID == 0x0122)
                    {
                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_JMICRON, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_JMICRON, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_JMICRON, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_JMICRON, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SAT, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SAT, identifyDevice))
                            {
                                return true;
                            }
                        }
                    }

                    if (storage.BusType == StorageBusType.BusTypeUsb && storage.VendorID == (ushort)VendorIDs.USB_VENDOR_IO_DATA)
                    {
                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_IO_DATA, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_IO_DATA, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_IO_DATA, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_IO_DATA, identifyDevice))
                            {
                                return true;
                            }
                        }
                    }

                    if (storage.BusType == StorageBusType.BusTypeUsb && storage.VendorID == (ushort)VendorIDs.USB_VENDOR_SUNPLUS)
                    {
                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SUNPLUS, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SUNPLUS, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SUNPLUS, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SUNPLUS, identifyDevice))
                            {
                                return true;
                            }
                        }
                    }

                    if (storage.BusType == StorageBusType.BusTypeUsb && storage.VendorID == (ushort)VendorIDs.USB_VENDOR_CYPRESS)
                    {
                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_CYPRESS, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_CYPRESS, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_CYPRESS, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_CYPRESS, identifyDevice))
                            {
                                return true;
                            }
                        }
                    }

                    if (storage.BusType == StorageBusType.BusTypeUsb &&
                        (storage.VendorID == (ushort)VendorIDs.USB_VENDOR_INITIO || storage.VendorID == (ushort)VendorIDs.USB_VENDOR_OXFORD))
                    {
                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SAT, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SAT, identifyDevice))
                            {
                                return true;
                            }
                        }
                    }

                    if (storage.BusType == StorageBusType.BusTypeUsb && storage.IsNVMe)
                    {
                        if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeJMicron(storage, handle, out identifyDevice))
                        {
                            if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_JMICRON))
                            {
                                return true;
                            }
                        }

                        if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeASMedia(storage, handle, out identifyDevice))
                        {
                            if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_ASMEDIA))
                            {
                                return true;
                            }
                        }

                        if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeRealtek(storage, handle, out identifyDevice))
                        {
                            var ok = DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_REALTEK);

                            if (RealtekMethods.RealtekRAIDMode(storage, handle))
                            {
                                if (RealtekMethods.RealtekSwitchMode(storage, handle, true, 1))
                                {
                                    if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeRealtek(storage, handle, out identifyDevice))
                                    {
                                        ok = DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP);
                                    }

                                    RealtekMethods.RealtekSwitchMode(storage, handle, true, 0);
                                }
                            }

                            if (ok)
                            {
                                return true;
                            }
                        }
                    }

                    if (true)
                    {
                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT, out identifyDevice))
                        {
                            bool ok = DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT, identifyDevice);

                            var ata = identifyDevice.ToATA();

                            var modelStr = Encoding.ASCII.GetString(ata.Model);

                            // for Buffalo SSD-SCTU3A
                            if (ok && modelStr.Contains("SSD-SCTU3A", StringComparison.OrdinalIgnoreCase)
                             && NVMeSmartIdentifier.DoIdentifyDeviceNVMeASMedia(storage, handle, out identifyDevice))
                            {
                                if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_ASMEDIA))
                                {
                                    return true;
                                }
                            }

                            // for ASM1352R
                            if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT_ASM1352R, out identifyDevice))
                            {
                                ok = DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT_ASM1352R, identifyDevice);
                            }

                            if (RealtekMethods.IsRealtekProduct(storage, handle)
                             && RealtekMethods.RealtekRAIDMode(storage, handle))
                            {
                                if (RealtekMethods.RealtekSwitchMode(storage, handle, true, 1))
                                {
                                    if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT_REALTEK9220DP, out identifyDevice))
                                    {
                                        ok = DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SAT_REALTEK9220DP, identifyDevice);
                                    }

                                    RealtekMethods.RealtekSwitchMode(storage, handle, true, 1);
                                }
                            }

                            if (ok)
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_JMICRON, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_JMICRON, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SUNPLUS, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_SUNPLUS, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_CYPRESS, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_CYPRESS, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_LOGITEC, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_LOGITEC, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_PROLIFIC, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xA0, COMMAND_TYPE.CMD_TYPE_PROLIFIC, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SAT, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SAT, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_JMICRON, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_JMICRON, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SUNPLUS, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_SUNPLUS, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_CYPRESS, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_CYPRESS, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_LOGITEC, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_LOGITEC, identifyDevice))
                            {
                                return true;
                            }
                        }

                        if (DoIdentifyDeviceSat(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_PROLIFIC, out identifyDevice))
                        {
                            if (DiskHandler.AddDisk(storage, handle, 0xB0, COMMAND_TYPE.CMD_TYPE_PROLIFIC, identifyDevice))
                            {
                                return true;
                            }
                        }

                        // USB-NVMe
                        if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeJMicron(storage, handle, out identifyDevice))
                        {
                            if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_JMICRON))
                            {
                                return true;
                            }
                        }

                        // USB-NVMe
                        if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeASMedia(storage, handle, out identifyDevice))
                        {
                            if (DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_ASMEDIA))
                            {
                                return true;
                            }
                        }

                        // USB-NVMe
                        if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeRealtek(storage, handle, out identifyDevice))
                        {
                            var ok = DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_REALTEK);

                            if (RealtekMethods.RealtekRAIDMode(storage, handle))
                            {
                                if (RealtekMethods.RealtekSwitchMode(storage, handle, true, 1))
                                {
                                    if (NVMeSmartIdentifier.DoIdentifyDeviceNVMeRealtek(storage, handle, out identifyDevice))
                                    {
                                        ok = DiskHandler.AddDiskNVMe(storage, handle, identifyDevice, COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP);
                                    }

                                    RealtekMethods.RealtekSwitchMode(storage, handle, true, 0);
                                }
                            }

                            if (ok)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                identifyDevice?.Dispose();
            }
        }

        internal static bool DoIdentifyDeviceSat(Storage storage, IntPtr handle, byte target, COMMAND_TYPE type, out IdentifyDevice identifyDevice)
        {
            var sptwb = new SCSI_PASS_THROUGH_WITH_BUFFERS();

            sptwb.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            sptwb.Spt.PathId = 0;
            sptwb.Spt.TargetId = 0;
            sptwb.Spt.Lun = 0;
            sptwb.Spt.SenseInfoLength = 24;
            sptwb.Spt.DataIn = InteropConstants.SCSI_IOCTL_DATA_IN;
            sptwb.Spt.DataTransferLength = InteropConstants.IDENTIFY_BUFFER_SIZE;
            sptwb.Spt.TimeOutValue = 2;
            sptwb.Spt.DataBufferOffset = (ulong)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.DataBuf)).ToInt64();
            sptwb.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.SenseBuf)).ToInt32();

            switch (type)
            {
                case COMMAND_TYPE.CMD_TYPE_SAT:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = 0;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 1;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 0;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = 0;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = 0;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.ID_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_SAT_ASM1352R:
                    // PROTOCOL field should be "0Dh”SATA port0 and "0Eh" SATA port1.
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (0xE << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = 0;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 1;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 0;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = 0;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = 0;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.ID_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = 0;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 1;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 0;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = 0;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = 0;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.ID_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_SUNPLUS:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xF8;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = 0x22;
                    sptwb.Spt.Cdb[3] = 0x10;
                    sptwb.Spt.Cdb[4] = 0x01;
                    sptwb.Spt.Cdb[5] = 0x00;
                    sptwb.Spt.Cdb[6] = 0x01;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = 0x00;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = target;
                    sptwb.Spt.Cdb[11] = 0xEC; // ID_CMD
                    break;
                case COMMAND_TYPE.CMD_TYPE_IO_DATA:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xE3;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0x01;
                    sptwb.Spt.Cdb[4] = 0x01;
                    sptwb.Spt.Cdb[5] = 0x00;
                    sptwb.Spt.Cdb[6] = 0x00;
                    sptwb.Spt.Cdb[7] = target;
                    sptwb.Spt.Cdb[8] = 0xEC;  // ID_CMD
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = 0x00;
                    sptwb.Spt.Cdb[11] = 0x00;
                    break;
                case COMMAND_TYPE.CMD_TYPE_LOGITEC:
                    sptwb.Spt.CdbLength = 10;
                    sptwb.Spt.Cdb[0] = 0xE0;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0x00;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = 0x00;
                    sptwb.Spt.Cdb[6] = 0x00;
                    sptwb.Spt.Cdb[7] = target;
                    sptwb.Spt.Cdb[8] = 0xEC;  // ID_CMD
                    sptwb.Spt.Cdb[9] = 0x4C;
                    break;
                case COMMAND_TYPE.CMD_TYPE_PROLIFIC:
                    sptwb.Spt.CdbLength = 16;
                    sptwb.Spt.Cdb[0] = 0xD8;
                    sptwb.Spt.Cdb[1] = 0x15;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0x00;
                    sptwb.Spt.Cdb[4] = 0x06;
                    sptwb.Spt.Cdb[5] = 0x7B;
                    sptwb.Spt.Cdb[6] = 0x00;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = 0x02;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = 0x01;
                    sptwb.Spt.Cdb[11] = 0x00;
                    sptwb.Spt.Cdb[12] = 0x00;
                    sptwb.Spt.Cdb[13] = 0x00;
                    sptwb.Spt.Cdb[14] = target;
                    sptwb.Spt.Cdb[15] = 0xEC; // ID_CMD
                    break;
                case COMMAND_TYPE.CMD_TYPE_JMICRON:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xDF;
                    sptwb.Spt.Cdb[1] = 0x10;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0x02;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = 0x00;
                    sptwb.Spt.Cdb[6] = 0x01;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = 0x00;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = target;
                    sptwb.Spt.Cdb[11] = 0xEC; // ID_CMD
                    break;
                case COMMAND_TYPE.CMD_TYPE_CYPRESS:
                    sptwb.Spt.CdbLength = 16;
                    sptwb.Spt.Cdb[0] = 0x24;
                    sptwb.Spt.Cdb[1] = 0x24;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0xBE;
                    sptwb.Spt.Cdb[4] = 0x01;
                    sptwb.Spt.Cdb[5] = 0x00;
                    sptwb.Spt.Cdb[6] = 0x00;
                    sptwb.Spt.Cdb[7] = 0x01;
                    sptwb.Spt.Cdb[8] = 0x00;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = 0x00;
                    sptwb.Spt.Cdb[11] = target;
                    sptwb.Spt.Cdb[12] = 0xEC; // ID_CMD
                    sptwb.Spt.Cdb[13] = 0x00;
                    sptwb.Spt.Cdb[14] = 0x00;
                    sptwb.Spt.Cdb[15] = 0x00;
                    break;
            }

            var length = (int)(Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.DataBuf)).ToInt32() + sptwb.Spt.DataTransferLength);

            var ptrSize = Marshal.SizeOf<SCSI_PASS_THROUGH_WITH_BUFFERS>();
            var ptr = Marshal.AllocHGlobal(ptrSize);
            Marshal.StructureToPtr(sptwb, ptr, false);

            if (!Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_PASS_THROUGH, ptr, Marshal.SizeOf<SCSI_PASS_THROUGH>(), ptr, length, out _, IntPtr.Zero))
            {
                Marshal.FreeHGlobal(ptr);

                identifyDevice = null;
                return false;
            }

            sptwb = Marshal.PtrToStructure<SCSI_PASS_THROUGH_WITH_BUFFERS>(ptr);

            if (false == sptwb.DataBuf.Any(b => b != 0))
            {
                Marshal.FreeHGlobal(ptr);

                identifyDevice = null;
                return false;
            }

            identifyDevice = new IdentifyDevice();

            Marshal.Copy(sptwb.DataBuf, 0, identifyDevice.IdentifyDevicePtr, identifyDevice.PtrSize);

            Marshal.FreeHGlobal(ptr);

            return true;
        }

        internal static bool DoIdentifyDeviceSi(Storage storage, IntPtr handle, ushort scsiBus, out IdentifyDevice identifyDevice)
        {
            var sid = new SilIdentDev();
            var size = Marshal.SizeOf<SilIdentDev>();

            sid.sic.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            Array.Copy(InteropConstants.SIL_SIG_STR_ARR, sid.sic.Signature, InteropConstants.SIL_SIG_STR_LEN);
            sid.sic.Timeout = 5;
            sid.sic.ControlCode = 270344; //CTL_CODE(FILE_DEVICE_CONTROLLER, 0x802, METHOD_BUFFERED, FILE_ANY_ACCESS)
            sid.sic.ReturnCode = 0xffffffff;
            sid.sic.Length = (uint)(size - Marshal.OffsetOf<SilIdentDev>(nameof(SilIdentDev.port)).ToInt32());
            sid.port = scsiBus;
            sid.maybe_always1 = 1;

            bool ok = false;

            var ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(sid, ptr, false);

            if (Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_MINIPORT, ptr, size, ptr, size, out _, IntPtr.Zero))
            {
                ok = true;

                sid = Marshal.PtrToStructure<SilIdentDev>(ptr);

                identifyDevice = new IdentifyDevice();

                Marshal.Copy(sid.id_data, 0, identifyDevice.IdentifyDevicePtr, identifyDevice.PtrSize);
            }
            else
            {
                identifyDevice = null;
            }

            Marshal.FreeHGlobal(ptr);

            return ok;
        }

        internal static bool DoIdentifyDevicePd(Storage storage, IntPtr handle, byte target, out IdentifyDevice identifyDevice)
        {
            bool ok = false;
            string model = null;

            identifyDevice = null;

            if (ATAInfo.AtaPassThrough && ATAInfo.AtaPassThroughSmart)
            {
                identifyDevice = new IdentifyDevice();

                var buffer = new byte[InteropConstants.IDENTIFY_BUFFER_SIZE];

                ok = ATAMethods.SendAtaCommandPd(handle, target, 0xEC, 0x00, 0x00, buffer);

                if (ok)
                {
                    Marshal.Copy(buffer, 0, identifyDevice.IdentifyDevicePtr, buffer.Length);
                }

                var ata = identifyDevice.ToATA();

                model = Encoding.ASCII.GetString(ata.Model);
            }

            if (!ok || string.IsNullOrEmpty(model))
            {
                var sendCmdIn = new SENDCMDINPARAMS();
                var sizeCmdIn = Marshal.SizeOf<SENDCMDINPARAMS>();

                sendCmdIn.irDriveRegs.bCommandReg      = InteropConstants.ID_CMD;
                sendCmdIn.irDriveRegs.bSectorCountReg  = 1;
                sendCmdIn.irDriveRegs.bSectorNumberReg = 1;
                sendCmdIn.irDriveRegs.bDriveHeadReg    = target;
                sendCmdIn.cBufferSize = InteropConstants.IDENTIFY_BUFFER_SIZE;

                var sendCmdOut = new IDENTIFY_DEVICE_OUTDATA();
                var sizeCmdOut = Marshal.SizeOf<IDENTIFY_DEVICE_OUTDATA>();

                var ptrIn = Marshal.AllocHGlobal(sizeCmdIn);
                Marshal.StructureToPtr(sendCmdIn, ptrIn, false);

                var ptrOut = Marshal.AllocHGlobal(sizeCmdOut);
                Marshal.StructureToPtr(sendCmdOut, ptrOut, false);

                ok = Kernel32.DeviceIoControl(handle, Kernel32.DFP_RECEIVE_DRIVE_DATA, ptrIn, sizeCmdIn, ptrOut, sizeCmdOut, out var returned, IntPtr.Zero);

                if (!ok || returned != sizeCmdOut)
                {
                    Marshal.FreeHGlobal(ptrIn);
                    Marshal.FreeHGlobal(ptrOut);

                    identifyDevice = null;

                    return false;
                }

                sendCmdOut = Marshal.PtrToStructure<IDENTIFY_DEVICE_OUTDATA>(ptrOut);

                var offset = Marshal.OffsetOf<IDENTIFY_DEVICE_OUTDATA>(nameof(IDENTIFY_DEVICE_OUTDATA.SendCmdOutParam)).ToInt32()
                           + Marshal.OffsetOf<SENDCMDOUTPARAMS>(nameof(SENDCMDOUTPARAMS.bBuffer)).ToInt32();

                var bufferOffsetPtr = ptrOut + offset;

                identifyDevice = new IdentifyDevice();

                MarshalExtensions.Copy(bufferOffsetPtr, identifyDevice.IdentifyDevicePtr, 0, Marshal.SizeOf<ATA_IDENTIFY_DEVICE>());

                Marshal.FreeHGlobal(ptrIn);
                Marshal.FreeHGlobal(ptrOut);

                return true;
            }

            return true;
        }

        internal static bool DoIdentifyDeviceScsi(Storage storage, IntPtr handle, out IdentifyDevice identifyDevice)
        {
            if (!SharedMethods.GetScsiAddress(handle, out var scsiAddress))
            {
                identifyDevice = null;

                return false;
            }

            var totalSize = Marshal.SizeOf<SRB_IO_CONTROL>() + Marshal.SizeOf<SENDCMDOUTPARAMS>() + InteropConstants.IDENTIFY_BUFFER_SIZE;
            var ptrAll = Marshal.AllocHGlobal(totalSize);

            var srbIo = new SRB_IO_CONTROL();
            var cmdIn = new SENDCMDINPARAMS();

            srbIo.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            srbIo.Timeout = 2;
            srbIo.Length = (uint)(Marshal.SizeOf<SENDCMDOUTPARAMS>() + InteropConstants.IDENTIFY_BUFFER_SIZE);
            srbIo.ControlCode = Kernel32.IOCTL_SCSI_MINIPORT_IDENTIFY;
            Array.Copy(InteropConstants.SCSI_SIG_STR_ARR, srbIo.Signature, InteropConstants.SCSI_SIG_STR_LEN);

            cmdIn.irDriveRegs.bCommandReg = InteropConstants.ID_CMD;
            cmdIn.bDriveNumber = scsiAddress.TargetId;

            if (Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_MINIPORT, ptrAll,
                Marshal.SizeOf<SRB_IO_CONTROL>() + Marshal.SizeOf<SENDCMDINPARAMS>() - 1,
                ptrAll, totalSize, out _, IntPtr.Zero))
            {
                var outPtrLocation = ptrAll + Marshal.SizeOf<SRB_IO_CONTROL>();

                var cmdOut = Marshal.PtrToStructure<SENDCMDOUTPARAMS>(outPtrLocation);

                var offset = Marshal.OffsetOf<SENDCMDOUTPARAMS>(nameof(SENDCMDOUTPARAMS.bBuffer)).ToInt32();

                var bufferOffsetPtr = outPtrLocation + offset;

                identifyDevice = new IdentifyDevice();

                MarshalExtensions.Copy(bufferOffsetPtr, identifyDevice.IdentifyDevicePtr, 0, identifyDevice.PtrSize);

                Marshal.FreeHGlobal(ptrAll);

                return true;
            }
            else
            {
                identifyDevice = null;

                Marshal.FreeHGlobal(ptrAll);

                return false;
            }
        }

        #endregion
    }
}
