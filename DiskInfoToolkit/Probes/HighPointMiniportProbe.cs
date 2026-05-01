/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

using OS = BlackSharp.Core.Platform.OperatingSystem;

namespace DiskInfoToolkit.Probes
{
    internal static class HighPointMiniportProbe
    {
        #region Public

        public static bool TryPopulate(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || !OS.IsWindows())
            {
                return false;
            }

            using (var session = new HighPointMiniportSession(ioControl))
            {
                return session.TryPopulate(device);
            }
        }

        #endregion

        #region Private

        private sealed class HighPointMiniportSession : IDisposable
        {
            #region Constructor

            public HighPointMiniportSession(IStorageIoControl ioControl)
            {
                _ioControl = ioControl;
                _directPortPath = string.Empty;
            }

            #endregion

            #region Fields

            private const uint IoControlScsiMiniport = 0x0004D008U;

            private const uint HptIoctlBase = 0x03702400U;

            private const uint HptIoctlGetVersion = HptIoctlBase + 0U * 4U;

            private const uint HptIoctlGetControllerCount = HptIoctlBase + 1U * 4U;

            private const uint HptIoctlGetControllerInfo = HptIoctlBase + 2U * 4U;

            private const uint HptIoctlGetDeviceInfo = HptIoctlBase + 5U * 4U;

            private const uint HptIoctlIdePassThrough = HptIoctlBase + 24U * 4U;

            private const uint HptIoctlGetDeviceInfoV2 = HptIoctlBase + 33U * 4U;

            private const uint HptIoctlGetDeviceInfoV3 = HptIoctlBase + 46U * 4U;

            private const uint HptIoctlGetControllerInfoV2 = HptIoctlBase + 47U * 4U;

            private const uint HptIoctlGetControllerInfoV3 = HptIoctlBase + 54U * 4U;

            private const uint HptIoctlGetDeviceInfoV4 = HptIoctlBase + 55U * 4U;

            private const uint HptIoctlScsiPassThrough = HptIoctlBase + 59U * 4U;

            private const uint HptIoctlGetPhysicalDevices = HptIoctlBase + 60U * 4U;

            private const uint HptIoctlIdePassThroughV2 = HptIoctlBase + 66U * 4U;

            private const int HptWrappedHeaderSize = 36;

            private const int HptSrbHeaderLength = 28;

            private const int HptMiniportTimeoutSeconds = 20;

            private const int HptMaxScsiPorts = 128;

            private const string HptSrbSignature = "HPT-CTRL";

            private readonly IStorageIoControl _ioControl;

            private SafeFileHandle _directPortHandle;

            private string _directPortPath;

            private int _lastDirectIoctlError;

            #endregion

            #region Public

            public void Dispose()
            {
                CloseDirectPort();
            }

            public bool TryPopulate(StorageDevice device)
            {
                if (device == null)
                {
                    return false;
                }

                try
                {
                    if (!EnsureDirectPort(device))
                    {
                        device.ProbeTrace.Add($"HighPoint miniport: HighPoint direct IOCTL port is not available (lastError={_lastDirectIoctlError}).");
                        return false;
                    }

                    uint version = DirectGetVersion();
                    int controllerCount = DirectGetControllerCount();

                    device.ProbeTrace.Add($"HighPoint miniport: HighPoint direct IOCTL port='{_directPortPath}', version=0x{version:X8}, controllers={controllerCount}.");

                    if (version == 0U || controllerCount < 0)
                    {
                        device.ProbeTrace.Add("HighPoint miniport: HighPoint direct IOCTL interface was opened, but the runtime command path is not usable for this controller/driver instance.");
                    }

                    bool success = false;
                    if (controllerCount > 0)
                    {
                        success |= TryPopulateControllerInfo(device, 0);
                    }

                    var ids = new uint[128];
                    int deviceCount = DirectGetPhysicalDevices(ids, ids.Length);

                    if (deviceCount < 0)
                    {
                        device.ProbeTrace.Add($"HighPoint miniport: HighPoint physical device query failed with {deviceCount}.");
                    }

                    if (deviceCount > 0)
                    {
                        device.ProbeTrace.Add($"HighPoint miniport: HighPoint physical devices reported={deviceCount}.");
                        if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                        {
                            device.Controller.Kind = StorageTextConstants.HighPoint;
                        }

                        if (TryFindDeviceIdForStorageDevice(device, ids, deviceCount, out uint selectedDeviceId))
                        {
                            device.ProbeTrace.Add($"HighPoint miniport: HighPoint selected physical device id={selectedDeviceId} for this storage device.");

                            if (TryPopulateDeviceInfo(device, selectedDeviceId))
                            {
                                success = true;
                            }

                            if (!HasUsefulDeviceIdentity(device) || !device.SupportsSmart)
                            {
                                ProbePassThrough(device, new[] { selectedDeviceId }, 1);
                                if (HasUsefulDeviceIdentity(device) || device.SupportsSmart)
                                {
                                    success = true;
                                }
                            }
                        }
                        else
                        {
                            device.ProbeTrace.Add("HighPoint miniport: HighPoint could not map this StorageDevice to one unique physical device; skipping direct identity/SMART assignment to avoid copying the wrong disk data.");
                        }
                    }

                    if (success)
                    {
                        if (device.Controller.Family == StorageControllerFamily.Unknown)
                        {
                            device.Controller.Family = StorageControllerFamily.RocketRaid;
                        }

                        if (device.TransportKind == StorageTransportKind.Unknown)
                        {
                            device.TransportKind = StorageTransportKind.Raid;
                        }

                        device.ProbeTrace.Add("HighPoint miniport: HighPoint probe produced controller data.");
                        return true;
                    }

                    device.ProbeTrace.Add("HighPoint miniport: HighPoint direct IOCTL path resolved but no matching device/controller data was obtained.");
                    return false;
                }
                finally
                {
                    CloseDirectPort();
                }
            }

            #endregion

            #region Private

            private struct HighPointDeviceInfoSnapshot
            {
                public uint DeviceID;

                public byte Type;

                public ulong Capacity;

                public bool HasDeviceAddress;

                public byte ControllerID;

                public byte PathID;

                public byte TargetID;

                public bool HasLogicalAddress;

                public byte VBusID;

                public byte LogicalTargetID;

                public string Model;

                public string Serial;

                public string Firmware;
            }

            private static void WriteUInt32(byte[] buffer, int offset, uint value)
            {
                var bytes = BitConverter.GetBytes(value);
                Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
            }

            private static bool HasUsefulDeviceIdentity(StorageDevice device)
            {
                return !string.IsNullOrWhiteSpace(device.ProductName)
                    || !string.IsNullOrWhiteSpace(device.SerialNumber)
                    || device.DiskSizeBytes.GetValueOrDefault() > 0;
            }

