/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Interop.Linux;
using DiskInfoToolkit.Models;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DiskInfoToolkit.Core
{
    public sealed class LinuxStorageIoControl : IStorageIoControl
    {
        #region Fields

        private const int IdentifyBufferLength = 512;

        private const int DefaultTimeoutMilliseconds = 5000;

        private const byte AtaPassThrough16Command = 0x85;

        private const byte AtaProtocolNonData = 3;

        private const byte AtaProtocolPioDataIn = 4;

        private const byte AtaProtocolPioDataOut = 5;

        private const byte NvmeAdminIdentify = 0x06;

        private const byte NvmeAdminGetLogPage = 0x02;

        private const uint NvmeDataTypeIdentify = 1;

        private const uint NvmeDataTypeLogPage = 2;

        private const uint NvmeLogPageSmartHealthInformation = 2;

        private static readonly ConcurrentDictionary<IntPtr, string> HandlePaths = new ConcurrentDictionary<IntPtr, string>();

        #endregion

        #region Public

        public SafeFileHandle OpenDevice(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes)
        {
            int flags = ((desiredAccess & IoAccess.GenericWrite) != 0)
                ? LinuxNative.O_RDWR
                : LinuxNative.O_RDONLY;

            flags |= LinuxNative.O_CLOEXEC;

            SafeFileHandle handle = LinuxNative.OpenDevice(path, flags);
            if (handle != null && !handle.IsInvalid)
            {
                HandlePaths[handle.DangerousGetHandle()] = path ?? string.Empty;
            }

            return handle;
        }

        public bool SendRawIoControl(SafeFileHandle handle, uint ioControlCode, byte[] inBuffer, byte[] outBuffer, out int bytesReturned)
        {
            bytesReturned = 0;

            if (handle == null || handle.IsInvalid)
            {
                return false;
            }

            if (ioControlCode == IoControlCodes.IOCTL_STORAGE_QUERY_PROPERTY)
            {
                return TryHandleStorageQueryProperty(handle, inBuffer, outBuffer, out bytesReturned);
            }

            return SendLinuxRawIoctl(handle, ioControlCode, inBuffer, outBuffer, out bytesReturned);
        }

        public bool TryGetStorageDeviceDescriptor(SafeFileHandle handle, out StorageDeviceDescriptorInfo descriptor)
        {
            descriptor = new StorageDeviceDescriptorInfo();

            string blockName = GetBlockName(handle);
            if (string.IsNullOrWhiteSpace(blockName))
            {
                return false;
            }

            string deviceDirectory = GetSysBlockDeviceDirectory(blockName);

            descriptor.VendorID = ReadSysfsString(deviceDirectory, "vendor");
            descriptor.ProductID = StringUtil.FirstNonEmpty(ReadSysfsString(deviceDirectory, "model"), blockName);

            descriptor.ProductRevision = ReadSysfsString(deviceDirectory, "rev");
            if (string.IsNullOrWhiteSpace(descriptor.ProductRevision))
            {
                descriptor.ProductRevision = ReadSysfsString(deviceDirectory, "firmware_rev");
            }

            descriptor.SerialNumber = ReadSysfsString(deviceDirectory, "serial");
            descriptor.RemovableMedia = ReadSysfsString(GetSysBlockDirectory(blockName), "removable") == "1";
            descriptor.BusType = DetectBusType(blockName, deviceDirectory);

            return true;
        }

        public bool TryGetStorageAdapterDescriptor(SafeFileHandle handle, out StorageAdapterDescriptorInfo descriptor)
        {
            descriptor = new StorageAdapterDescriptorInfo();

            string blockName = GetBlockName(handle);
            if (string.IsNullOrWhiteSpace(blockName))
            {
                return false;
            }

            descriptor.BusType = DetectBusType(blockName, GetSysBlockDeviceDirectory(blockName));
            return descriptor.BusType != StorageBusType.Unknown;
        }

        public bool TryGetDriveLayout(SafeFileHandle handle, out byte[] rawLayout)
        {
            rawLayout = null;
            return false;
        }

        public bool TryGetScsiAddress(SafeFileHandle handle, out ScsiAddressInfo scsiAddress)
        {
            scsiAddress = new ScsiAddressInfo();

            string blockName = GetBlockName(handle);
            if (string.IsNullOrWhiteSpace(blockName))
            {
                return false;
            }

            string realDevicePath = ResolveRealPath(GetSysBlockDeviceDirectory(blockName));

            var match = Regex.Match(realDevicePath ?? string.Empty, @"(?<host>\d+):(?<bus>\d+):(?<target>\d+):(?<lun>\d+)");
            if (!match.Success)
            {
                return false;
            }

            scsiAddress.Length = Marshal.SizeOf<SCSI_ADDRESS>();
            scsiAddress.PortNumber = byte.Parse(match.Groups["host"].Value, CultureInfo.InvariantCulture);
            scsiAddress.PathID = byte.Parse(match.Groups["bus"].Value, CultureInfo.InvariantCulture);
            scsiAddress.TargetID = byte.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);
            scsiAddress.Lun = byte.Parse(match.Groups["lun"].Value, CultureInfo.InvariantCulture);
            return true;
        }

        public bool TryGetStorageDeviceNumber(SafeFileHandle handle, out StorageDeviceNumberInfo info)
        {
            info = new StorageDeviceNumberInfo();
            return false;
        }

        public bool TryGetDriveGeometryEx(SafeFileHandle handle, out DiskGeometryInfo info)
        {
            info = new DiskGeometryInfo();

            if (!TryIoctlUInt64(handle, LinuxNative.BLKGETSIZE64, out var diskSize))
            {
                return false;
            }

            info.DiskSize = diskSize;

            if (TryIoctlInt32(handle, LinuxNative.BLKSSZGET, out var sectorSize) && sectorSize > 0)
            {
                info.BytesPerSector = (uint)sectorSize;
            }

            return true;
        }

        public bool TryGetPredictFailure(SafeFileHandle handle, out PredictFailureInfo info)
        {
            info = new PredictFailureInfo();
            return false;
        }

        public bool TryGetSffDiskDeviceProtocol(SafeFileHandle handle, out StorageProtocolType protocolType)
        {
            protocolType = StorageProtocolType.Unknown;
            return false;
        }

        public bool TryGetSmartVersion(SafeFileHandle handle, out SmartVersionInfo info)
        {
            info = new SmartVersionInfo();

            if (!TryReadAtaIdentify(handle, out _))
            {
                return false;
            }

            info.Version = 1;
            info.Revision = 1;
            info.IdeDeviceMap = 1;
            info.Capabilities = 1;
            return true;
        }

        public bool TryScsiPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return SendLinuxRawIoctl(handle, LinuxNative.SG_IO, requestBuffer, responseBuffer, out bytesReturned);
        }

        public bool TryScsiMiniport(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            bytesReturned = 0;
            return false;
        }

        public bool TryAtaPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return TryWindowsAtaPassThroughOnLinux(handle, requestBuffer, responseBuffer, out bytesReturned);
        }

        public bool TryIdePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return TryWindowsAtaPassThroughOnLinux(handle, requestBuffer, responseBuffer, out bytesReturned);
        }

        public bool TrySmartReceiveDriveData(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return TryWindowsSmartReceiveOnLinux(handle, requestBuffer, responseBuffer, out bytesReturned);
        }

        public bool TrySmartSendDriveCommand(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return TryWindowsSmartSendOnLinux(handle, requestBuffer, responseBuffer, out bytesReturned);
        }

        public bool TryIntelNvmePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            bytesReturned = 0;
            return false;
        }

        #endregion

        #region Private

        private static bool SendLinuxRawIoctl(SafeFileHandle handle, uint request, byte[] inBuffer, byte[] outBuffer, out int bytesReturned)
        {
            bytesReturned = 0;

            int length = Math.Max(inBuffer != null ? inBuffer.Length : 0, outBuffer != null ? outBuffer.Length : 0);
            if (length <= 0)
            {
                return LinuxNative.ioctl(handle, (UIntPtr)request, IntPtr.Zero) == 0;
            }

            var nativeBuffer = Marshal.AllocHGlobal(length);
            try
            {
                for (int i = 0; i < length; ++i)
                {
                    Marshal.WriteByte(nativeBuffer, i, 0);
                }

                if (inBuffer != null && inBuffer.Length > 0)
                {
                    Marshal.Copy(inBuffer, 0, nativeBuffer, inBuffer.Length);
                }

                if (LinuxNative.ioctl(handle, (UIntPtr)request, nativeBuffer) != 0)
                {
                    return false;
                }

                if (outBuffer != null && outBuffer.Length > 0)
                {
                    Marshal.Copy(nativeBuffer, outBuffer, 0, outBuffer.Length);
                    bytesReturned = outBuffer.Length;
                }

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(nativeBuffer);
            }
        }

        private static bool TryWindowsAtaPassThroughOnLinux(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            bytesReturned = 0;
            if (requestBuffer == null || requestBuffer.Length < Marshal.SizeOf<ATA_PASS_THROUGH_EX>())
            {
                return false;
            }

            var apt = StructureHelper.FromBytes<ATA_PASS_THROUGH_EX>(requestBuffer);

            bool dataIn = (apt.AtaFlags & 0x02) != 0;
            bool dataOut = (apt.AtaFlags & 0x04) != 0;

            int dataLength = (int)Math.Min(apt.DataTransferLength, (uint)Math.Max(0, requestBuffer.Length));

            var data = new byte[Math.Max(0, dataLength)];
            if (dataOut && apt.DataBufferOffset < (ulong)requestBuffer.Length)
            {
                int sourceOffset = (int)apt.DataBufferOffset;
                int copyLength = Math.Min(data.Length, requestBuffer.Length - sourceOffset);

                Buffer.BlockCopy(requestBuffer, sourceOffset, data, 0, copyLength);
            }

            if (!ExecuteAtaCommand(handle, apt.CurrentTaskFile, dataIn, dataOut, data, Math.Max(1, (int)apt.TimeOutValue), out var returnedData))
            {
                return false;
            }

            if (responseBuffer != null && responseBuffer.Length > 0)
            {
                Buffer.BlockCopy(requestBuffer, 0, responseBuffer, 0, Math.Min(requestBuffer.Length, responseBuffer.Length));

                if (dataIn && apt.DataBufferOffset < (ulong)responseBuffer.Length && returnedData != null)
                {
                    int targetOffset = (int)apt.DataBufferOffset;
                    int copyLength = Math.Min(returnedData.Length, responseBuffer.Length - targetOffset);

                    Buffer.BlockCopy(returnedData, 0, responseBuffer, targetOffset, copyLength);
                }

                bytesReturned = responseBuffer.Length;
            }

            return true;
        }

        private static bool TryWindowsSmartReceiveOnLinux(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            bytesReturned = 0;
            if (requestBuffer == null || responseBuffer == null || requestBuffer.Length < Marshal.SizeOf<SENDCMDINPARAMS>())
            {
                return false;
            }

            var input = StructureHelper.FromBytes<SENDCMDINPARAMS>(requestBuffer);

            int dataLength = (int)Math.Max(0, input.cBufferSize);
            var data = new byte[dataLength];

            if (!ExecuteAtaCommand(handle, input.irDriveRegs, true, false, data, DefaultTimeoutMilliseconds / 1000, out var returnedData))
            {
                return false;
            }

            int dataOffset = Marshal.SizeOf<SENDCMDOUTPARAMS>() - 1;
            if (dataOffset < 0 || dataOffset >= responseBuffer.Length)
            {
                return false;
            }

            for (int i = 0; i < responseBuffer.Length; ++i)
            {
                responseBuffer[i] = 0;
            }

            var sizeBytes = BitConverter.GetBytes((uint)(returnedData != null ? returnedData.Length : 0));
            Buffer.BlockCopy(sizeBytes, 0, responseBuffer, 0, Math.Min(sizeBytes.Length, responseBuffer.Length));

            int copyLength = Math.Min(returnedData != null ? returnedData.Length : 0, responseBuffer.Length - dataOffset);
            if (copyLength > 0)
            {
                Buffer.BlockCopy(returnedData, 0, responseBuffer, dataOffset, copyLength);
            }

            bytesReturned = dataOffset + copyLength;
            return true;
        }

        private static bool TryWindowsSmartSendOnLinux(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            bytesReturned = 0;
            if (requestBuffer == null || requestBuffer.Length < Marshal.SizeOf<SENDCMDINPARAMS>())
            {
                return false;
            }

            var input = StructureHelper.FromBytes<SENDCMDINPARAMS>(requestBuffer);

            bool ok = ExecuteAtaCommand(handle, input.irDriveRegs, false, false, Array.Empty<byte>(), DefaultTimeoutMilliseconds / 1000, out _);
            if (ok && responseBuffer != null)
            {
                bytesReturned = responseBuffer.Length;
            }

            return ok;
        }

        private static bool TryReadAtaIdentify(SafeFileHandle handle, out byte[] identifyData)
        {
            identifyData = new byte[IdentifyBufferLength];

            var pinned = GCHandle.Alloc(identifyData, GCHandleType.Pinned);
            try
            {
                if (LinuxNative.ioctl(handle, (UIntPtr)LinuxNative.HDIO_GET_IDENTITY, pinned.AddrOfPinnedObject()) == 0)
                {
                    return true;
                }
            }
            finally
            {
                pinned.Free();
            }

            var regs = new IDEREGS();
            regs.bCommandReg = 0xEC;
            regs.bDriveHeadReg = 0xA0;

            return ExecuteAtaCommand(handle, regs, true, false, new byte[IdentifyBufferLength], DefaultTimeoutMilliseconds / 1000, out identifyData);
        }

        private static bool ExecuteAtaCommand(SafeFileHandle handle, IDEREGS regs, bool dataIn, bool dataOut, byte[] dataBuffer, int timeoutSeconds, out byte[] returnedData)
        {
            returnedData = null;
            dataBuffer = dataBuffer ?? Array.Empty<byte>();

            var cdb = BuildAtaPassThrough16Cdb(regs, dataIn, dataOut, dataBuffer.Length);
            var sense = new byte[32];

            var cdbHandle   = GCHandle.Alloc(cdb       , GCHandleType.Pinned);
            var senseHandle = GCHandle.Alloc(sense     , GCHandleType.Pinned);
            var dataHandle  = GCHandle.Alloc(dataBuffer, GCHandleType.Pinned);

            var hdrPtr = IntPtr.Zero;

            try
            {
                var hdr = new SG_IO_HDR();
                hdr.interface_id = (int)'S';
                hdr.dxfer_direction = dataBuffer.Length == 0
                    ? LinuxNative.SG_DXFER_NONE
                    : (dataIn ? LinuxNative.SG_DXFER_FROM_DEV : LinuxNative.SG_DXFER_TO_DEV);
                hdr.cmd_len = (byte)cdb.Length;
                hdr.mx_sb_len = (byte)sense.Length;
                hdr.dxfer_len = (uint)dataBuffer.Length;
                hdr.dxferp = dataBuffer.Length == 0 ? IntPtr.Zero : dataHandle.AddrOfPinnedObject();
                hdr.cmdp = cdbHandle.AddrOfPinnedObject();
                hdr.sbp = senseHandle.AddrOfPinnedObject();
                hdr.timeout = (uint)Math.Max(1, timeoutSeconds) * 1000;

                hdrPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SG_IO_HDR>());
                Marshal.StructureToPtr(hdr, hdrPtr, false);

                if (LinuxNative.ioctl(handle, (UIntPtr)LinuxNative.SG_IO, hdrPtr) != 0)
                {
                    return false;
                }

                var result = Marshal.PtrToStructure<SG_IO_HDR>(hdrPtr);
                if (result.status != 0 || result.host_status != 0 || result.driver_status != 0)
                {
                    return false;
                }

                returnedData = dataBuffer;
                return true;
            }
            finally
            {
                if (hdrPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(hdrPtr);
                }

                if (dataHandle.IsAllocated)
                {
                    dataHandle.Free();
                }

                if (senseHandle.IsAllocated)
                {
                    senseHandle.Free();
                }

                if (cdbHandle.IsAllocated)
                {
                    cdbHandle.Free();
                }
            }
        }

        private static byte[] BuildAtaPassThrough16Cdb(IDEREGS regs, bool dataIn, bool dataOut, int transferLength)
        {
            byte protocol = transferLength <= 0
                ? AtaProtocolNonData
                : (dataOut ? AtaProtocolPioDataOut : AtaProtocolPioDataIn);

            byte transferFlags = transferLength <= 0
                ? (byte)0
                : (byte)((dataIn ? 0x08 : 0x00) | 0x04 | 0x02);

            return new[]
            {
                AtaPassThrough16Command,
                (byte)(protocol << 1),
                transferFlags,
                (byte)0,
                regs.bFeaturesReg,
                (byte)0,
                regs.bSectorCountReg,
                (byte)0,
                regs.bSectorNumberReg,
                (byte)0,
                regs.bCylLowReg,
                (byte)0,
                regs.bCylHighReg,
                regs.bDriveHeadReg,
                regs.bCommandReg,
                (byte)0
            };
        }

        private static bool TryHandleStorageQueryProperty(SafeFileHandle handle, byte[] inBuffer, byte[] outBuffer, out int bytesReturned)
        {
            bytesReturned = 0;

            if (inBuffer == null || outBuffer == null)
            {
                return false;
            }

            int queryHeaderSize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY_NVME>();
            int protocolSize = Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();

            if (inBuffer.Length < queryHeaderSize + protocolSize || outBuffer.Length < queryHeaderSize + protocolSize)
            {
                return false;
            }

            var protocolBytes = new byte[protocolSize];
            Buffer.BlockCopy(inBuffer, queryHeaderSize, protocolBytes, 0, protocolSize);

            var protocol = StructureHelper.FromBytes<STORAGE_PROTOCOL_SPECIFIC_DATA>(protocolBytes);

            if (protocol.ProtocolType != 3)
            {
                return false;
            }

            byte[] data;
            if (protocol.DataType == NvmeDataTypeIdentify)
            {
                data = new byte[Math.Max(IdentifyBufferLength, (int)protocol.ProtocolDataLength)];
                if (!TryNvmeIdentify(handle, protocol.ProtocolDataRequestValue, protocol.ProtocolDataRequestSubValue, data))
                {
                    return false;
                }
            }
            else if (protocol.DataType == NvmeDataTypeLogPage && protocol.ProtocolDataRequestValue == NvmeLogPageSmartHealthInformation)
            {
                data = new byte[Math.Max(512, (int)protocol.ProtocolDataLength)];
                if (!TryNvmeGetLogPage(handle, protocol.ProtocolDataRequestValue, protocol.ProtocolDataRequestSubValue, data))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            int copyLength = Math.Min((int)protocol.ProtocolDataLength, data.Length);
            uint dataOffset = protocol.ProtocolDataOffset;

            if (dataOffset < (uint)protocolSize)
            {
                dataOffset = (uint)protocolSize;
            }

            int payloadOffset = queryHeaderSize + (int)dataOffset;
            if (payloadOffset < 0 || payloadOffset + copyLength > outBuffer.Length)
            {
                return false;
            }

            Buffer.BlockCopy(inBuffer, 0, outBuffer, 0, Math.Min(inBuffer.Length, outBuffer.Length));

            protocol.ProtocolDataOffset = dataOffset;
            protocol.ProtocolDataLength = (uint)copyLength;

            var responseProtocolBytes = StructureHelper.GetBytes(protocol);

            Buffer.BlockCopy(responseProtocolBytes, 0, outBuffer, queryHeaderSize, responseProtocolBytes.Length);
            Buffer.BlockCopy(data, 0, outBuffer, payloadOffset, copyLength);

            bytesReturned = payloadOffset + copyLength;

            return true;
        }

        private static bool TryNvmeIdentify(SafeFileHandle handle, uint cns, uint namespaceId, byte[] data)
        {
            uint nsid = cns == 0 ? NormalizeNvmeNamespaceId(namespaceId) : 0;
            return ExecuteNvmeAdminCommand(handle, NvmeAdminIdentify, nsid, cns, 0, data);
        }

        private static bool TryNvmeGetLogPage(SafeFileHandle handle, uint logPage, uint namespaceId, byte[] data)
        {
            uint dwords = (uint)Math.Max(1, data.Length / 4) - 1;
            uint cdw10 = (logPage & 0xFF) | (dwords << 16);
            uint nsid = NormalizeNvmeNamespaceId(namespaceId);

            return ExecuteNvmeAdminCommand(handle, NvmeAdminGetLogPage, nsid, cdw10, 0, data)
                || ExecuteNvmeAdminCommand(handle, NvmeAdminGetLogPage, 0xFFFFFFFF, cdw10, 0, data)
                || ExecuteNvmeAdminCommand(handle, NvmeAdminGetLogPage, 1, cdw10, 0, data);
        }

        private static uint NormalizeNvmeNamespaceId(uint namespaceId)
        {
            if (namespaceId == 0 || namespaceId == 0xFFFFFFFF)
            {
                return 1;
            }

            return namespaceId;
        }

        private static bool ExecuteNvmeAdminCommand(SafeFileHandle handle, byte opcode, uint namespaceId, uint cdw10, uint cdw11, byte[] data)
        {
            var dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
            var commandPtr = IntPtr.Zero;

            try
            {
                var command = new LINUX_NVME_ADMIN_CMD();
                command.opcode = opcode;
                command.nsid = namespaceId;
                command.addr = (ulong)dataHandle.AddrOfPinnedObject().ToInt64();
                command.data_len = (uint)data.Length;
                command.cdw10 = cdw10;
                command.cdw11 = cdw11;
                command.timeout_ms = DefaultTimeoutMilliseconds;

                commandPtr = Marshal.AllocHGlobal(Marshal.SizeOf<LINUX_NVME_ADMIN_CMD>());
                Marshal.StructureToPtr(command, commandPtr, false);

                return LinuxNative.ioctl(handle, (UIntPtr)LinuxNative.NVME_IOCTL_ADMIN_CMD, commandPtr) == 0;
            }
            finally
            {
                if (commandPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(commandPtr);
                }

                if (dataHandle.IsAllocated)
                {
                    dataHandle.Free();
                }
            }
        }

        private static bool TryIoctlUInt64(SafeFileHandle handle, uint request, out ulong value)
        {
            value = 0;
            var buffer = Marshal.AllocHGlobal(sizeof(ulong));
            try
            {
                Marshal.WriteInt64(buffer, 0);
                if (LinuxNative.ioctl(handle, (UIntPtr)request, buffer) != 0)
                {
                    return false;
                }

                value = (ulong)Marshal.ReadInt64(buffer);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool TryIoctlInt32(SafeFileHandle handle, uint request, out int value)
        {
            value = 0;
            var buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(buffer, 0);
                if (LinuxNative.ioctl(handle, (UIntPtr)request, buffer) != 0)
                {
                    return false;
                }

                value = Marshal.ReadInt32(buffer);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string GetBlockName(SafeFileHandle handle)
        {
            if (handle == null || handle.IsInvalid)
            {
                return string.Empty;
            }

            if (!HandlePaths.TryGetValue(handle.DangerousGetHandle(), out var path))
            {
                path = ResolveFileDescriptorPath(handle);
            }

            return GetBlockNameFromPath(path);
        }

        private static string GetBlockNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            if (Directory.Exists(GetSysBlockDirectory(name)))
            {
                return name;
            }

            string parent = TrimPartitionSuffix(name);

            return Directory.Exists(GetSysBlockDirectory(parent)) ? parent : name;
        }

        private static string TrimPartitionSuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            if (name.StartsWith("nvme", StringComparison.OrdinalIgnoreCase))
            {
                int index = name.LastIndexOf('p');
                return index > 0 ? name.Substring(0, index) : name;
            }

            int end = name.Length;
            while (end > 0 && char.IsDigit(name[end - 1]))
            {
                --end;
            }

            return end > 0 ? name.Substring(0, end) : name;
        }

        private static string ResolveFileDescriptorPath(SafeFileHandle handle)
        {
            string fdPath = "/proc/self/fd/" + handle.DangerousGetHandle().ToInt64().ToString(CultureInfo.InvariantCulture);
            return ResolveLink(fdPath);
        }

        private static string GetSysBlockDirectory(string blockName)
        {
            return string.IsNullOrWhiteSpace(blockName) ? string.Empty : Path.Combine("/sys/block", blockName);
        }

        private static string GetSysBlockDeviceDirectory(string blockName)
        {
            return Path.Combine(GetSysBlockDirectory(blockName), "device");
        }

        private static StorageBusType DetectBusType(string blockName, string deviceDirectory)
        {
            string name = blockName ?? string.Empty;
            string path = ResolveRealPath(deviceDirectory);
            string driver = ResolveLink(SearchUpwardsForLink(deviceDirectory, "driver"));

            if (name.StartsWith("nvme", StringComparison.OrdinalIgnoreCase) || path.IndexOf("/nvme", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return StorageBusType.Nvme;
            }

            if (path.IndexOf("/usb", StringComparison.OrdinalIgnoreCase) >= 0 || driver.EndsWith("usb-storage", StringComparison.OrdinalIgnoreCase) || driver.EndsWith("uas", StringComparison.OrdinalIgnoreCase))
            {
                return StorageBusType.Usb;
            }

            if (driver.EndsWith("megaraid_sas", StringComparison.OrdinalIgnoreCase))
            {
                return StorageBusType.RAID;
            }

            if (path.IndexOf("/ata", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("/sata", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return StorageBusType.Sata;
            }

            if (name.StartsWith("sd", StringComparison.OrdinalIgnoreCase))
            {
                return StorageBusType.Scsi;
            }

            return StorageBusType.Unknown;
        }

        private static string SearchUpwardsForLink(string startDirectory, string linkName)
        {
            string current = ResolveRealPath(startDirectory);
            for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); ++i)
            {
                string candidate = Path.Combine(current, linkName);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            return string.Empty;
        }

        private static string ReadSysfsString(string directory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return string.Empty;
            }

            try
            {
                string path = Path.Combine(directory, fileName);
                return File.Exists(path) ? StringUtil.TrimStorageString(File.ReadAllText(path)) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveRealPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return string.Empty;
                }

                return ResolveSymlinkChain(Path.GetFullPath(path), 0);
            }
            catch
            {
                return path ?? string.Empty;
            }
        }

        private static string ResolveSymlinkChain(string path, int depth)
        {
            if (depth > 16 || string.IsNullOrWhiteSpace(path))
            {
                return path ?? string.Empty;
            }

            string target = ResolveLink(path);
            if (string.IsNullOrWhiteSpace(target))
            {
                return path;
            }

            if (!Path.IsPathRooted(target))
            {
                string parent = Path.GetDirectoryName(path) ?? string.Empty;
                target = Path.Combine(parent, target);
            }

            return ResolveSymlinkChain(Path.GetFullPath(target), depth + 1);
        }

        private static string ResolveLink(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var buffer = new byte[4096];

            var length = LinuxNative.readlink(path, buffer, (UIntPtr)buffer.Length);
            long count = length.ToInt64();

            return count > 0 && count < buffer.Length
                ? Encoding.UTF8.GetString(buffer, 0, (int)count)
                : string.Empty;
        }

        #endregion
    }
}
