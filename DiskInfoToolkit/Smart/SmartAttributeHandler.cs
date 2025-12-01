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

using DiskInfoToolkit.Enums.Interop;
using DiskInfoToolkit.HardDrive;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Interop.Enums;
using DiskInfoToolkit.Interop.Structures;
using DiskInfoToolkit.Logging;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Smart
{
    internal static class SmartAttributeHandler
    {
        #region Public

        public static void CheckSmartAttributeUpdate(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            if (!storage.Smart.Status.HasFlag(SmartStatus.IsSmartCorrect))
            {
                return;
            }

            //For now just update the attributes
            //It is possible to add an event later to notify user which attribute has changed, with previous and current value[s]
            SetAttributes(storage, smartAttributes);
        }

        public static void SetAttributes(Storage storage, List<SmartAttributeStructure> smartAttributes)
        {
            //Set all attributes
            if (smartAttributes != null)
            {
                storage.Smart.SmartAttributes.Clear();

                foreach (var attr in smartAttributes)
                {
                    var info = SmartAttributeInfoMapping.Mapping[storage.SmartKey];
                    if (info != null)
                    {
                        var attrInfo = info.Find(sai => sai.ID == attr.ID);

                        if (attrInfo != null)
                        {
                            storage.Smart.SmartAttributes.Add(new()
                            {
                                Info = attrInfo,
                                Attribute = attr,
                            });
                        }
                        else
                        {
                            LogSimple.LogTrace($"Attribute for '{storage.SmartKey}' is not implemented (ID: '{attr.ID,-3}' | Value: '{attr.RawValueULong,-20}').");
                        }
                    }
                }
            }
        }

        internal static bool GetSmartDataAMD_RC2(Storage storage, IntPtr handle, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            throw new NotImplementedException("TODO");
        }

        internal static bool GetSmartThresholdAMD_RC2(Storage storage, IntPtr handle, byte[] buffer)
        {
            throw new NotImplementedException("TODO");
        }

        internal static bool GetSmartInfoJMS56X(Storage storage, IntPtr handle, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            throw new NotImplementedException("TODO");
        }

        internal static bool GetSmartInfoJMB39X(Storage storage, IntPtr handle, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            throw new NotImplementedException("TODO");
        }

        internal static bool GetSmartInfoJMS586_40(Storage storage, IntPtr handle, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            throw new NotImplementedException("TODO");
        }

        internal static bool GetSmartInfoJMS586_20(Storage storage, IntPtr handle, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            throw new NotImplementedException("TODO");
        }

        public static bool GetSmartAttributePd(Storage storage, IntPtr handle, byte target, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            bool ok = false;

            if (ATAInfo.AtaPassThrough && ATAInfo.AtaPassThroughSmart)
            {
                ok = ATAMethods.SendAtaCommandPd(handle, target, InteropConstants.SMART_CMD, InteropConstants.READ_ATTRIBUTES, 0x00, buffer);
            }

            if (!ok)
            {
                var sendCmdIn = new SENDCMDINPARAMS();
                var sizeCmdIn = Marshal.SizeOf<SENDCMDINPARAMS>();

                sendCmdIn.irDriveRegs.bFeaturesReg = InteropConstants.READ_ATTRIBUTES;
                sendCmdIn.irDriveRegs.bSectorCountReg = 1;
                sendCmdIn.irDriveRegs.bSectorNumberReg = 1;
                sendCmdIn.irDriveRegs.bCylLowReg = InteropConstants.SMART_CYL_LOW;
                sendCmdIn.irDriveRegs.bCylHighReg = InteropConstants.SMART_CYL_HI;
                sendCmdIn.irDriveRegs.bDriveHeadReg = target;
                sendCmdIn.irDriveRegs.bCommandReg = InteropConstants.SMART_CMD;
                sendCmdIn.cBufferSize = InteropConstants.READ_ATTRIBUTE_BUFFER_SIZE;

                var sendCmdOut = new SMART_READ_DATA_OUTDATA();
                var sizeCmdOut = Marshal.SizeOf<SMART_READ_DATA_OUTDATA>();

                var ptrIn = Marshal.AllocHGlobal(sizeCmdIn);
                Marshal.StructureToPtr(sendCmdIn, ptrIn, false);

                var ptrOut = Marshal.AllocHGlobal(sizeCmdOut);
                Marshal.StructureToPtr(sendCmdOut, ptrOut, false);

                ok = Kernel32.DeviceIoControl(handle, Kernel32.DFP_RECEIVE_DRIVE_DATA, ptrIn, sizeCmdIn, ptrOut, sizeCmdOut, out var returned, IntPtr.Zero);

                if (!ok || returned != sizeCmdOut)
                {
                    Marshal.FreeHGlobal(ptrIn);
                    Marshal.FreeHGlobal(ptrOut);

                    smartAttributes = null;

                    return false;
                }

                var offset = Marshal.OffsetOf<SMART_READ_DATA_OUTDATA>(nameof(SMART_READ_DATA_OUTDATA.SendCmdOutParam)).ToInt32()
                           + Marshal.OffsetOf<SENDCMDOUTPARAMS>(nameof(SENDCMDOUTPARAMS.bBuffer)).ToInt32();

                var bufferOffsetPtr = ptrOut + offset;

                Marshal.Copy(bufferOffsetPtr, buffer, 0, buffer.Length);

                Marshal.FreeHGlobal(ptrIn);
                Marshal.FreeHGlobal(ptrOut);

                smartAttributes = new();

                return true;
            }

            return FillSmartData(storage, buffer, out smartAttributes);
        }

        public static bool FillSmartData(Storage storage, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            bool hasAttributes = false;

            smartAttributes = new();

            var attributeSize = Marshal.SizeOf<SmartAttributeStructure>();

            var ptr = Marshal.AllocHGlobal(attributeSize);

            for (int i = 0; i < SmartAttributeStructure.MaxAttribute; ++i)
            {
                Marshal.Copy(buffer, i * attributeSize + 2, ptr, attributeSize);

                var attribute = Marshal.PtrToStructure<SmartAttributeStructure>(ptr);

                if (attribute.ID != 0)
                {
                    hasAttributes = true;

                    smartAttributes.Add(attribute);

                    HandleAttribute(storage, storage.Smart, attribute);
                }
            }

            Marshal.FreeHGlobal(ptr);

            return hasAttributes;
        }

        public static bool CheckSmartAttributeCorrect(List<SmartAttributeStructure> smartAttributes, List<SmartAttributeStructure> smartAttributesCheck)
        {
            if (smartAttributes.Count != smartAttributesCheck.Count)
            {
                return false;
            }

            for (int i = 0; i < smartAttributes.Count; ++i)
            {
                if (smartAttributes[i].ID != smartAttributesCheck[i].ID)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool GetSmartThresholdPd(Storage storage, IntPtr handle, byte target, byte[] buffer, List<SmartAttributeStructure> smartAttributes)
        {
            bool ok = false;

            if (ATAInfo.AtaPassThrough && ATAInfo.AtaPassThroughSmart)
            {
                ok = ATAMethods.SendAtaCommandPd(handle, target, InteropConstants.SMART_CMD, InteropConstants.READ_THRESHOLDS, 0x00, buffer);
            }

            if (!ok)
            {
                var sendCmdIn = new SENDCMDINPARAMS();
                var sizeCmdIn = Marshal.SizeOf<SENDCMDINPARAMS>();

                sendCmdIn.irDriveRegs.bFeaturesReg = InteropConstants.READ_THRESHOLDS;
                sendCmdIn.irDriveRegs.bSectorCountReg = 1;
                sendCmdIn.irDriveRegs.bSectorNumberReg = 1;
                sendCmdIn.irDriveRegs.bCylLowReg = InteropConstants.SMART_CYL_LOW;
                sendCmdIn.irDriveRegs.bCylHighReg = InteropConstants.SMART_CYL_HI;
                sendCmdIn.irDriveRegs.bDriveHeadReg = target;
                sendCmdIn.irDriveRegs.bCommandReg = InteropConstants.SMART_CMD;
                sendCmdIn.cBufferSize = InteropConstants.READ_THRESHOLD_BUFFER_SIZE;

                var sendCmdOut = new SMART_READ_DATA_OUTDATA();
                var sizeCmdOut = Marshal.SizeOf<SMART_READ_DATA_OUTDATA>();

                var ptrIn = Marshal.AllocHGlobal(sizeCmdIn);
                Marshal.StructureToPtr(sendCmdIn, ptrIn, false);

                var ptrOut = Marshal.AllocHGlobal(sizeCmdOut);
                Marshal.StructureToPtr(sendCmdOut, ptrOut, false);

                ok = Kernel32.DeviceIoControl(handle, Kernel32.DFP_RECEIVE_DRIVE_DATA, ptrIn, sizeCmdIn, ptrOut, sizeCmdOut, out var returned, IntPtr.Zero);

                if (!ok || returned != sizeCmdOut)
                {
                    Marshal.FreeHGlobal(ptrIn);
                    Marshal.FreeHGlobal(ptrOut);

                    return false;
                }

                var offset = Marshal.OffsetOf<SMART_READ_DATA_OUTDATA>(nameof(SMART_READ_DATA_OUTDATA.SendCmdOutParam)).ToInt32()
                           + Marshal.OffsetOf<SENDCMDOUTPARAMS>(nameof(SENDCMDOUTPARAMS.bBuffer)).ToInt32();

                var bufferOffsetPtr = ptrOut + offset;

                Marshal.Copy(bufferOffsetPtr, buffer, 0, buffer.Length);

                Marshal.FreeHGlobal(ptrIn);
                Marshal.FreeHGlobal(ptrOut);

                return true;
            }

            return FillSmartThreshold(storage, buffer, smartAttributes);
        }

        public static bool FillSmartThreshold(Storage storage, byte[] buffer, List<SmartAttributeStructure> smartAttributes)
        {
            var thresholdSize = Marshal.SizeOf<SmartThreshold>();

            var ptr = Marshal.AllocHGlobal(thresholdSize);

            int count = 0;
            for (int i = 0; i < SmartAttributeStructure.MaxAttribute; ++i)
            {
                Marshal.Copy(buffer, i * thresholdSize + 2, ptr, thresholdSize);

                var threshold = Marshal.PtrToStructure<SmartThreshold>(ptr);

                if (threshold.ID != 0)
                {
                    for (int j = 0; j < smartAttributes.Count; ++j)
                    {
                        var temp = smartAttributes[j];

                        if (threshold.ID == temp.ID)
                        {
                            temp.Threshold = threshold.ThresholdValue;

                            smartAttributes[j] = temp;

                            ++count;
                        }
                    }
                }
            }

            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicron && count == 0)
            {
                for (int i = 0; i < SmartAttributeStructure.MaxAttribute; ++i)
                {
                    Marshal.Copy(buffer, i * thresholdSize + 2, ptr, thresholdSize);

                    var threshold = Marshal.PtrToStructure<SmartThreshold>(ptr);

                    if (threshold.ID != 0)
                    {
                        for (int j = 0; j < smartAttributes.Count; ++j)
                        {
                            var temp = smartAttributes[j];

                            if (threshold.ID == temp.ID)
                            {
                                //Threshold value is already in SmartAttribute.Reserved
                                //So nothing to do here

                                ++count;
                            }
                        }
                    }
                }
            }

            Marshal.FreeHGlobal(ptr);

            return smartAttributes.Count > 0;
        }

        public static bool ControlSmartStatusPd(Storage storage, IntPtr handle, byte target, byte command)
        {
            bool ok = false;

            if (ATAInfo.AtaPassThrough && ATAInfo.AtaPassThroughSmart)
            {
                ok = ATAMethods.SendAtaCommandPd(handle, target, InteropConstants.SMART_CMD, command, 0x00, null);
            }

            if (!ok)
            {
                var sendCmdIn = new SENDCMDINPARAMS();
                var sizeCmdIn = Marshal.SizeOf<SENDCMDINPARAMS>();

                sendCmdIn.irDriveRegs.bFeaturesReg = command;
                sendCmdIn.irDriveRegs.bSectorCountReg = 1;
                sendCmdIn.irDriveRegs.bSectorNumberReg = 1;
                sendCmdIn.irDriveRegs.bCylLowReg = InteropConstants.SMART_CYL_LOW;
                sendCmdIn.irDriveRegs.bCylHighReg = InteropConstants.SMART_CYL_HI;
                sendCmdIn.irDriveRegs.bDriveHeadReg = target;
                sendCmdIn.irDriveRegs.bCommandReg = InteropConstants.SMART_CMD;
                sendCmdIn.cBufferSize = 0;

                var sendCmdOut = new SMART_READ_DATA_OUTDATA();
                var sizeCmdOut = Marshal.SizeOf<SMART_READ_DATA_OUTDATA>();

                var ptrIn = Marshal.AllocHGlobal(sizeCmdIn);
                Marshal.StructureToPtr(sendCmdIn, ptrIn, false);

                var ptrOut = Marshal.AllocHGlobal(sizeCmdOut);
                Marshal.StructureToPtr(sendCmdOut, ptrOut, false);

                ok = Kernel32.DeviceIoControl(handle, Kernel32.DFP_SEND_DRIVE_COMMAND, ptrIn, sizeCmdIn - 1, ptrOut, sizeCmdOut - 1, out var returned, IntPtr.Zero);

                Marshal.FreeHGlobal(ptrIn);
                Marshal.FreeHGlobal(ptrOut);
            }

            return ok;
        }

        public static bool GetSmartAttributeScsi(Storage storage, IntPtr handle, byte target, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            var inParams = new SENDCMDINPARAMS();
            var sizeIn = Marshal.SizeOf<SENDCMDINPARAMS>();

            var p = new SRB_IO_CONTROL();
            var sizeP = Marshal.SizeOf<SRB_IO_CONTROL>();

            var sizeOutParams = Marshal.SizeOf<SENDCMDOUTPARAMS>();

            var tempBufferSize = sizeP + sizeOutParams + InteropConstants.READ_ATTRIBUTE_BUFFER_SIZE;

            var ptr = Marshal.AllocHGlobal(tempBufferSize);
            p.HeaderLength = (uint)sizeP;
            p.Timeout = 2;
            p.Length = (uint)(sizeOutParams + InteropConstants.READ_ATTRIBUTE_BUFFER_SIZE);
            p.ControlCode = Kernel32.IOCTL_SCSI_MINIPORT_READ_SMART_ATTRIBS;

            Array.Copy(InteropConstants.SCSI_SIG_STR_ARR, p.Signature, InteropConstants.SCSI_SIG_STR_LEN);

            inParams.irDriveRegs.bFeaturesReg = InteropConstants.READ_ATTRIBUTES;
            inParams.irDriveRegs.bSectorCountReg = 1;
            inParams.irDriveRegs.bSectorNumberReg = 1;
            inParams.irDriveRegs.bCylLowReg = InteropConstants.SMART_CYL_LOW;
            inParams.irDriveRegs.bCylHighReg = InteropConstants.SMART_CYL_HI;
            inParams.irDriveRegs.bCommandReg = InteropConstants.SMART_CMD;
            inParams.cBufferSize = InteropConstants.READ_ATTRIBUTE_BUFFER_SIZE;
            inParams.bDriveNumber = target;

            Marshal.StructureToPtr(p, ptr, false);

            var pinLocation = ptr + sizeP;
            Marshal.StructureToPtr(inParams, pinLocation, false);

            if (Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_MINIPORT,
                                         ptr, sizeP + sizeIn - 1,
                                         ptr, tempBufferSize,
                                         out var returned, IntPtr.Zero))
            {
                Marshal.Copy(ptr + sizeP, buffer, 0, buffer.Length);

                Marshal.FreeHGlobal(ptr);

                return FillSmartData(storage, buffer, out smartAttributes);
            }

            Marshal.FreeHGlobal(ptr);

            smartAttributes = null;

            return false;
        }

        public static bool GetSmartThresholdScsi(Storage storage, IntPtr handle, byte target, byte[] buffer, List<SmartAttributeStructure> smartAttributes)
        {
            var inParams = new SENDCMDINPARAMS();
            var sizeIn = Marshal.SizeOf<SENDCMDINPARAMS>();

            var p = new SRB_IO_CONTROL();
            var sizeP = Marshal.SizeOf<SRB_IO_CONTROL>();

            var sizeOutParams = Marshal.SizeOf<SENDCMDOUTPARAMS>();

            var tempBufferSize = sizeP + sizeOutParams + InteropConstants.READ_THRESHOLD_BUFFER_SIZE;

            var ptr = Marshal.AllocHGlobal(tempBufferSize);
            p.HeaderLength = (uint)sizeP;
            p.Timeout = 2;
            p.Length = (uint)(sizeOutParams + InteropConstants.READ_THRESHOLD_BUFFER_SIZE);
            p.ControlCode = Kernel32.IOCTL_SCSI_MINIPORT_READ_SMART_THRESHOLDS;

            Array.Copy(InteropConstants.SCSI_SIG_STR_ARR, p.Signature, InteropConstants.SCSI_SIG_STR_LEN);

            inParams.irDriveRegs.bFeaturesReg = InteropConstants.READ_THRESHOLDS;
            inParams.irDriveRegs.bSectorCountReg = 1;
            inParams.irDriveRegs.bSectorNumberReg = 1;
            inParams.irDriveRegs.bCylLowReg = InteropConstants.SMART_CYL_LOW;
            inParams.irDriveRegs.bCylHighReg = InteropConstants.SMART_CYL_HI;
            inParams.irDriveRegs.bCommandReg = InteropConstants.SMART_CMD;
            inParams.cBufferSize = InteropConstants.READ_THRESHOLD_BUFFER_SIZE;
            inParams.bDriveNumber = target;

            Marshal.StructureToPtr(p, ptr, false);

            var pinLocation = ptr + sizeP;
            Marshal.StructureToPtr(inParams, pinLocation, false);

            if (Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_MINIPORT,
                                         ptr, sizeP + sizeIn - 1,
                                         ptr, tempBufferSize,
                                         out var returned, IntPtr.Zero))
            {
                Marshal.Copy(ptr + sizeP, buffer, 0, buffer.Length);

                Marshal.FreeHGlobal(ptr);

                return FillSmartThreshold(storage, buffer, smartAttributes);
            }

            Marshal.FreeHGlobal(ptr);

            return false;
        }

        public static bool ControlSmartStatusScsi(Storage storage, IntPtr handle, byte target, byte command)
        {
            bool ok = false;

            var inParams = new SENDCMDINPARAMS();
            var sizeIn = Marshal.SizeOf<SENDCMDINPARAMS>();

            var p = new SRB_IO_CONTROL();
            var sizeP = Marshal.SizeOf<SRB_IO_CONTROL>();

            var sizeOutParams = Marshal.SizeOf<SENDCMDOUTPARAMS>();

            var tempBufferSize = sizeP + sizeOutParams + InteropConstants.SCSI_MINIPORT_BUFFER_SIZE;

            var ptr = Marshal.AllocHGlobal(tempBufferSize);
            p.HeaderLength = (uint)sizeP;
            p.Timeout = 2;
            p.Length = (uint)(sizeOutParams + InteropConstants.SCSI_MINIPORT_BUFFER_SIZE);

            if (command == InteropConstants.DISABLE_SMART)
            {
                p.ControlCode = Kernel32.IOCTL_SCSI_MINIPORT_DISABLE_SMART;
            }
            else
            {
                p.ControlCode = Kernel32.IOCTL_SCSI_MINIPORT_ENABLE_SMART;
            }

            Array.Copy(InteropConstants.SCSI_SIG_STR_ARR, p.Signature, InteropConstants.SCSI_SIG_STR_LEN);

            inParams.irDriveRegs.bFeaturesReg = command;
            inParams.irDriveRegs.bSectorCountReg = 1;
            inParams.irDriveRegs.bSectorNumberReg = 1;
            inParams.irDriveRegs.bCylLowReg = InteropConstants.SMART_CYL_LOW;
            inParams.irDriveRegs.bCylHighReg = InteropConstants.SMART_CYL_HI;
            inParams.irDriveRegs.bCommandReg = InteropConstants.SMART_CMD;
            inParams.cBufferSize = InteropConstants.SCSI_MINIPORT_BUFFER_SIZE;
            inParams.bDriveNumber = target;

            Marshal.StructureToPtr(p, ptr, false);

            var pinLocation = ptr + sizeP;
            Marshal.StructureToPtr(inParams, pinLocation, false);

            ok = Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_MINIPORT,
                                          ptr, sizeP + sizeIn - 1,
                                          ptr, tempBufferSize,
                                          out var returned, IntPtr.Zero);

            Marshal.FreeHGlobal(ptr);

            return ok;
        }

        public static bool GetSmartAttributeSi(Storage storage, IntPtr handle, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            var spf = new STORAGE_PREDICT_FAILURE();
            var spfSize = Marshal.SizeOf<STORAGE_PREDICT_FAILURE>();

            var ptr = Marshal.AllocHGlobal(spfSize);

            Marshal.StructureToPtr(spf, ptr, false);

            if (Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_STORAGE_PREDICT_FAILURE, ptr, spfSize, ptr, spfSize, out _, IntPtr.Zero))
            {
                spf = Marshal.PtrToStructure<STORAGE_PREDICT_FAILURE>(ptr);

                Array.Copy(spf.VendorSpecific, buffer, buffer.Length);

                Marshal.FreeHGlobal(ptr);

                return FillSmartData(storage, buffer, out smartAttributes);
            }

            Marshal.FreeHGlobal(ptr);

            smartAttributes = null;

            //return GetSmartAttributeWmi();
            return false;
        }

        public static bool GetSmartAttributeCsmi(Storage storage, IntPtr handle, CSMI_SAS_PHY_ENTITY? sasPhyEntity, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            if (ATAMethods.SendAtaCommandCsmi(handle, sasPhyEntity, InteropConstants.SMART_CMD, InteropConstants.READ_ATTRIBUTES, 0x00, buffer))
            {
                return FillSmartData(storage, buffer, out smartAttributes);
            }
            else
            {
                smartAttributes = null;

                return false;
            }
        }

        public static bool GetSmartThresholdCsmi(Storage storage, IntPtr handle, CSMI_SAS_PHY_ENTITY? sasPhyEntity, byte[] buffer, List<SmartAttributeStructure> smartAttributes)
        {
            if (ATAMethods.SendAtaCommandCsmi(handle, sasPhyEntity, InteropConstants.SMART_CMD, InteropConstants.READ_THRESHOLDS, 0x00, buffer))
            {
                return FillSmartThreshold(storage, buffer, smartAttributes);
            }
            else
            {
                smartAttributes = null;

                return false;
            }
        }

        public static bool ControlSmartStatusCsmi(Storage storage, IntPtr handle, CSMI_SAS_PHY_ENTITY? sasPhyEntity, byte command)
        {
            return ATAMethods.SendAtaCommandCsmi(handle, sasPhyEntity, InteropConstants.SMART_CMD, command, 0x00, null);
        }

        public static bool GetSmartAttributeSat(Storage storage, IntPtr handle, byte target, byte[] buffer, COMMAND_TYPE commandType, out List<SmartAttributeStructure> smartAttributes)
        {
            var sptwb = new SCSI_PASS_THROUGH_WITH_BUFFERS();

            sptwb.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            sptwb.Spt.PathId = 0;
            sptwb.Spt.TargetId = 0;
            sptwb.Spt.Lun = 0;
            sptwb.Spt.SenseInfoLength = 24;
            sptwb.Spt.DataIn = InteropConstants.SCSI_IOCTL_DATA_IN;
            sptwb.Spt.DataTransferLength = InteropConstants.READ_ATTRIBUTE_BUFFER_SIZE;
            sptwb.Spt.TimeOutValue = 2;
            sptwb.Spt.DataBufferOffset = (ulong)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.DataBuf)).ToInt64();
            sptwb.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.SenseBuf)).ToInt32();

            switch (commandType)
            {
                case COMMAND_TYPE.CMD_TYPE_SAT:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = InteropConstants.READ_ATTRIBUTES;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 1;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 1;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_LOW;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = InteropConstants.SMART_CYL_HI;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_SAT_ASM1352R:
                    // PROTOCOL field should be "0Dh”SATA port0 and "0Eh" SATA port1.
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (0xE << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = InteropConstants.READ_ATTRIBUTES;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 1;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 1;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_LOW;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = InteropConstants.SMART_CYL_HI;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = InteropConstants.READ_ATTRIBUTES;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 1;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 1;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_LOW;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = InteropConstants.SMART_CYL_HI;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_SUNPLUS:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xF8;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = 0x22;
                    sptwb.Spt.Cdb[3] = 0x10;
                    sptwb.Spt.Cdb[4] = 0x01;
                    sptwb.Spt.Cdb[5] = InteropConstants.READ_ATTRIBUTES;
                    sptwb.Spt.Cdb[6] = 0x01;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[10] = target;
                    sptwb.Spt.Cdb[11] = InteropConstants.SMART_CMD;
                    break;
                case COMMAND_TYPE.CMD_TYPE_IO_DATA:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xE3;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = InteropConstants.READ_ATTRIBUTES;
                    sptwb.Spt.Cdb[3] = 0x00;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[7] = target;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CMD;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = 0x00;
                    sptwb.Spt.Cdb[11] = 0x00;
                    break;
                case COMMAND_TYPE.CMD_TYPE_LOGITEC:
                    sptwb.Spt.CdbLength = 10;
                    sptwb.Spt.Cdb[0] = 0xE0;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = InteropConstants.READ_ATTRIBUTES;
                    sptwb.Spt.Cdb[3] = 0x00;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[7] = target;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CMD;
                    sptwb.Spt.Cdb[9] = 0x4C;
                    break;
                case COMMAND_TYPE.CMD_TYPE_PROLIFIC:
                    sptwb.Spt.CdbLength = 16;
                    sptwb.Spt.Cdb[0] = 0xD8;
                    sptwb.Spt.Cdb[1] = 0x15;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = InteropConstants.READ_ATTRIBUTES;
                    sptwb.Spt.Cdb[4] = 0x06;
                    sptwb.Spt.Cdb[5] = 0x7B;
                    sptwb.Spt.Cdb[6] = 0x00;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = 0x02;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = 0x01;
                    sptwb.Spt.Cdb[11] = 0x00;
                    sptwb.Spt.Cdb[12] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[13] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[14] = target;
                    sptwb.Spt.Cdb[15] = InteropConstants.SMART_CMD;
                    break;
                case COMMAND_TYPE.CMD_TYPE_JMICRON:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xDF;
                    sptwb.Spt.Cdb[1] = 0x10;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0x02;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = InteropConstants.READ_ATTRIBUTES;
                    sptwb.Spt.Cdb[6] = 0x01;
                    sptwb.Spt.Cdb[7] = 0x01;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[10] = target;
                    sptwb.Spt.Cdb[11] = InteropConstants.SMART_CMD;
                    break;
                case COMMAND_TYPE.CMD_TYPE_CYPRESS:
                    sptwb.Spt.CdbLength = 16;
                    sptwb.Spt.Cdb[0] = 0x24;
                    sptwb.Spt.Cdb[1] = 0x24;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0xBE;
                    sptwb.Spt.Cdb[4] = 0x01;
                    sptwb.Spt.Cdb[5] = 0x00;
                    sptwb.Spt.Cdb[6] = InteropConstants.READ_ATTRIBUTES;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = 0x00;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[10] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[11] = target;
                    sptwb.Spt.Cdb[12] = InteropConstants.SMART_CMD;
                    sptwb.Spt.Cdb[13] = 0x00;
                    sptwb.Spt.Cdb[14] = 0x00;
                    sptwb.Spt.Cdb[15] = 0x00;
                    break;
                default:
                    smartAttributes = null;
                    return false;
            }

            var length = (int)(Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.DataBuf)).ToInt32() + sptwb.Spt.DataTransferLength);

            var ptrSize = Marshal.SizeOf<SCSI_PASS_THROUGH_WITH_BUFFERS>();
            var ptr = Marshal.AllocHGlobal(ptrSize);
            Marshal.StructureToPtr(sptwb, ptr, false);

            if (!Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_PASS_THROUGH, ptr, Marshal.SizeOf<SCSI_PASS_THROUGH>(), ptr, length, out _, IntPtr.Zero))
            {
                Marshal.FreeHGlobal(ptr);

                smartAttributes = null;
                return false;
            }

            sptwb = Marshal.PtrToStructure<SCSI_PASS_THROUGH_WITH_BUFFERS>(ptr);

            if (false == sptwb.DataBuf.Any(b => b != 0))
            {
                Marshal.FreeHGlobal(ptr);

                smartAttributes = null;
                return false;
            }

            Array.Copy(sptwb.DataBuf, buffer, buffer.Length);

            Marshal.FreeHGlobal(ptr);

            return FillSmartData(storage, buffer, out smartAttributes);
        }

        public static bool GetSmartThresholdSat(Storage storage, IntPtr handle, byte target, byte[] buffer, COMMAND_TYPE commandType, List<SmartAttributeStructure> smartAttributes)
        {
            var sptwb = new SCSI_PASS_THROUGH_WITH_BUFFERS();

            sptwb.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            sptwb.Spt.PathId = 0;
            sptwb.Spt.TargetId = 0;
            sptwb.Spt.Lun = 0;
            sptwb.Spt.SenseInfoLength = 24;
            sptwb.Spt.DataIn = InteropConstants.SCSI_IOCTL_DATA_IN;
            sptwb.Spt.DataTransferLength = InteropConstants.READ_THRESHOLD_BUFFER_SIZE;
            sptwb.Spt.TimeOutValue = 2;
            sptwb.Spt.DataBufferOffset = (ulong)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.DataBuf)).ToInt64();
            sptwb.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.SenseBuf)).ToInt32();

            switch (commandType)
            {
                case COMMAND_TYPE.CMD_TYPE_SAT:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = InteropConstants.READ_THRESHOLDS;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 1;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 1;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_LOW;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = InteropConstants.SMART_CYL_HI;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_SAT_ASM1352R:
                    // PROTOCOL field should be "0Dh”SATA port0 and "0Eh" SATA port1.
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (0xE << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = InteropConstants.READ_THRESHOLDS;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 1;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 1;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_LOW;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = InteropConstants.SMART_CYL_HI;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = InteropConstants.READ_THRESHOLDS;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 1;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 1;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_LOW;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = InteropConstants.SMART_CYL_HI;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_SUNPLUS:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xF8;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = 0x22;
                    sptwb.Spt.Cdb[3] = 0x10;
                    sptwb.Spt.Cdb[4] = 0x01;
                    sptwb.Spt.Cdb[5] = InteropConstants.READ_THRESHOLDS;
                    sptwb.Spt.Cdb[6] = 0x01;
                    sptwb.Spt.Cdb[7] = 0x01;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[10] = target;
                    sptwb.Spt.Cdb[11] = InteropConstants.SMART_CMD;
                    break;
                case COMMAND_TYPE.CMD_TYPE_IO_DATA:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xE3;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = InteropConstants.READ_THRESHOLDS;
                    sptwb.Spt.Cdb[3] = 0x00;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[7] = target;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CMD;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = 0x00;
                    sptwb.Spt.Cdb[11] = 0x00;
                    break;
                case COMMAND_TYPE.CMD_TYPE_LOGITEC:
                    sptwb.Spt.CdbLength = 10;
                    sptwb.Spt.Cdb[0] = 0xE0;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = InteropConstants.READ_THRESHOLDS;
                    sptwb.Spt.Cdb[3] = 0x00;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[7] = target;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CMD;
                    sptwb.Spt.Cdb[9] = 0x4C;
                    break;
                case COMMAND_TYPE.CMD_TYPE_PROLIFIC:
                    sptwb.Spt.CdbLength = 16;
                    sptwb.Spt.Cdb[0] = 0xD8;
                    sptwb.Spt.Cdb[1] = 0x15;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = InteropConstants.READ_THRESHOLDS;
                    sptwb.Spt.Cdb[4] = 0x06;
                    sptwb.Spt.Cdb[5] = 0x7B;
                    sptwb.Spt.Cdb[6] = 0x00;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = 0x02;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = 0x01;
                    sptwb.Spt.Cdb[11] = 0x01;
                    sptwb.Spt.Cdb[12] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[13] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[14] = target;
                    sptwb.Spt.Cdb[15] = InteropConstants.SMART_CMD;
                    break;
                case COMMAND_TYPE.CMD_TYPE_JMICRON:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xDF;
                    sptwb.Spt.Cdb[1] = 0x10;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0x02;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = InteropConstants.READ_THRESHOLDS;
                    sptwb.Spt.Cdb[6] = 0x01;
                    sptwb.Spt.Cdb[7] = 0x01;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[10] = target;
                    sptwb.Spt.Cdb[11] = InteropConstants.SMART_CMD;
                    break;
                case COMMAND_TYPE.CMD_TYPE_CYPRESS:
                    sptwb.Spt.CdbLength = 16;
                    sptwb.Spt.Cdb[0] = 0x24;
                    sptwb.Spt.Cdb[1] = 0x24;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0xBE;
                    sptwb.Spt.Cdb[4] = 0x01;
                    sptwb.Spt.Cdb[5] = 0x00;
                    sptwb.Spt.Cdb[6] = InteropConstants.READ_THRESHOLDS;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = 0x00;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[10] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[11] = target;
                    sptwb.Spt.Cdb[12] = InteropConstants.SMART_CMD;
                    sptwb.Spt.Cdb[13] = 0x00;
                    sptwb.Spt.Cdb[14] = 0x00;
                    sptwb.Spt.Cdb[15] = 0x00;
                    break;
                default:
                    return false;
            }

            var length = (int)(Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.DataBuf)).ToInt32() + sptwb.Spt.DataTransferLength);

            var ptrSize = Marshal.SizeOf<SCSI_PASS_THROUGH_WITH_BUFFERS>();
            var ptr = Marshal.AllocHGlobal(ptrSize);
            Marshal.StructureToPtr(sptwb, ptr, false);

            if (!Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_PASS_THROUGH, ptr, Marshal.SizeOf<SCSI_PASS_THROUGH>(), ptr, length, out _, IntPtr.Zero))
            {
                Marshal.FreeHGlobal(ptr);

                smartAttributes = null;
                return false;
            }

            sptwb = Marshal.PtrToStructure<SCSI_PASS_THROUGH_WITH_BUFFERS>(ptr);

            if (false == sptwb.DataBuf.Any(b => b != 0))
            {
                Marshal.FreeHGlobal(ptr);

                smartAttributes = null;
                return false;
            }

            Array.Copy(sptwb.DataBuf, buffer, buffer.Length);

            Marshal.FreeHGlobal(ptr);

            return FillSmartThreshold(storage, buffer, smartAttributes);
        }

        public static bool ControlSmartStatusSat(Storage storage, IntPtr handle, byte target, byte command, COMMAND_TYPE commandType)
        {
            var sptwb = new SCSI_PASS_THROUGH_WITH_BUFFERS();

            sptwb.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            sptwb.Spt.PathId = 0;
            sptwb.Spt.TargetId = 0;
            sptwb.Spt.Lun = 0;
            sptwb.Spt.SenseInfoLength = 24;
            sptwb.Spt.DataIn = InteropConstants.SCSI_IOCTL_DATA_IN;
            sptwb.Spt.DataTransferLength = 0;
            sptwb.Spt.TimeOutValue = 2;
            sptwb.Spt.DataBufferOffset = (ulong)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.DataBuf)).ToInt64();
            sptwb.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.SenseBuf)).ToInt32();

            switch (commandType)
            {
                case COMMAND_TYPE.CMD_TYPE_SAT:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = command;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 0;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 1;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_LOW;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = InteropConstants.SMART_CYL_HI;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_SAT_ASM1352R:
                    // PROTOCOL field should be "0Dh”SATA port0 and "0Eh" SATA port1.
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (0xE << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = command;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 0;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 1;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_LOW;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = InteropConstants.SMART_CYL_HI;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_NVME_REALTEK9220DP:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
                    sptwb.Spt.Cdb[1] = (3 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
                    sptwb.Spt.Cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2
                    sptwb.Spt.Cdb[3] = command;//FEATURES (7:0)
                    sptwb.Spt.Cdb[4] = 0;//SECTOR_COUNT (7:0)
                    sptwb.Spt.Cdb[5] = 1;//LBA_LOW (7:0)
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_LOW;//LBA_MID (7:0)
                    sptwb.Spt.Cdb[7] = InteropConstants.SMART_CYL_HI;//LBA_HIGH (7:0)
                    sptwb.Spt.Cdb[8] = target;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CMD;//COMMAND
                    break;
                case COMMAND_TYPE.CMD_TYPE_SUNPLUS:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xF8;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = 0x22;
                    sptwb.Spt.Cdb[3] = 0x10;
                    sptwb.Spt.Cdb[4] = 0x01;
                    sptwb.Spt.Cdb[5] = command;
                    sptwb.Spt.Cdb[6] = 0x01;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[10] = target;
                    sptwb.Spt.Cdb[11] = InteropConstants.SMART_CMD;
                    break;
                case COMMAND_TYPE.CMD_TYPE_IO_DATA:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xE3;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = command;
                    sptwb.Spt.Cdb[3] = 0x00;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[7] = target;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CMD;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = 0x00;
                    sptwb.Spt.Cdb[11] = 0x00;
                    break;
                case COMMAND_TYPE.CMD_TYPE_LOGITEC:
                    sptwb.Spt.CdbLength = 10;
                    sptwb.Spt.Cdb[0] = 0xE0;
                    sptwb.Spt.Cdb[1] = 0x00;
                    sptwb.Spt.Cdb[2] = command;
                    sptwb.Spt.Cdb[3] = 0x00;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[6] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[7] = target;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CMD;
                    sptwb.Spt.Cdb[9] = 0x4C;
                    break;
                case COMMAND_TYPE.CMD_TYPE_PROLIFIC:
                    sptwb.Spt.CdbLength = 16;
                    sptwb.Spt.Cdb[0] = 0xD8;
                    sptwb.Spt.Cdb[1] = 0x15;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = command;
                    sptwb.Spt.Cdb[4] = 0x06;
                    sptwb.Spt.Cdb[5] = 0x7B;
                    sptwb.Spt.Cdb[6] = 0x00;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = 0x02;
                    sptwb.Spt.Cdb[9] = 0x00;
                    sptwb.Spt.Cdb[10] = 0x01;
                    sptwb.Spt.Cdb[11] = 0x00;
                    sptwb.Spt.Cdb[12] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[13] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[14] = target;
                    sptwb.Spt.Cdb[15] = InteropConstants.SMART_CMD;
                    break;
                case COMMAND_TYPE.CMD_TYPE_JMICRON:
                    sptwb.Spt.CdbLength = 12;
                    sptwb.Spt.Cdb[0] = 0xDF;
                    sptwb.Spt.Cdb[1] = 0x10;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0x02;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = command;
                    sptwb.Spt.Cdb[6] = 0x01;
                    sptwb.Spt.Cdb[7] = 0x01;
                    sptwb.Spt.Cdb[8] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[10] = target;
                    sptwb.Spt.Cdb[11] = InteropConstants.SMART_CMD;
                    break;
                case COMMAND_TYPE.CMD_TYPE_CYPRESS:
                    sptwb.Spt.CdbLength = 16;
                    sptwb.Spt.Cdb[0] = 0x24;
                    sptwb.Spt.Cdb[1] = 0x24;
                    sptwb.Spt.Cdb[2] = 0x00;
                    sptwb.Spt.Cdb[3] = 0xBE;
                    sptwb.Spt.Cdb[4] = 0x00;
                    sptwb.Spt.Cdb[5] = 0x00;
                    sptwb.Spt.Cdb[6] = command;
                    sptwb.Spt.Cdb[7] = 0x00;
                    sptwb.Spt.Cdb[8] = 0x00;
                    sptwb.Spt.Cdb[9] = InteropConstants.SMART_CYL_LOW;
                    sptwb.Spt.Cdb[10] = InteropConstants.SMART_CYL_HI;
                    sptwb.Spt.Cdb[11] = target;
                    sptwb.Spt.Cdb[12] = InteropConstants.SMART_CMD;
                    sptwb.Spt.Cdb[13] = 0x00;
                    sptwb.Spt.Cdb[14] = 0x00;
                    sptwb.Spt.Cdb[15] = 0x00;
                    break;
                default:
                    return false;
            }

            var length = (int)(Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(sptwb.DataBuf)).ToInt32() + sptwb.Spt.DataTransferLength);

            var ptrSize = Marshal.SizeOf<SCSI_PASS_THROUGH_WITH_BUFFERS>();
            var ptr = Marshal.AllocHGlobal(ptrSize);
            Marshal.StructureToPtr(sptwb, ptr, false);

            var ok = Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_PASS_THROUGH, ptr, Marshal.SizeOf<SCSI_PASS_THROUGH>(), ptr, length, out _, IntPtr.Zero);

            Marshal.FreeHGlobal(ptr);
            return ok;
        }

        public static bool GetSmartAttributeMegaRAID(Storage storage, IntPtr handle, byte[] buffer, out List<SmartAttributeStructure> smartAttributes)
        {
            var irDriveRegs = new IDEREGS();

            irDriveRegs.bFeaturesReg = InteropConstants.READ_ATTRIBUTES;
            irDriveRegs.bSectorCountReg = 1;
            irDriveRegs.bSectorNumberReg = 0;
            irDriveRegs.bCylLowReg = InteropConstants.SMART_CYL_LOW;
            irDriveRegs.bCylHighReg = InteropConstants.SMART_CYL_HI;
            irDriveRegs.bDriveHeadReg = 0;
            irDriveRegs.bCommandReg = InteropConstants.SMART_CMD;

            var cdb = new byte[16];
            byte cdbLength = 12;

            cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
            cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
            cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2

            var span = cdb.AsSpan(3);
            MemoryMarshal.Write(span, ref irDriveRegs);

            if (!ATAMethods.SendPassThroughCommandMegaRAID(handle, buffer, cdb, cdbLength))
            {
                smartAttributes = null;
                return false;
            }

            return FillSmartData(storage, buffer, out smartAttributes);
        }

        public static bool GetSmartThresholdMegaRAID(Storage storage, IntPtr handle, byte[] buffer, List<SmartAttributeStructure> smartAttributes)
        {
            var irDriveRegs = new IDEREGS();

            irDriveRegs.bFeaturesReg = InteropConstants.READ_THRESHOLDS;
            irDriveRegs.bSectorCountReg = 1;
            irDriveRegs.bSectorNumberReg = 0;
            irDriveRegs.bCylLowReg = InteropConstants.SMART_CYL_LOW;
            irDriveRegs.bCylHighReg = InteropConstants.SMART_CYL_HI;
            irDriveRegs.bCommandReg = InteropConstants.SMART_CMD;

            var cdb = new byte[16];
            byte cdbLength = 12;

            cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
            cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
            cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2

            var span = cdb.AsSpan(3);
            MemoryMarshal.Write(span, ref irDriveRegs);

            if (!ATAMethods.SendPassThroughCommandMegaRAID(handle, buffer, cdb, cdbLength))
            {
                return false;
            }

            return FillSmartThreshold(storage, buffer, smartAttributes);
        }

        public static bool ControlSmartStatusMegaRAID(Storage storage, IntPtr handle, byte command)
        {
            var irDriveRegs = new IDEREGS();

            irDriveRegs.bFeaturesReg = command;
            irDriveRegs.bSectorCountReg = 1;
            irDriveRegs.bSectorNumberReg = 0;
            irDriveRegs.bCylLowReg = InteropConstants.SMART_CYL_LOW;
            irDriveRegs.bCylHighReg = InteropConstants.SMART_CYL_HI;
            irDriveRegs.bCommandReg = InteropConstants.SMART_CMD;

            var cdb = new byte[16];
            byte cdbLength = 12;

            cdb[0] = 0xA1;//ATA PASS THROUGH(12) OPERATION CODE(A1h)
            cdb[1] = (4 << 1) | 0; //MULTIPLE_COUNT=0,PROTOCOL=4(PIO Data-In),Reserved
            cdb[2] = (1 << 3) | (1 << 2) | 2;//OFF_LINE=0,CK_COND=0,Reserved=0,T_DIR=1(ToDevice),BYTE_BLOCK=1,T_LENGTH=2

            var span = cdb.AsSpan(3);
            MemoryMarshal.Write(span, ref irDriveRegs);

            return ATAMethods.SendPassThroughCommandMegaRAID(handle, null, cdb, cdbLength);
        }

        public static void HandleAttribute(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            switch (attribute.ID)
            {
                case 0x09: //Power on hours
                    Handle_09(storage, smart, attribute);
                    break;
                case 0x0C: //Power on count
                    Handle_0C(storage, smart, attribute);
                    break;
                case 0xBE:
                    Handle_BE(storage, smart, attribute);
                    break;
                case 0xBF: // Clean PowerOff Count for Sandisk/WD CloudSpeed SSD
                    Handle_BF(storage, smart, attribute);
                    break;
                case 0xC0: // UnClean PowerOff Count for Sandisk/WD CloudSpeed SSD
                    Handle_C0(storage, smart, attribute);
                    break;
                case 0xC2: //Temperature
                    Handle_C2(storage, smart, attribute);
                    break;
                case 0xF3: // Temperature for YMTC
                    Handle_F3(storage, smart, attribute);
                    break;
                case 0xBB:
                    Handle_BB(storage, smart, attribute);
                    break;
                case 0xCA:
                    Handle_CA(storage, smart, attribute);
                    break;
                case 0xD1:
                    Handle_D1(storage, smart, attribute);
                    break;
                case 0xC9:
                    Handle_C9(storage, smart, attribute);
                    break;
                case 0xE6:
                    Handle_E6(storage, smart, attribute);
                    break;
                case 0xE8:
                    Handle_E8(storage, smart, attribute);
                    break;
                case 0xE9:
                    Handle_E9(storage, smart, attribute);
                    break;
                case 0xE1:
                    Handle_E1(storage, smart, attribute);
                    break;
                case 0xEA:
                    Handle_EA(storage, smart, attribute);
                    break;
                case 0xEB:
                    Handle_EB(storage, smart, attribute);
                    break;
                case 0xF1:
                    Handle_F1(storage, smart, attribute);
                    break;
                case 0xF2:
                    Handle_F2(storage, smart, attribute);
                    break;
                case 0xF9:
                    Handle_F9(storage, smart, attribute);
                    break;
                case 0xFA:
                    Handle_FA(storage, smart, attribute);
                    break;
                case 0x64:
                    Handle_64(storage, smart, attribute);
                    break;
                case 0xAD:
                    Handle_AD(storage, smart, attribute);
                    break;
                case 0xB1:
                    Handle_B1(storage, smart, attribute);
                    break;
                case 0xE7:
                    Handle_E7(storage, smart, attribute);
                    break;
                case 0xA9:
                    Handle_A9(storage, smart, attribute);
                    break;
                case 0xC6:
                    Handle_C6(storage, smart, attribute);
                    break;
                case 0xC7:
                    Handle_C7(storage, smart, attribute);
                    break;
                case 0xF5:
                    Handle_F5(storage, smart, attribute);
                    break;
                case 0xF6:
                    Handle_F6(storage, smart, attribute);
                    break;
                default:
                    break;
            }
        }

        #endregion

        #region Private

        static void Handle_09(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            var rawValue = attribute.RawValueUInt;

            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIndilinx)
            {
                rawValue = (uint)(attribute.WorstValue * 256 + attribute.CurrentValue);
            }
            else if (storage.DetectedTimeUnitType == TimeUnitType.PowerOnMilliSeconds
                  || (storage.DetectedTimeUnitType == TimeUnitType.PowerOnHours && rawValue >= 0x0DA000)
                  || (Storage.ModelContains(storage, "Intel")) && rawValue >= 0x0DA000)
            {
                storage.MeasuredTimeUnitType = TimeUnitType.PowerOnMilliSeconds;

                rawValue = (uint)(attribute.RawValue[2] * 256 * 256
                                + attribute.RawValue[1] * 256
                                + attribute.RawValue[0] - 0x0DA753);

                if (rawValue < 0)
                {
                    rawValue = 0;
                }
            }

            smart.DetectedPowerOnHours = GetPowerOnHours(rawValue, storage.DetectedTimeUnitType);
            smart.MeasuredPowerOnHours = GetPowerOnHours(rawValue, storage.MeasuredTimeUnitType);
        }

        static void Handle_0C(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            var rawValue = attribute.RawValueUInt;

            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIndilinx)
            {
                rawValue = (uint)(attribute.WorstValue * 256 + attribute.CurrentValue);
            }

            smart.PowerOnCount = rawValue;
        }

        static void Handle_BE(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (attribute.RawValue[0] > 0 && attribute.RawValue[0] < 100)
            {
                smart.Temperature = attribute.RawValue[0];
            }
        }

        static void Handle_BF(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskCloud)
            {
                // Use Clean Shutdowns to calculate Power On Count
                smart.PowerOnCount = attribute.RawValueUInt + 1;
            }
        }

        static void Handle_C0(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskCloud)
            {
                // Use UnClean Shutdowns to calculate Power On Count
                smart.PowerOnCount += attribute.RawValueUInt;
            }
        }

        static void Handle_C2(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (Storage.ModelContains(storage, "SAMSUNG SV")
             && (attribute.RawValue[1] != 0 || attribute.RawValue[0] > 70))
            {
                smart.Temperature = (int)attribute.RawValueUInt / 10;
            }
            else if (attribute.RawValue[0] > 0 && storage.TemperatureMultiplier < 1.0f)
            {
                smart.Temperature = (int)Math.Round(attribute.RawValue[0] * storage.TemperatureMultiplier);
            }
            else if (attribute.RawValue[0] > 0)
            {
                smart.Temperature = attribute.RawValue[0];
            }

            if (smart.Temperature >= 100)
            {
                smart.Temperature = null;
            }
        }

        static void Handle_F3(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorYMTC)
            {
                if (attribute.RawValue[0] > 0)
                {
                    smart.Temperature = attribute.RawValue[0];
                }

                if (smart.Temperature >= 100)
                {
                    smart.Temperature = null;
                }
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntel)
            {
                smart.NandWrites = (int)(attribute.RawValueULong / 32);
            }
        }

        static void Handle_BB(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorMtron)
            {
                smart.Life = (sbyte)attribute.CurrentValue;

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_CA(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicron
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicronMU03
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntelDC
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotionCVC)
            {
                smart.Life = (sbyte)attribute.CurrentValue;

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_D1(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIndilinx)
            {
                smart.Life = (sbyte)attribute.CurrentValue;

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_C9(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHP
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHPVenus)
            {
                smart.Life = (sbyte)attribute.CurrentValue;

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_E6(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorWdc
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandisk)
            {
                if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeSanDiskUsbMemory))
                {
                    smart.Life = -1;
                }
                else if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeSanDisk0_1))
                {
                    smart.Life = (sbyte)(100 - (attribute.RawValue[1] * 256 + attribute.RawValue[0]) / 100);
                }
                else if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeSanDisk1))
                {
                    smart.Life = (sbyte)(100 - attribute.RawValue[1]);
                }
                else if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeSanDiskLenovo))
                {
                    smart.Life = (sbyte)attribute.CurrentValue;
                }
                else
                {
                    smart.Life = (sbyte)(100 - attribute.RawValue[1]);
                }

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovo
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskDell)
            {
                smart.Life = (sbyte)attribute.CurrentValue;

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_E8(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorPlextor)
            {
                smart.Life = (sbyte)attribute.CurrentValue;

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorOcz)
            {
                smart.HostWrites = attribute.RawValueULong / 2 / 1024 / 1024;
            }
        }

        static void Handle_E9(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntel
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorOcz
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorOczVector
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSkhynix)
            {
                if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeRawValue))
                {
                    smart.Life = (sbyte)attribute.RawValue[0];
                }
                else
                {
                    smart.Life = (sbyte)attribute.CurrentValue;
                }

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovoHelenVenus)
            {
                smart.Life = (sbyte)attribute.CurrentValue;

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
            else if ( (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandisk
                    || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovo
                    || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskCloud)
                    && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
            {
                if (storage.NandWritesUnit == NandWritesUnit.NandWrites1MB)
                {
                    smart.NandWrites = attribute.RawValueInt / 1024;
                }
                else
                {
                    smart.NandWrites = attribute.RawValueInt;
                }
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorPlextor
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorKingston
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorWdc
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSSSTC
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSeagate
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorYMTC
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotionCVC)
            {
                smart.NandWrites = attribute.RawValueInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorJMicron
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorAdataIndustrial)
            {
                smart.NandWrites = (int)(attribute.RawValueULong / 2 / 1024 / 1024);
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorMaxiotek)
            {
                if (storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWrites512B)
                {
                    smart.NandWrites = (int)(attribute.RawValueULong / 2 / 1024 / 1024);
                }
                else
                {
                    smart.NandWrites = attribute.RawValueInt;
                }
            }
        }

        static void Handle_E1(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntel)
            {
                smart.HostWrites = attribute.RawValueULong / 32;
            }
        }

        static void Handle_EA(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorKingston
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSeagate
             || (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSkhynix && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB))
            {
                smart.NandWrites = attribute.RawValueInt;
            }
        }

        static void Handle_EB(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntelDC)
            {
                smart.HostWrites = attribute.RawValueULong / 32;
            }
        }

        static void Handle_F1(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDGeneral)
            {
                switch (storage.HostReadsWritesUnit)
                {
                    case HostReadsWritesUnit.HostReadsWrites512B:
                        smart.HostWrites = attribute.RawValueULong / 2 / 1024 / 1024;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites1MB:
                        smart.HostWrites = attribute.RawValueULong / 1024;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites16MB:
                        smart.HostWrites = attribute.RawValueULong / 64;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites32MB:
                        smart.HostWrites = attribute.RawValueULong / 32;
                        break;
                    case HostReadsWritesUnit.HostReadsWritesGB:
                        smart.HostWrites = attribute.RawValueULong;
                        break;
                    default:
                        break;
                }
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorToshiba
                  && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
            {
                smart.HostWrites = attribute.RawValueUInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotionCVC
                  && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
            {
                smart.HostWrites = attribute.RawValueUInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntelDC)
            {
                smart.NandWrites = (int)(attribute.RawValueULong / 32);
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntel
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorToshiba
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorKioxia
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotion)
            {
                smart.HostWrites = attribute.RawValueULong / 32;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandforce
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorOczVector
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorCorsair
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorKingston
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorRealtek
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorWdc
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSSSTC
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSkhynix
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorPhison
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSeagate
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMarvell
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMaxiotek
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorYMTC
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSCY
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorRecadata
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicronMU03
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHP
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHPVenus
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovo
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovoHelenVenus
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskDell
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorAdataIndustrial
                  )
            {
                switch (storage.HostReadsWritesUnit)
                {
                    case HostReadsWritesUnit.HostReadsWrites512B:
                        smart.HostWrites = attribute.RawValueULong / 2 / 1024 / 1024;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites1MB:
                        smart.HostWrites = attribute.RawValueULong / 1024;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites16MB:
                        smart.HostWrites = attribute.RawValueULong / 64;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites32MB:
                        smart.HostWrites = attribute.RawValueULong / 32;
                        break;
                    default:
                        smart.HostWrites = attribute.RawValueULong;
                        break;
                }
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSamsung
                  && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
            {
                smart.HostWrites = attribute.RawValueUInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSamsung
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorAPACER
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorJMicron)
            {
                smart.HostWrites = attribute.RawValueULong / 2 / 1024 / 1024;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorPlextor)
            {
                smart.HostWrites = attribute.RawValueULong / 32;
            }
            else if ( (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandisk || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskCloud)
                    && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
            {
                smart.HostWrites = attribute.RawValueUInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandisk)
            {
                smart.HostWrites = attribute.RawValueULong / 2 / 1024 / 1024;
            }
        }

        static void Handle_F2(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDGeneral)
            {
                switch (storage.HostReadsWritesUnit)
                {
                    case HostReadsWritesUnit.HostReadsWrites512B:
                        smart.HostReads = attribute.RawValueULong / 2 / 1024 / 1024;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites16MB:
                        smart.HostReads = attribute.RawValueULong / 64;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites32MB:
                        smart.HostReads = attribute.RawValueULong / 32;
                        break;
                    case HostReadsWritesUnit.HostReadsWritesGB:
                        smart.HostReads = attribute.RawValueULong;
                        break;
                    default:
                        break;
                }
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorToshiba
                  && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
            {
                smart.HostReads = attribute.RawValueUInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotionCVC
                  && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
            {
                smart.HostReads = attribute.RawValueUInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntel
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorToshiba
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotion)
            {
                smart.HostReads = attribute.RawValueULong / 32;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandforce
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorOczVector
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorCorsair
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorKingston
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorRealtek
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorWdc
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSSSTC
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSkhynix
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSeagate
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMarvell
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMaxiotek
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorYMTC
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSCY
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorRecadata
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicronMU03
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHP
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHPVenus
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovo
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovoHelenVenus
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskDell
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorAdataIndustrial
                  )
            {
                switch (storage.HostReadsWritesUnit)
                {
                    case HostReadsWritesUnit.HostReadsWrites512B:
                        smart.HostReads = attribute.RawValueULong / 2 / 1024 / 1024;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites16MB:
                        smart.HostReads = attribute.RawValueULong / 64;
                        break;
                    case HostReadsWritesUnit.HostReadsWrites32MB:
                        smart.HostReads = attribute.RawValueULong / 32;
                        break;
                    default:
                        smart.HostReads = attribute.RawValueULong;
                        break;
                }
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSamsung
                  && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
            {
                smart.HostReads = attribute.RawValueUInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSamsung
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorJMicron)
            {
                smart.HostReads = attribute.RawValueULong / 2 / 1024 / 1024;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorPlextor)
            {
                smart.HostReads = attribute.RawValueULong / 32;
            }
            else if ((storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandisk || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskCloud)
                  && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
            {
                smart.HostReads = attribute.RawValueUInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandisk)
            {
                smart.HostReads = attribute.RawValueULong / 2 / 1024 / 1024;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorJMicron
                  || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicronMU03)
            {
                smart.Life = (sbyte)attribute.CurrentValue;

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_F9(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorIntel
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorRealtek
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorWdc
             || (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandisk && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWritesGB)
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHP
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskHPVenus
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskLenovoHelenVenus)
            {
                smart.NandWrites = attribute.RawValueInt;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorOczVector)
            {
                smart.NandWrites = (int)(attribute.RawValueULong / 64 / 1024);
            }
        }

        static void Handle_FA(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorRealtek)
            {
                smart.NandWrites = attribute.RawValueInt;
            }
        }

        static void Handle_64(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandforce)
            {
                smart.GBytesErased = attribute.RawValueInt;
            }
        }

        static void Handle_AD(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorToshiba
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorKioxia)
            {
                smart.Life = (sbyte)(attribute.CurrentValue - 100);

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_B1(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSamsung)
            {
                smart.WearLevelingCount = attribute.RawValueInt;

                smart.Life = (sbyte)attribute.CurrentValue;

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_E7(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandforce
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorCorsair
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorKingston
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSkhynix
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorRealtek
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandisk
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSSSTC
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorAPACER
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorJMicron
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorPhison
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSeagate
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMaxiotek
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorYMTC
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSCY
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorRecadata
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorAdataIndustrial
             )
            {
                if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeRawValueIncrement))
                {
                    smart.Life = (sbyte)(100 - attribute.RawValue[0]);
                }
                else if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeRawValue))
                {
                    smart.Life = (sbyte)(attribute.RawValue[0]);
                }
                else
                {
                    smart.Life = (sbyte)attribute.CurrentValue;
                }

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_A9(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorRealtek
             || (storage.ATAInfo.VendorID == VendorIDs.SSDVendorKingston && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWrites32MB)
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotion)
            {
                if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeRawValueIncrement))
                {
                    smart.Life = (sbyte)(100 - attribute.RawValue[0]);
                }
                else if (storage.ATAInfo.FlagLife.HasFlag(FlagLife.FlagLifeRawValue))
                {
                    smart.Life = (sbyte)(attribute.RawValue[0]);
                }
                else
                {
                    smart.Life = (sbyte)attribute.CurrentValue;
                }

                if (smart.Life < 0 || smart.Life > 100)
                {
                    smart.Life = -1;
                }
            }
        }

        static void Handle_C6(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorOczVector)
            {
                smart.HostReads = attribute.RawValueUInt;
            }
        }

        static void Handle_C7(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorOczVector)
            {
                smart.HostWrites = attribute.RawValueUInt;
            }
        }

        static void Handle_F5(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            // Percent Drive Life Remaining (SanDisk/WD CloudSpeed)
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSandiskCloud)
            {
                smart.Life = (sbyte)attribute.CurrentValue;
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicron)
            {
                //NAND Page Size = 8KBytes
                smart.NandWrites = (int)(attribute.RawValueULong * 8 / 1024 / 1024);
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicronMU03)
            {
                smart.NandWrites = (int)(attribute.RawValueULong / 32);
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorKingston
                  && storage.HostReadsWritesUnit == HostReadsWritesUnit.HostReadsWrites32MB)
            {
                smart.NandWrites = (int)(attribute.RawValueULong / 32);
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSiliconMotion)
            {
                smart.NandWrites = (int)(attribute.RawValueULong / 32);
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorSCY)
            {
                smart.NandWrites = (int)(attribute.RawValueULong / 32);
            }
            else if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorRecadata)
            {
                smart.NandWrites = (int)attribute.RawValueULong;
            }
        }

        static void Handle_F6(Storage storage, SmartInfo smart, SmartAttributeStructure attribute)
        {
            if (storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicron
             || storage.ATAInfo.VendorID == VendorIDs.SSDVendorMicronMU03)
            {
                smart.HostWrites = attribute.RawValueULong / 2 / 1024 / 1024;
            }
        }

        static uint GetPowerOnHours(uint rawValue, TimeUnitType timeUnitType)
        {
            switch (timeUnitType)
            {
                case TimeUnitType.PowerOnUnknown:
                    return 0;
                case TimeUnitType.PowerOnHours:
                    return rawValue;
                case TimeUnitType.PowerOnMinutes:
                    return rawValue / 60;
                case TimeUnitType.PowerOnHalfMinutes:
                    return rawValue / 120;
                case TimeUnitType.PowerOnSeconds:
                    return rawValue / 60 / 60;
                case TimeUnitType.PowerOn10Minutes:
                    return rawValue / 6;
                case TimeUnitType.PowerOnMilliSeconds:
                    return rawValue;
                default:
                    return rawValue;
            }
        }

        #endregion
    }
}
