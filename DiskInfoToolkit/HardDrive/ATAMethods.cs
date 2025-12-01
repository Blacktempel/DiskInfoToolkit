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

using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Interop.Structures;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.HardDrive
{
    internal static class ATAMethods
    {
        #region Public

        public static bool SendAtaCommandPd(IntPtr handle, byte target, byte main, byte sub, byte param, byte[] data)
        {
            if (ATAInfo.AtaPassThrough)
            {
                var ab = new ATA_PASS_THROUGH_EX_WITH_BUFFERS();
                ab.Apt.Length = (ushort)Marshal.SizeOf<ATA_PASS_THROUGH_EX>();
                ab.Apt.TimeOutValue = 2;

                var size = Marshal.OffsetOf<ATA_PASS_THROUGH_EX_WITH_BUFFERS>(nameof(ab.Buf));
                ab.Apt.DataBufferOffset = (ulong)size;

                if (data?.Length > 0)
                {
                    if (data.Length > ab.Buf.Length)
                    {
                        return false;
                    }

                    ab.Apt.AtaFlags = InteropConstants.ATA_FLAGS_DATA_IN;
                    ab.Apt.DataTransferLength = (uint)data.Length;
                    ab.Buf[0] = 0xCF; //Magic number

                    size += data.Length;
                }

                ab.Apt.CurrentTaskFile.bFeaturesReg    = sub;
                ab.Apt.CurrentTaskFile.bSectorCountReg = param;
                ab.Apt.CurrentTaskFile.bDriveHeadReg   = target;
                ab.Apt.CurrentTaskFile.bCommandReg     = main;

                if (main == InteropConstants.SMART_CMD)
                {
                    ab.Apt.CurrentTaskFile.bCylLowReg       = InteropConstants.SMART_CYL_LOW;
                    ab.Apt.CurrentTaskFile.bCylHighReg      = InteropConstants.SMART_CYL_HI;
                    ab.Apt.CurrentTaskFile.bSectorCountReg  = 1;
                    ab.Apt.CurrentTaskFile.bSectorNumberReg = 1;
                }

                var ptrSize = Marshal.SizeOf<ATA_PASS_THROUGH_EX_WITH_BUFFERS>();
                var ptr = Marshal.AllocHGlobal(ptrSize);
                Marshal.StructureToPtr(ab, ptr, false);

                if (!Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_ATA_PASS_THROUGH, ptr, ptrSize, ptr, ptrSize, out _, IntPtr.Zero))
                {
                    Marshal.FreeHGlobal(ptr);

                    return false;
                }
                else
                {
                    ab = Marshal.PtrToStructure<ATA_PASS_THROUGH_EX_WITH_BUFFERS>(ptr);

                    if (data != null)
                    {
                        Array.Copy(ab.Buf, data, data.Length);
                    }

                    Marshal.FreeHGlobal(ptr);
                }
            }
            else
            {
                var buf = new CMD_IDE_PATH_THROUGH();

                buf.reg.bFeaturesReg     = sub;
                buf.reg.bSectorCountReg  = param;
                buf.reg.bSectorNumberReg = 0;
                buf.reg.bCylLowReg       = 0;
                buf.reg.bCylHighReg      = 0;
                buf.reg.bDriveHeadReg    = target;
                buf.reg.bCommandReg      = main;
                buf.reg.bReserved        = 0;

                if (data != null)
                {
                    buf.length = (uint)data.Length;
                }

                var ptrSize = Marshal.SizeOf<CMD_IDE_PATH_THROUGH>();
                var ptr = Marshal.AllocHGlobal(ptrSize);
                Marshal.StructureToPtr(buf, ptr, false);

                if (!Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_IDE_PASS_THROUGH, ptr, ptrSize, ptr, ptrSize, out _, IntPtr.Zero))
                {
                    Marshal.FreeHGlobal(ptr);

                    return false;
                }
                else
                {
                    if (data != null)
                    {
                        var offset = Marshal.OffsetOf<CMD_IDE_PATH_THROUGH>(nameof(CMD_IDE_PATH_THROUGH.buffer)).ToInt32();

                        var bufferOffsetPtr = ptr + offset;

                        Marshal.Copy(bufferOffsetPtr, data, 0, data.Length);
                    }

                    Marshal.FreeHGlobal(ptr);
                }
            }

            return true;
        }

        public static unsafe bool SendAtaCommandCsmi(IntPtr handle, CSMI_SAS_PHY_ENTITY? sasPhyEntity, byte main, byte sub, byte param, byte[] data)
        {
            if (sasPhyEntity == null)
            {
                return false;
            }

            var spe = sasPhyEntity.Value;

            var csmiBuf = new CSMI_SAS_STP_PASSTHRU_BUFFER();

            var dataLength = data == null ? 0 : data.Length;

            var size = Marshal.SizeOf<CSMI_SAS_STP_PASSTHRU_BUFFER>() + dataLength;

            csmiBuf.Parameters.bPhyIdentifier  = spe.Attached.bPhyIdentifier;
            csmiBuf.Parameters.bPortIdentifier = spe.bPortIdentifier;

            Array.Copy(spe.Attached.bSASAddress, csmiBuf.Parameters.bDestinationSASAddress, spe.Attached.bSASAddress.Length);

            csmiBuf.Parameters.bConnectionRate = InteropConstants.CSMI_SAS_LINK_RATE_NEGOTIATED;

            if (main == 0xEF) //AAM / APM
            {
                csmiBuf.Parameters.uFlags = InteropConstants.CSMI_SAS_STP_UNSPECIFIED;
            }
            else
            {
                csmiBuf.Parameters.uFlags = InteropConstants.CSMI_SAS_STP_PIO | InteropConstants.CSMI_SAS_STP_READ;
            }

            csmiBuf.Parameters.uDataLength = (uint)dataLength;

            csmiBuf.Parameters.bCommandFIS[ 0] = 0x27; //Type: host-to-device FIS
            csmiBuf.Parameters.bCommandFIS[ 1] = 0x80; //Bit7: Update command register

            if (main == InteropConstants.SMART_CMD)
            {
                csmiBuf.Parameters.bCommandFIS[ 2] = main;
                csmiBuf.Parameters.bCommandFIS[ 3] = sub;
                csmiBuf.Parameters.bCommandFIS[ 4] = 0;
                csmiBuf.Parameters.bCommandFIS[ 5] = InteropConstants.SMART_CYL_LOW;
                csmiBuf.Parameters.bCommandFIS[ 6] = InteropConstants.SMART_CYL_HI;
                csmiBuf.Parameters.bCommandFIS[ 7] = 0xA0; // target
                csmiBuf.Parameters.bCommandFIS[ 8] = 0;
                csmiBuf.Parameters.bCommandFIS[ 9] = 0;
                csmiBuf.Parameters.bCommandFIS[10] = 0;
                csmiBuf.Parameters.bCommandFIS[11] = 0;
                csmiBuf.Parameters.bCommandFIS[12] = param;
                csmiBuf.Parameters.bCommandFIS[13] = 0;
            }
            else
            {
                csmiBuf.Parameters.bCommandFIS[ 2] = main;
                csmiBuf.Parameters.bCommandFIS[ 3] = sub;
                csmiBuf.Parameters.bCommandFIS[ 4] = 0;
                csmiBuf.Parameters.bCommandFIS[ 5] = 0;
                csmiBuf.Parameters.bCommandFIS[ 6] = 0;
                csmiBuf.Parameters.bCommandFIS[ 7] = 0xA0; // target
                csmiBuf.Parameters.bCommandFIS[ 8] = 0;
                csmiBuf.Parameters.bCommandFIS[ 9] = 0;
                csmiBuf.Parameters.bCommandFIS[10] = 0;
                csmiBuf.Parameters.bCommandFIS[11] = 0;
                csmiBuf.Parameters.bCommandFIS[12] = param;
                csmiBuf.Parameters.bCommandFIS[13] = 0;
            }

            if (!CsmiIoctl(handle, InteropConstants.CC_CSMI_SAS_STP_PASSTHRU, ref csmiBuf.IoctlHeader, size))
            {
                return false;
            }

            if (main != 0xEF && data != null)
            {
                fixed (byte *pData = csmiBuf.bDataBuffer)
                fixed (byte *pDest = data)
                {
                    Buffer.MemoryCopy(pData, pDest, data.LongLength, data.LongLength);
                }
            }

            return true;
        }

        public static bool CsmiIoctl(IntPtr handle, uint code, ref SRB_IO_CONTROL csmiBuf, int csmiBufSize)
        {
            //Determine signature
            string signature;

            switch (code)
            {
                case InteropConstants.CC_CSMI_SAS_GET_DRIVER_INFO:
                    signature = InteropConstants.CSMI_ALL_SIGNATURE;
                    break;
                case InteropConstants.CC_CSMI_SAS_GET_PHY_INFO:
                case InteropConstants.CC_CSMI_SAS_STP_PASSTHRU:
                    signature = InteropConstants.CSMI_SAS_SIGNATURE;
                    break;
                case InteropConstants.CC_CSMI_SAS_GET_RAID_INFO:
                case InteropConstants.CC_CSMI_SAS_GET_RAID_CONFIG:
                    signature = InteropConstants.CSMI_RAID_SIGNATURE;
                    break;
                default:
                    return false;
            }

            var srbSize = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();

            //Set header
            csmiBuf.HeaderLength = srbSize;

            var bytes = Encoding.ASCII.GetBytes(signature);
            Array.Copy(bytes, csmiBuf.Signature, csmiBuf.Signature.Length);

            csmiBuf.Timeout = InteropConstants.CSMI_SAS_TIMEOUT;
            csmiBuf.ControlCode = code;
            csmiBuf.ReturnCode = 0;
            csmiBuf.Length = (uint)(csmiBufSize - srbSize);

            var ptr = Marshal.AllocHGlobal((int)csmiBufSize);

            Marshal.StructureToPtr(csmiBuf, ptr, false);

            //Call function
            var ok = Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_MINIPORT, ptr, csmiBufSize, ptr, csmiBufSize, out _, IntPtr.Zero);

            if (ok)
            {
                csmiBuf = Marshal.PtrToStructure<SRB_IO_CONTROL>(ptr);
            }

            Marshal.FreeHGlobal(ptr);

            return ok;
        }

        public static bool SendPassThroughCommandMegaRAID(IntPtr handle, byte[] buffer, byte[] cdb, byte cdbLength)
        {
            if (cdb == null || cdbLength == 0)
            {
                return false;
            }

            var bufferLength = buffer == null ? 0 : buffer.Length;

            var mpti = new MEGARAID_PASS_THROUGH_IOCTL();
            var mptiSize = Marshal.SizeOf<MEGARAID_PASS_THROUGH_IOCTL>();

            mpti.SrbIoCtrl.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();

            Array.Copy(InteropConstants.RAID_SIG_STR_ARR, mpti.SrbIoCtrl.Signature, InteropConstants.RAID_SIG_STR_LEN);

            mpti.SrbIoCtrl.Timeout = 0;
            mpti.SrbIoCtrl.ControlCode = 0;
            mpti.SrbIoCtrl.Length = (uint)(mptiSize - mpti.DataBuf.Length + bufferLength);

            mpti.Mpt.Cmd = InteropConstants.MFI_CMD_PD_SCSI_IO;
            mpti.Mpt.CmdStatus = 0xFF;
            mpti.Mpt.ScsiStatus = 0x00;
            unchecked
            {
                mpti.Mpt.TargetId = (byte)-1;
            }
            mpti.Mpt.Lun = 0;
            mpti.Mpt.CdbLength = cdbLength;
            mpti.Mpt.TimeOutValue = 0;
            mpti.Mpt.Flags = InteropConstants.MFI_FRAME_DIR_READ;
            mpti.Mpt.DataTransferLength = (uint)bufferLength;

            Array.Copy(cdb, mpti.Mpt.Cdb, cdbLength);

            var ptr = Marshal.AllocHGlobal(mptiSize);
            Marshal.StructureToPtr(mpti, ptr, false);

            if (!Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_SCSI_MINIPORT, ptr, mptiSize, ptr, mptiSize, out var readSize, IntPtr.Zero))
            {
                Marshal.FreeHGlobal(ptr);
                return false;
            }

            if (readSize < (Marshal.SizeOf<SRB_IO_CONTROL>() + Marshal.SizeOf<MEGARAID_DCOMD>()))
            {
                Marshal.FreeHGlobal(ptr);
                return false;
            }

            if (mpti.Mpt.CmdStatus != 0)
            {
                Marshal.FreeHGlobal(ptr);
                return false;
            }

            if ((readSize - mptiSize - mpti.DataBuf.Length) < mpti.Mpt.DataTransferLength)
            {
                Marshal.FreeHGlobal(ptr);
                return false;
            }

            if (buffer != null)
            {
                Array.Copy(mpti.DataBuf, buffer, mpti.Mpt.DataTransferLength);
            }

            Marshal.FreeHGlobal(ptr);
            return true;
        }

        #endregion
    }
}