            private static string DecodeSwappedWords(ushort[] words)
            {
                if (words == null)
                {
                    return string.Empty;
                }

                var bytes = new byte[words.Length * 2];
                for (int i = 0; i < words.Length; ++i)
                {
                    ushort value = words[i];
                    bytes[i * 2] = (byte)(value >> 8);
                    bytes[i * 2 + 1] = (byte)(value & 0xFF);
                }

                return StringUtil.TrimStorageString(Encoding.ASCII.GetString(bytes));
            }

            private static string DecodeAscii(byte[] data)
            {
                if (data == null)
                {
                    return string.Empty;
                }

                int end = 0;
                while (end < data.Length && data[end] != 0)
                {
                    ++end;
                }

                return StringUtil.TrimStorageString(Encoding.ASCII.GetString(data, 0, end));
            }

            private bool TryPopulateControllerInfo(StorageDevice device, int controllerId)
            {
                if (DirectGetControllerInfoV3(controllerId, out var infoV3) == 0)
                {
                    ApplyControllerInfo(device, infoV3.ProductID, infoV3.VendorID, infoV3.NumBuses);
                    return true;
                }

                if (DirectGetControllerInfoV2(controllerId, out var infoV2) == 0)
                {
                    ApplyControllerInfo(device, infoV2.ProductID, infoV2.VendorID, 0);
                    return true;
                }

                if (DirectGetControllerInfo(controllerId, out var info) == 0)
                {
                    ApplyControllerInfo(device, info.ProductID, info.VendorID, info.NumBuses);
                    return true;
                }

                return false;
            }

            private void ApplyControllerInfo(StorageDevice device, byte[] productBytes, byte[] vendorBytes, byte busCount)
            {
                var product = DecodeAscii(productBytes);
                var vendor  = DecodeAscii(vendorBytes);

                if (!string.IsNullOrWhiteSpace(product))
                {
                    device.Controller.Name = product;
                    if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                    {
                        device.Controller.Kind = product;
                    }
                }

                if (!string.IsNullOrWhiteSpace(vendor) && string.IsNullOrWhiteSpace(device.VendorName))
                {
                    device.VendorName = vendor;
                }

                if (!string.IsNullOrWhiteSpace(vendor) || !string.IsNullOrWhiteSpace(product))
                {
                    device.ProbeTrace.Add($"HighPoint miniport: HighPoint controller vendor='{vendor}', product='{product}', buses={busCount}.");
                }
            }

            private bool TryPopulateDeviceInfo(StorageDevice device, uint deviceId)
            {
                if (!TryGetDeviceInfoSnapshot(deviceId, out var snapshot))
                {
                    return false;
                }

                ApplyLogicalDeviceInfo(device, snapshot);
                return true;
            }

            private bool TryFindDeviceIdForStorageDevice(StorageDevice device, uint[] ids, int count, out uint deviceId)
            {
                deviceId = 0U;

                if (ids == null || count <= 0)
                {
                    return false;
                }

                var snapshots = new List<HighPointDeviceInfoSnapshot>();

                int nonZeroDeviceIds = 0;
                uint onlyDeviceId = 0U;

                for (int i = 0; i < count && i < ids.Length; ++i)
                {
                    uint candidateId = ids[i];
                    if (candidateId == 0U)
                    {
                        continue;
                    }

                    ++nonZeroDeviceIds;
                    onlyDeviceId = candidateId;

                    if (TryGetDeviceInfoSnapshot(candidateId, out var snapshot))
                    {
                        snapshots.Add(snapshot);
                    }
                }

                if (nonZeroDeviceIds == 1)
                {
                    deviceId = onlyDeviceId;
                    return true;
                }

                if (device != null && snapshots.Count > 0)
                {
                    if (device.Scsi.PathID.HasValue && device.Scsi.TargetID.HasValue)
                    {
                        byte pathId = device.Scsi.PathID.Value;
                        byte targetId = device.Scsi.TargetID.Value;

                        if (TryFindUniqueSnapshot(snapshots, s => s.HasLogicalAddress && s.VBusID == pathId && s.LogicalTargetID == targetId, out var matchedByLogicalAddress))
                        {
                            deviceId = matchedByLogicalAddress.DeviceID;
                            return true;
                        }

                        if (TryFindUniqueSnapshot(snapshots, s => s.HasDeviceAddress && s.PathID == pathId && s.TargetID == targetId, out var matchedByPhysicalAddress))
                        {
                            deviceId = matchedByPhysicalAddress.DeviceID;
                            return true;
                        }
                    }

                    if (device.Scsi.TargetID.HasValue)
                    {
                        byte targetId = device.Scsi.TargetID.Value;

                        if (TryFindUniqueSnapshot(snapshots, s => s.HasLogicalAddress && s.LogicalTargetID == targetId, out var matchedByLogicalTarget))
                        {
                            deviceId = matchedByLogicalTarget.DeviceID;
                            return true;
                        }

                        if (TryFindUniqueSnapshot(snapshots, s => s.HasDeviceAddress && s.TargetID == targetId, out var matchedByPhysicalTarget))
                        {
                            deviceId = matchedByPhysicalTarget.DeviceID;
                            return true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(device.SerialNumber))
                    {
                        string serial = NormalizeIdentityString(device.SerialNumber);
                        if (TryFindUniqueSnapshot(snapshots, s => NormalizeIdentityString(s.Serial).Equals(serial, StringComparison.OrdinalIgnoreCase), out var matchedBySerial))
                        {
                            deviceId = matchedBySerial.DeviceID;
                            return true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(device.ProductName))
                    {
                        string product = NormalizeIdentityString(device.ProductName);
                        if (TryFindUniqueSnapshot(snapshots, s => NormalizeIdentityString(s.Model).Equals(product, StringComparison.OrdinalIgnoreCase), out var matchedByModel))
                        {
                            deviceId = matchedByModel.DeviceID;
                            return true;
                        }
                    }
                }

                if (count == 1 && ids[0] != 0U)
                {
                    deviceId = ids[0];
                    return true;
                }

                return false;
            }

            private static bool TryFindUniqueSnapshot(List<HighPointDeviceInfoSnapshot> snapshots, Func<HighPointDeviceInfoSnapshot, bool> predicate, out HighPointDeviceInfoSnapshot match)
            {
                match = default;

                bool found = false;
                for (int i = 0; i < snapshots.Count; ++i)
                {
                    if (!predicate(snapshots[i]))
                    {
                        continue;
                    }

                    if (found)
                    {
                        match = default;
                        return false;
                    }

                    match = snapshots[i];
                    found = true;
                }

                return found;
            }

            private bool TryGetDeviceInfoSnapshot(uint deviceId, out HighPointDeviceInfoSnapshot snapshot)
            {
                if (DirectGetDeviceInfoV4(deviceId, out var infoV4) == 0)
                {
                    snapshot = CreateDeviceInfoSnapshot(deviceId, infoV4.Type, infoV4.Capacity, infoV4.Device);
                    snapshot.HasLogicalAddress = infoV4.TargetID != 0xFF;
                    snapshot.VBusID = infoV4.VBusID;
                    snapshot.LogicalTargetID = infoV4.TargetID;
                    return true;
                }

                if (DirectGetDeviceInfoV3(deviceId, out var infoV3) == 0)
                {
                    snapshot = CreateDeviceInfoSnapshot(deviceId, infoV3.Type, infoV3.Capacity, infoV3.Device);
                    snapshot.HasLogicalAddress = infoV3.TargetID != 0xFF;
                    snapshot.VBusID = infoV3.VBusID;
                    snapshot.LogicalTargetID = infoV3.TargetID;
                    return true;
                }

                if (DirectGetDeviceInfoV2(deviceId, out var infoV2) == 0)
                {
                    snapshot = CreateDeviceInfoSnapshot(deviceId, infoV2.Type, infoV2.Capacity, infoV2.Device);
                    return true;
                }

                if (DirectGetDeviceInfo(deviceId, out var info) == 0)
                {
                    snapshot = CreateDeviceInfoSnapshot(deviceId, info.Type, info.Capacity, info.Device);
                    return true;
                }

                snapshot = default;
                return false;
            }

            private static HighPointDeviceInfoSnapshot CreateDeviceInfoSnapshot(uint deviceId, byte type, ulong capacity, HPT_DEVICE_INFO info)
            {
                return new HighPointDeviceInfoSnapshot
                {
                    DeviceID = deviceId,
                    Type = type,
                    Capacity = capacity,
                    HasDeviceAddress = true,
                    ControllerID = info.ControllerID,
                    PathID = info.PathID,
                    TargetID = info.TargetID,
                    Model = DecodeSwappedWords(info.IdentifyData.ModelNumber),
                    Serial = DecodeSwappedWords(info.IdentifyData.SerialNumber),
                    Firmware = DecodeSwappedWords(info.IdentifyData.FirmwareRevision)
                };
            }

            private static HighPointDeviceInfoSnapshot CreateDeviceInfoSnapshot(uint deviceId, byte type, ulong capacity, HPT_DEVICE_INFO_V2 info)
            {
                return new HighPointDeviceInfoSnapshot
                {
                    DeviceID = deviceId,
                    Type = type,
                    Capacity = capacity,
                    HasDeviceAddress = true,
                    ControllerID = info.ControllerID,
                    PathID = info.PathID,
                    TargetID = info.TargetID,
                    Model = DecodeSwappedWords(info.IdentifyData.ModelNumber),
                    Serial = DecodeSwappedWords(info.IdentifyData.SerialNumber),
                    Firmware = DecodeSwappedWords(info.IdentifyData.FirmwareRevision)
                };
            }

            private static string NormalizeIdentityString(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return string.Empty;
                }

                return StringUtil.TrimStorageString(value).Replace(" ", string.Empty);
            }

            private void ApplyLogicalDeviceInfo(StorageDevice device, HighPointDeviceInfoSnapshot info)
            {
                if (!string.IsNullOrWhiteSpace(info.Model))
                {
                    device.ProductName = info.Model;
                    if (string.IsNullOrWhiteSpace(device.DisplayName) || device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase))
                    {
                        device.DisplayName = info.Model;
                    }
                }

                if (!string.IsNullOrWhiteSpace(info.Serial))
                {
                    device.SerialNumber = info.Serial;
                }

                if (!string.IsNullOrWhiteSpace(info.Firmware))
                {
                    device.ProductRevision = info.Firmware;
                }

                if (info.Capacity > 0 && device.DiskSizeBytes.GetValueOrDefault() == 0)
                {
                    device.DiskSizeBytes = info.Capacity;
                }

                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }

                if (device.Controller.Family == StorageControllerFamily.Unknown)
                {
                    device.Controller.Family = StorageControllerFamily.RocketRaid;
                }

                if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                {
                    device.Controller.Kind = StorageTextConstants.HighPoint;
                }

                device.ProbeTrace.Add($"HighPoint miniport: HighPoint device info id={info.DeviceID}, type={info.Type}, controller={info.ControllerID}, path={info.PathID}, target={info.TargetID}, vbus={info.VBusID}, osTarget={info.LogicalTargetID}, model='{info.Model}', serial='{info.Serial}'.");
            }

            private void ProbePassThrough(StorageDevice device, uint[] ids, int count)
            {
                for (int i = 0; i < count; ++i)
                {
                    uint deviceId = ids[i];
                    if (deviceId == 0)
                    {
                        continue;
                    }

                    bool nvme = false;

                    var identified = TryIdentifyViaIdePassThroughV2(device, deviceId)
                        || TryIdentifyViaIdePassThrough(device, deviceId);

                    var smart = TryReadSmartViaIdePassThroughV2(device, deviceId)
                        || TryReadSmartViaIdePassThrough(device, deviceId);

                    if (!HasUsefulDeviceIdentity(device) || !device.SupportsSmart)
                    {
                        nvme = TryPopulateViaNvmePassThrough(device, deviceId);
                    }

                    var inquiry = TryInquiryViaScsiPassThrough(device, deviceId);
                    inquiry |= TryInquirySerialViaScsiPassThrough(device, deviceId);
                    inquiry |= TryDeviceIdViaScsiPassThrough(device, deviceId);

                    var capacity = TryCapacityViaScsiPassThrough(device, deviceId);

                    if (identified || smart || inquiry || nvme || capacity)
                    {
                        device.ProbeTrace.Add($"HighPoint miniport: HighPoint passthrough probing succeeded for id {deviceId} (identify={identified}, smart={smart}, inquiry={inquiry}, nvme={nvme}, capacity={capacity}).");
                        return;
                    }
                }
            }

            private bool TryPopulateViaNvmePassThrough(StorageDevice device, uint deviceId)
            {
                bool success = false;
                if (TryNvmeIdentifyController(device, deviceId))
                {
                    success = true;
                }

                if (TryNvmeIdentifyNamespace(device, deviceId))
                {
                    success = true;
                }

                if (TryNvmeReadSmartLog(device, deviceId))
                {
                    success = true;
                }

                if (success)
                {
                    if (device.TransportKind == StorageTransportKind.Unknown)
                    {
                        device.TransportKind = StorageTransportKind.Nvme;
                    }

                    if (device.Controller.Family == StorageControllerFamily.Unknown)
                    {
                        device.Controller.Family = StorageControllerFamily.RocketRaid;
                    }

                    if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                    {
                        device.Controller.Kind = "HighPoint NVMe";
                    }
                }

                return success;
            }

            private bool TryNvmeIdentifyController(StorageDevice device, uint deviceId)
            {
                if (!TryNvmePassThrough(deviceId, 0U, 6U, BufferSizeConstants.Size4K, 1U, 0U, out var data))
                {
                    return false;
                }

                device.Nvme.IdentifyControllerData = data;
                NvmeProbeUtil.ApplyIdentifyControllerStrings(device, data);
                return data != null && data.Length >= BufferSizeConstants.Size4K;
            }

            private bool TryNvmeIdentifyNamespace(StorageDevice device, uint deviceId)
            {
                if (!TryNvmePassThrough(deviceId, 1U, 6U, BufferSizeConstants.Size4K, 0U, 0U, out var data))
                {
                    return false;
                }

                device.Nvme.IdentifyNamespaceData = data;
                NvmeNamespaceParser.ApplyNamespaceData(device, data);
                return data != null && data.Length >= BufferSizeConstants.Size4K;
            }

            private bool TryNvmeReadSmartLog(StorageDevice device, uint deviceId)
            {
                if (!TryNvmePassThrough(deviceId, 0xFFFFFFFFU, 2U, 512, 2U, 0U, out var data))
                {
                    return false;
                }

                device.Nvme.SmartLogData = data;
                NvmeSmartLogParser.ApplySmartLog(device, data);
                device.SupportsSmart = data != null && data.Length >= 512;
                return device.SupportsSmart;
            }

            private bool TryNvmePassThrough(uint deviceId, uint namespaceId, uint operationCode, int requestedLength, uint parameter0, uint parameter1, out byte[] data)
            {
                data = null;
                var request = new byte[76];

                WriteUInt32(request, 0, deviceId);
                request[4] = 1;
                request[8] = (byte)operationCode;
                WriteUInt32(request, 12, namespaceId);
                WriteUInt32(request, 44, (uint)requestedLength);
                WriteUInt32(request, 48, parameter0);
                WriteUInt32(request, 52, parameter1);
                WriteUInt32(request, 72, 100U);

                var response = new byte[4108];

                var requestHandle  = GCHandle.Alloc(request, GCHandleType.Pinned);
                var responseHandle = GCHandle.Alloc(response, GCHandleType.Pinned);

                try
                {
                    if (DirectNvmePassThrough(requestHandle.AddrOfPinnedObject(), (uint)request.Length, responseHandle.AddrOfPinnedObject(), (uint)response.Length) != 0)
                    {
                        return false;
                    }
                }
                finally
                {
                    responseHandle.Free();
                    requestHandle.Free();
                }

                int copyLength = Math.Min(requestedLength, Math.Max(0, response.Length - 12));
                if (copyLength <= 0)
                {
                    return false;
                }

                data = new byte[copyLength];
                Buffer.BlockCopy(response, 12, data, 0, copyLength);
                return true;
            }

            private bool TryIdentifyViaIdePassThrough(StorageDevice device, uint deviceId)
            {
                int headerSize = Marshal.SizeOf<HPT_IDE_PASS_THROUGH_HEADER>();
                var buffer = new byte[headerSize + 512];

                var header = new HPT_IDE_PASS_THROUGH_HEADER();
                header.DeviceID = deviceId;
                header.SectorCountReg = 1;
                header.LbaLowReg = 1;
                header.DriveHeadReg = 0xA0;
                header.CommandReg = 0xEC;
                header.SectorTransferCount = 1;
                header.Protocol = 1;
                header.Reserved = new byte[3];

                var headerBytes = StructureHelper.GetBytes(header);
                Buffer.BlockCopy(headerBytes, 0, buffer, 0, headerBytes.Length);

                var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                try
                {
                    if (DirectIdePassThrough(pin.AddrOfPinnedObject()) != 0)
                    {
                        return false;
                    }
                }
                finally
                {
                    pin.Free();
                }

                var identify = new byte[512];
                Buffer.BlockCopy(buffer, headerSize, identify, 0, identify.Length);

                StandardAtaProbe.ApplyAtaIdentify(device, identify);

                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }

                if (device.Controller.Family == StorageControllerFamily.Unknown)
                {
                    device.Controller.Family = StorageControllerFamily.RocketRaid;
                }

                return HasUsefulDeviceIdentity(device);
            }

            private bool TryIdentifyViaIdePassThroughV2(StorageDevice device, uint deviceId)
            {
                int headerSize = Marshal.SizeOf<HPT_IDE_PASS_THROUGH_HEADER_V2>();
                var buffer = new byte[headerSize + 512];

                var header = new HPT_IDE_PASS_THROUGH_HEADER_V2();
                header.DeviceID = deviceId;
                header.SectorCountReg = 1;
                header.LbaLowReg = 1;
                header.DriveHeadReg = 0xA0;
                header.CommandReg = 0xEC;
                header.SectorTransferCount = 1;
                header.Protocol = 1;

                var headerBytes = StructureHelper.GetBytes(header);
                Buffer.BlockCopy(headerBytes, 0, buffer, 0, headerBytes.Length);

                var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                try
                {
                    if (DirectIdePassThroughV2(pin.AddrOfPinnedObject()) != 0)
                    {
                        return false;
                    }
                }
                finally
                {
                    pin.Free();
                }

                var identify = new byte[512];
                Buffer.BlockCopy(buffer, headerSize, identify, 0, identify.Length);

                StandardAtaProbe.ApplyAtaIdentify(device, identify);

                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }

                if (device.Controller.Family == StorageControllerFamily.Unknown)
                {
                    device.Controller.Family = StorageControllerFamily.RocketRaid;
                }

                return HasUsefulDeviceIdentity(device);
            }

            private bool TryReadSmartViaIdePassThrough(StorageDevice device, uint deviceId)
            {
                byte[] smartData = null;
                byte[] smartThresholds = null;

                bool ok =
                    (TrySmartReadViaIdePassThrough(deviceId, false, 0xD0, out smartData)
                        && TrySmartReadViaIdePassThrough(deviceId, false, 0xD1, out smartThresholds))
                    || (TryEnableSmartViaIdePassThrough(deviceId, false)
                        && TrySmartReadViaIdePassThrough(deviceId, false, 0xD0, out smartData)
                        && TrySmartReadViaIdePassThrough(deviceId, false, 0xD1, out smartThresholds));

                if (!ok)
                {
                    return false;
                }

                var attributes = SmartProbe.ParseSmartPages(smartData, smartThresholds);
                if (attributes.Count == 0)
                {
                    return false;
                }

                device.SupportsSmart = true;
                device.SmartAttributes = attributes;

                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }

                if (device.Controller.Family == StorageControllerFamily.Unknown)
                {
                    device.Controller.Family = StorageControllerFamily.RocketRaid;
                }

                return true;
            }

            private bool TryReadSmartViaIdePassThroughV2(StorageDevice device, uint deviceId)
            {
                byte[] smartData = null;
                byte[] smartThresholds = null;
                bool ok =
                    (TrySmartReadViaIdePassThrough(deviceId, true, 0xD0, out smartData)
                        && TrySmartReadViaIdePassThrough(deviceId, true, 0xD1, out smartThresholds))
                    || (TryEnableSmartViaIdePassThrough(deviceId, true)
                        && TrySmartReadViaIdePassThrough(deviceId, true, 0xD0, out smartData)
                        && TrySmartReadViaIdePassThrough(deviceId, true, 0xD1, out smartThresholds));

                if (!ok)
                {
                    return false;
                }

                var attributes = SmartProbe.ParseSmartPages(smartData, smartThresholds);
                if (attributes.Count == 0)
                {
                    return false;
                }

                device.SupportsSmart = true;
                device.SmartAttributes = attributes;

                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }

                if (device.Controller.Family == StorageControllerFamily.Unknown)
                {
                    device.Controller.Family = StorageControllerFamily.RocketRaid;
                }

                return true;
            }

            private bool TrySmartReadViaIdePassThrough(uint deviceId, bool useV2, byte feature, out byte[] data)
            {
                data = null;

                if (useV2)
                {
                    int headerSize = Marshal.SizeOf<HPT_IDE_PASS_THROUGH_HEADER_V2>();
                    var buffer = new byte[headerSize + 512];

                    var header = new HPT_IDE_PASS_THROUGH_HEADER_V2();
                    header.DeviceID = deviceId;
                    header.FeaturesReg = feature;
                    header.SectorCountReg = 1;
                    header.LbaLowReg = 1;
                    header.LbaMidReg = 0x4F;
                    header.LbaHighReg = 0xC2;
                    header.DriveHeadReg = 0xA0;
                    header.CommandReg = 0xB0;
                    header.SectorTransferCount = 1;
                    header.Protocol = 1;

                    var headerBytes = StructureHelper.GetBytes(header);
                    Buffer.BlockCopy(headerBytes, 0, buffer, 0, headerBytes.Length);

                    var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                    try
                    {
                        if (DirectIdePassThroughV2(pin.AddrOfPinnedObject()) != 0)
                        {
                            return false;
                        }
                    }
                    finally
                    {
                        pin.Free();
                    }

                    data = new byte[512];
                    Buffer.BlockCopy(buffer, headerSize, data, 0, data.Length);

                    return true;
                }

                int legacyHeaderSize = Marshal.SizeOf<HPT_IDE_PASS_THROUGH_HEADER>();
                var legacyBuffer = new byte[legacyHeaderSize + 512];

                var legacyHeader = new HPT_IDE_PASS_THROUGH_HEADER();
                legacyHeader.DeviceID = deviceId;
                legacyHeader.FeaturesReg = feature;
                legacyHeader.SectorCountReg = 1;
                legacyHeader.LbaLowReg = 1;
                legacyHeader.LbaMidReg = 0x4F;
                legacyHeader.LbaHighReg = 0xC2;
                legacyHeader.DriveHeadReg = 0xA0;
                legacyHeader.CommandReg = 0xB0;
                legacyHeader.SectorTransferCount = 1;
                legacyHeader.Protocol = 1;
                legacyHeader.Reserved = new byte[3];

                var legacyHeaderBytes = StructureHelper.GetBytes(legacyHeader);
                Buffer.BlockCopy(legacyHeaderBytes, 0, legacyBuffer, 0, legacyHeaderBytes.Length);

                var legacyPin = GCHandle.Alloc(legacyBuffer, GCHandleType.Pinned);

                try
                {
                    if (DirectIdePassThrough(legacyPin.AddrOfPinnedObject()) != 0)
                    {
                        return false;
                    }
                }
                finally
                {
                    legacyPin.Free();
                }

                data = new byte[512];
                Buffer.BlockCopy(legacyBuffer, legacyHeaderSize, data, 0, data.Length);

                return true;
            }

            private bool TryEnableSmartViaIdePassThrough(uint deviceId, bool useV2)
            {
                if (useV2)
                {
                    var header = new HPT_IDE_PASS_THROUGH_HEADER_V2();
                    header.DeviceID = deviceId;
                    header.FeaturesReg = 0xD8;
                    header.SectorCountReg = 1;
                    header.LbaLowReg = 1;
                    header.LbaMidReg = 0x4F;
                    header.LbaHighReg = 0xC2;
                    header.DriveHeadReg = 0xA0;
                    header.CommandReg = 0xB0;
                    header.SectorTransferCount = 0;
                    header.Protocol = 0;

                    var buffer = StructureHelper.GetBytes(header);
                    var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);

                    try
                    {
                        return DirectIdePassThroughV2(pin.AddrOfPinnedObject()) == 0;
                    }
                    finally
                    {
                        pin.Free();
                    }
                }

                var legacyHeader = new HPT_IDE_PASS_THROUGH_HEADER();
                legacyHeader.DeviceID = deviceId;
                legacyHeader.FeaturesReg = 0xD8;
                legacyHeader.SectorCountReg = 1;
                legacyHeader.LbaLowReg = 1;
                legacyHeader.LbaMidReg = 0x4F;
                legacyHeader.LbaHighReg = 0xC2;
                legacyHeader.DriveHeadReg = 0xA0;
                legacyHeader.CommandReg = 0xB0;
                legacyHeader.SectorTransferCount = 0;
                legacyHeader.Protocol = 0;
                legacyHeader.Reserved = new byte[3];

                var legacyBuffer = StructureHelper.GetBytes(legacyHeader);
                var legacyPin = GCHandle.Alloc(legacyBuffer, GCHandleType.Pinned);

                try
                {
                    return DirectIdePassThrough(legacyPin.AddrOfPinnedObject()) == 0;
                }
                finally
                {
                    legacyPin.Free();
                }
            }

            private bool TryInquirySerialViaScsiPassThrough(StorageDevice device, uint deviceId)
            {
                var input = new HPT_SCSI_PASSTHROUGH_IN();
                input.DeviceID = deviceId;
                input.Protocol = 1;
                input.CdbLength = 6;
                input.Cdb = new byte[16];
                input.Cdb[0] = 0x12;
                input.Cdb[1] = 0x01;
                input.Cdb[2] = 0x80;
                input.Cdb[4] = 0x40;
                input.DataLength = 0x40;

                var inBytes = StructureHelper.GetBytes(input);
                var outBytes = new byte[Marshal.SizeOf<HPT_SCSI_PASSTHROUGH_OUT>() + 0x40];

                var inPin = GCHandle.Alloc(inBytes, GCHandleType.Pinned);
                var outPin = GCHandle.Alloc(outBytes, GCHandleType.Pinned);

                try
                {
                    if (DirectScsiPassThrough(inPin.AddrOfPinnedObject(), (uint)inBytes.Length, outPin.AddrOfPinnedObject(), (uint)outBytes.Length) != 0)
                    {
                        return false;
                    }
                }
                finally
                {
                    inPin.Free();
                    outPin.Free();
                }

                int offset = Marshal.SizeOf<HPT_SCSI_PASSTHROUGH_OUT>();
                if (offset + 4 > outBytes.Length)
                {
                    return false;
                }

                int pageLength = outBytes[offset + 3];
                int serialLength = Math.Min(pageLength, outBytes.Length - (offset + 4));

                if (serialLength <= 0)
                {
                    return false;
                }

                var serial = Encoding.ASCII.GetString(outBytes, offset + 4, serialLength).Trim('\0', ' ');
                if (string.IsNullOrWhiteSpace(serial))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(device.SerialNumber))
                {
                    device.SerialNumber = serial;
                }

                return true;
            }

            private bool TryDeviceIdViaScsiPassThrough(StorageDevice device, uint deviceId)
            {
                if (!TryScsiPassThroughDataIn(deviceId, 0x12, new byte[] { 0x01, 0x83, 0x00, 0x00, 0xFC }, 0xFC, out var page))
                {
                    return false;
                }

                ScsiInquiryProbe.ApplyDeviceIdentifier(device, page);
                return !string.IsNullOrWhiteSpace(device.Scsi.DeviceIdentifier);
            }

            private bool TryCapacityViaScsiPassThrough(StorageDevice device, uint deviceId)
            {
                if (TryScsiPassThroughDataIn(deviceId, 0x9E, new byte[] { 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00 }, 32, out var data))
                {
                    if (data != null && data.Length >= 12)
                    {
                        ulong lastLba = ((ulong)data[0] << 56) | ((ulong)data[1] << 48) | ((ulong)data[2] << 40) | ((ulong)data[3] << 32)
                            | ((ulong)data[4] << 24) | ((ulong)data[5] << 16) | ((ulong)data[6] << 8) | data[7];

                        uint blockLength = ((uint)data[8] << 24) | ((uint)data[9] << 16) | ((uint)data[10] << 8) | data[11];

                        if (blockLength != 0)
                        {
                            device.Scsi.LastLogicalBlockAddress = lastLba;
                            device.Scsi.LogicalBlockLength = blockLength;

                            if (!device.DiskSizeBytes.HasValue)
                            {
                                device.DiskSizeBytes = (lastLba + 1UL) * blockLength;
                            }

                            if (string.IsNullOrWhiteSpace(device.CapacitySource))
                            {
                                device.CapacitySource = "HighPoint SCSI Read Capacity";
                            }

                            return true;
                        }
                    }
                }

                if (TryScsiPassThroughDataIn(deviceId, 0x25, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 8, out data))
                {
                    if (data != null && data.Length >= 8)
                    {
                        ulong lastLba = ((ulong)data[0] << 24) | ((ulong)data[1] << 16) | ((ulong)data[2] << 8) | data[3];

                        uint blockLength = ((uint)data[4] << 24) | ((uint)data[5] << 16) | ((uint)data[6] << 8) | data[7];

                        if (blockLength != 0)
                        {
                            device.Scsi.LastLogicalBlockAddress = lastLba;
                            device.Scsi.LogicalBlockLength = blockLength;

                            if (!device.DiskSizeBytes.HasValue)
                            {
                                device.DiskSizeBytes = (lastLba + 1UL) * blockLength;
                            }

                            if (string.IsNullOrWhiteSpace(device.CapacitySource))
                            {
                                device.CapacitySource = "HighPoint SCSI Read Capacity";
                            }

                            return true;
                        }
                    }
                }

                return false;
            }

            private bool TryScsiPassThroughDataIn(uint deviceId, byte cdb0, byte[] cdbTail, int dataLength, out byte[] data)
            {
                data = null;
                var input = new HPT_SCSI_PASSTHROUGH_IN();
                input.DeviceID = deviceId;
                input.Protocol = 1;
                input.CdbLength = (byte)(1 + cdbTail.Length);
                input.Cdb = new byte[16];
                input.Cdb[0] = cdb0;

                Array.Copy(cdbTail, 0, input.Cdb, 1, Math.Min(cdbTail.Length, 15));

                input.DataLength = (uint)dataLength;

                var inBytes = StructureHelper.GetBytes(input);
                var outBytes = new byte[Marshal.SizeOf<HPT_SCSI_PASSTHROUGH_OUT>() + dataLength];

                var inPin = GCHandle.Alloc(inBytes, GCHandleType.Pinned);
                var outPin = GCHandle.Alloc(outBytes, GCHandleType.Pinned);

                try
                {
                    if (DirectScsiPassThrough(inPin.AddrOfPinnedObject(), (uint)inBytes.Length, outPin.AddrOfPinnedObject(), (uint)outBytes.Length) != 0)
                    {
                        return false;
                    }
                }
                finally
                {
                    inPin.Free();
                    outPin.Free();
                }

                int offset = Marshal.SizeOf<HPT_SCSI_PASSTHROUGH_OUT>();
                if (offset + dataLength > outBytes.Length)
                {
                    return false;
                }

                data = new byte[dataLength];
                Buffer.BlockCopy(outBytes, offset, data, 0, dataLength);
                return true;
            }

            private bool TryInquiryViaScsiPassThrough(StorageDevice device, uint deviceId)
            {
                var input = new HPT_SCSI_PASSTHROUGH_IN();
                input.DeviceID = deviceId;
                input.Protocol = 1;
                input.CdbLength = 6;
                input.Cdb = new byte[16];
                input.Cdb[0] = 0x12;
                input.Cdb[4] = 36;
                input.DataLength = 36;

                var inBytes = StructureHelper.GetBytes(input);
                var outBytes = new byte[Marshal.SizeOf<HPT_SCSI_PASSTHROUGH_OUT>() + 36];

                var inPin = GCHandle.Alloc(inBytes, GCHandleType.Pinned);
                var outPin = GCHandle.Alloc(outBytes, GCHandleType.Pinned);

                try
                {
                    if (DirectScsiPassThrough(inPin.AddrOfPinnedObject(), (uint)inBytes.Length, outPin.AddrOfPinnedObject(), (uint)outBytes.Length) != 0)
                    {
                        return false;
                    }
                }
                finally
                {
                    inPin.Free();
                    outPin.Free();
                }

                int offset = Marshal.SizeOf<HPT_SCSI_PASSTHROUGH_OUT>();
                if (offset + 36 > outBytes.Length)
                {
                    return false;
                }

                var inquiry = new byte[36];

                Buffer.BlockCopy(outBytes, offset, inquiry, 0, inquiry.Length);

                var vendor   = StringUtil.TrimStorageString(Encoding.ASCII.GetString(inquiry, 8, 8));
                var product  = StringUtil.TrimStorageString(Encoding.ASCII.GetString(inquiry, 16, 16));
                var revision = StringUtil.TrimStorageString(Encoding.ASCII.GetString(inquiry, 32, 4));

                if (!string.IsNullOrWhiteSpace(vendor) && string.IsNullOrWhiteSpace(device.VendorName))
                {
                    device.VendorName = vendor;
                }

                if (!string.IsNullOrWhiteSpace(product))
                {
                    device.ProductName = product;
                    if (string.IsNullOrWhiteSpace(device.DisplayName) || device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase))
                    {
                        device.DisplayName = product;
                    }
                }

                if (!string.IsNullOrWhiteSpace(revision) && string.IsNullOrWhiteSpace(device.ProductRevision))
                {
                    device.ProductRevision = revision;
                }

                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }

                if (device.Controller.Family == StorageControllerFamily.Unknown)
                {
                    device.Controller.Family = StorageControllerFamily.RocketRaid;
                }

                return HasUsefulDeviceIdentity(device);
            }

            private void CloseDirectPort()
            {
                if (_directPortHandle != null)
                {
                    _directPortHandle.Dispose();
                    _directPortHandle = null;
                }

                _directPortPath = string.Empty;
            }

            private bool EnsureDirectPort(StorageDevice device)
            {
                if (_directPortHandle != null)
                {
                    if (!_directPortHandle.IsInvalid && !_directPortHandle.IsClosed)
                    {
                        return true;
                    }

                    _directPortHandle.Dispose();
                    _directPortHandle = null;
                }

                foreach (var portNumber in EnumerateCandidateScsiPorts(device))
                {
                    string path = StoragePathBuilder.BuildScsiPortPath(portNumber);
                    SafeFileHandle handle = _ioControl.OpenDevice(
                        path,
                        IoAccess.GenericRead | IoAccess.GenericWrite,
                        IoShare.ReadWrite,
                        IoCreation.OpenExisting,
                        IoFlags.Normal);

                    if (handle == null || handle.IsInvalid)
                    {
                        if (handle != null)
                        {
                            handle.Dispose();
                        }

                        _lastDirectIoctlError = Marshal.GetLastWin32Error();
                        continue;
                    }

                    if (TryGetVersionFromHandle(handle, out uint version) && version != 0U)
                    {
                        _directPortHandle = handle;
                        _directPortPath   = path;

                        return true;
                    }

                    handle.Dispose();
                }

                return false;
            }

            private static IEnumerable<byte> EnumerateCandidateScsiPorts(StorageDevice device)
            {
                var seen = new bool[HptMaxScsiPorts];

                if (device != null && device.Scsi.PortNumber.HasValue)
                {
                    byte portNumber = device.Scsi.PortNumber.Value;
                    if (portNumber < HptMaxScsiPorts)
                    {
                        seen[portNumber] = true;
                        yield return portNumber;
                    }
                }

                for (int portNumber = 0; portNumber < HptMaxScsiPorts; ++portNumber)
                {
                    if (!seen[portNumber])
                    {
                        yield return (byte)portNumber;
                    }
                }
            }

            private uint DirectGetVersion()
            {
                if (!EnsureDirectPort(null) || !TryGetVersionFromHandle(_directPortHandle, out uint version))
                {
                    return 0U;
                }

                return version;
            }

            private int DirectGetControllerCount()
            {
                if (!EnsureDirectPort(null))
                {
                    return -1;
                }

                var output = new byte[4];
                int status = ExecuteHptMiniportCommand(_directPortHandle, HptIoctlGetControllerCount, null, output);

                if (status != 0 || output.Length < 4)
                {
                    return -1;
                }

                return BitConverter.ToInt32(output, 0);
            }

            private int DirectGetControllerInfo(int id, out HPT_CONTROLLER_INFO info)
            {
                return DirectGetStructure(id, HptIoctlGetControllerInfo, out info);
            }

            private int DirectGetControllerInfoV2(int id, out HPT_CONTROLLER_INFO_V2 info)
            {
                return DirectGetStructure(id, HptIoctlGetControllerInfoV2, out info);
            }

            private int DirectGetControllerInfoV3(int id, out HPT_CONTROLLER_INFO_V3 info)
            {
                return DirectGetStructure(id, HptIoctlGetControllerInfoV3, out info);
            }

            private int DirectGetDeviceInfo(uint id, out HPT_LOGICAL_DEVICE_INFO info)
            {
                return DirectGetStructure(id, HptIoctlGetDeviceInfo, out info);
            }

            private int DirectGetDeviceInfoV2(uint id, out HPT_LOGICAL_DEVICE_INFO_V2 info)
            {
                return DirectGetStructure(id, HptIoctlGetDeviceInfoV2, out info);
            }

            private int DirectGetDeviceInfoV3(uint id, out HPT_LOGICAL_DEVICE_INFO_V3 info)
            {
                return DirectGetStructure(id, HptIoctlGetDeviceInfoV3, out info);
            }

            private int DirectGetDeviceInfoV4(uint id, out HPT_LOGICAL_DEVICE_INFO_V4 info)
            {
                return DirectGetStructure(id, HptIoctlGetDeviceInfoV4, out info);
            }

            private int DirectGetPhysicalDevices(uint[] ids, int maxCount)
            {
                if (ids == null || maxCount <= 0 || !EnsureDirectPort(null))
                {
                    return -1;
                }

                int boundedMaxCount = Math.Min(maxCount, ids.Length);

                var input = BitConverter.GetBytes(boundedMaxCount);
                var output = new byte[4 + boundedMaxCount * 4];

                int status = ExecuteHptMiniportCommand(_directPortHandle, HptIoctlGetPhysicalDevices, input, output);
                if (status != 0 || output.Length < 4)
                {
                    return -1;
                }

                int count = BitConverter.ToInt32(output, 0);
                if (count < 0)
                {
                    return -1;
                }

                int copyCount = Math.Min(count, boundedMaxCount);
                for (int i = 0; i < copyCount; ++i)
                {
                    ids[i] = BitConverter.ToUInt32(output, 4 + i * 4);
                }

                return copyCount;
            }

            private int DirectIdePassThrough(IntPtr header)
            {
                return DirectIdePassThroughCore(header, false);
            }

            private int DirectIdePassThroughV2(IntPtr header)
            {
                return DirectIdePassThroughCore(header, true);
            }

            private int DirectScsiPassThrough(IntPtr inBuffer, uint inSize, IntPtr outBuffer, uint outSize)
            {
                if (inBuffer == IntPtr.Zero || outBuffer == IntPtr.Zero || inSize == 0 || outSize == 0 || inSize > int.MaxValue || outSize > int.MaxValue || !EnsureDirectPort(null))
                {
                    return -1;
                }

                var input = new byte[(int)inSize];
                var output = new byte[(int)outSize];

                Marshal.Copy(inBuffer, input, 0, input.Length);

                int status = ExecuteHptMiniportCommand(_directPortHandle, HptIoctlScsiPassThrough, input, output);
                if (status != 0)
                {
                    return status;
                }

                Marshal.Copy(output, 0, outBuffer, output.Length);
                return 0;
            }

            private int DirectNvmePassThrough(IntPtr inBuffer, uint inSize, IntPtr outBuffer, uint outSize)
            {
                return -1;
            }

            private int DirectIdePassThroughCore(IntPtr header, bool useV2)
            {
                if (header == IntPtr.Zero || !EnsureDirectPort(null))
                {
                    return -1;
                }

                int headerSize = useV2
                    ? Marshal.SizeOf<HPT_IDE_PASS_THROUGH_HEADER_V2>()
                    : Marshal.SizeOf<HPT_IDE_PASS_THROUGH_HEADER>();

                int sectorTransferCount = useV2
                    ? Marshal.PtrToStructure<HPT_IDE_PASS_THROUGH_HEADER_V2>(header).SectorTransferCount
                    : Marshal.PtrToStructure<HPT_IDE_PASS_THROUGH_HEADER>(header).SectorTransferCount;

                int transferLength = Math.Max(0, sectorTransferCount) * 512;
                int bufferLength = headerSize + transferLength;

                var buffer = new byte[bufferLength];
                Marshal.Copy(header, buffer, 0, buffer.Length);

                var output = new byte[buffer.Length];
                int status = ExecuteHptMiniportCommand(_directPortHandle, useV2 ? HptIoctlIdePassThroughV2 : HptIoctlIdePassThrough, buffer, output);

                if (status != 0)
                {
                    return status;
                }

                Marshal.Copy(output, 0, header, output.Length);
                return 0;
            }

            private int DirectGetStructure<T>(int inputValue, uint command, out T value)
                where T : struct
            {
                return DirectGetStructure(BitConverter.GetBytes(inputValue), command, out value);
            }

            private int DirectGetStructure<T>(uint inputValue, uint command, out T value)
                where T : struct
            {
                return DirectGetStructure(BitConverter.GetBytes(inputValue), command, out value);
            }

            private int DirectGetStructure<T>(byte[] input, uint command, out T value)
                where T : struct
            {
                value = default;

                if (!EnsureDirectPort(null))
                {
                    return -1;
                }

                var output = new byte[Marshal.SizeOf<T>()];
                int status = ExecuteHptMiniportCommand(_directPortHandle, command, input, output);

                if (status != 0)
                {
                    return status;
                }

                value = StructureHelper.FromBytes<T>(output);
                return 0;
            }

            private bool TryGetVersionFromHandle(SafeFileHandle handle, out uint version)
            {
                version = 0U;

                if (handle == null || handle.IsInvalid)
                {
                    return false;
                }

                var output = new byte[4];
                int status = ExecuteHptMiniportCommand(handle, HptIoctlGetVersion, null, output);

                if (status != 0 || output.Length < 4)
                {
                    return false;
                }

                version = BitConverter.ToUInt32(output, 0);
                return true;
            }

            private int ExecuteHptMiniportCommand(SafeFileHandle handle, uint command, byte[] input, byte[] output)
            {
                int status = ExecuteWrappedHptMiniportCommand(handle, command, input, output);
                return status;
            }

            private int ExecuteWrappedHptMiniportCommand(SafeFileHandle handle, uint command, byte[] input, byte[] output)
            {
                input  = input  ?? [];
                output = output ?? [];

                int inputLength  = input.Length;
                int outputLength = output.Length;

                var request = new byte[HptWrappedHeaderSize + inputLength + outputLength];

                WriteUInt32(request, 0, HptSrbHeaderLength);

                var signature = Encoding.ASCII.GetBytes(HptSrbSignature);
                Buffer.BlockCopy(signature, 0, request, 4, signature.Length);

                WriteUInt32(request, 12, HptMiniportTimeoutSeconds);
                WriteUInt32(request, 16, command);
                WriteUInt32(request, 20, 0U);
                WriteUInt32(request, 24, (uint)(outputLength + 8));
                WriteUInt32(request, 28, (uint)inputLength);
                WriteUInt32(request, 32, (uint)outputLength);

                if (inputLength > 0)
                {
                    Buffer.BlockCopy(input, 0, request, HptWrappedHeaderSize, inputLength);
                }

                var response = (byte[])request.Clone();

                if (!Kernel32Native.DeviceIoControl(handle, IoControlScsiMiniport, request, request.Length, response, response.Length, out _, IntPtr.Zero))
                {
                    _lastDirectIoctlError = Marshal.GetLastWin32Error();
                    return -1;
                }

                if (response.Length < HptWrappedHeaderSize)
                {
                    return -1;
                }

                int returnCode = BitConverter.ToInt32(response, 20);
                if (returnCode != 0)
                {
                    return returnCode;
                }

                if (outputLength > 0)
                {
                    int reportedLength = BitConverter.ToInt32(response, 24) - 8;
                    if (reportedLength < 0 || reportedLength > outputLength)
                    {
                        reportedLength = outputLength;
                    }

                    int copyLength = Math.Min(reportedLength, Math.Min(outputLength, response.Length - HptWrappedHeaderSize - inputLength));
                    if (copyLength > 0)
                    {
                        Buffer.BlockCopy(response, HptWrappedHeaderSize + inputLength, output, 0, copyLength);
                    }
                }

                return 0;
            }


            #endregion
        }

        #endregion
    }
}
