/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Probes;
using DiskInfoToolkit.Utilities;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.Vendors
{
    public sealed class HighPointBackend : IOptionalVendorBackend
    {
        #region Constructor

        public HighPointBackend(ExternalVendorLibraryManager libraries)
        {
            _libraries = libraries;
            _capabilities = new HighPointBackendCapabilities();
        }

        #endregion

        #region Fields

        public const string HptGetVersion = "hpt_get_version";

        private readonly ExternalVendorLibraryManager _libraries;

        private bool _exportsResolved;

        private HighPointBackendCapabilities _capabilities;

        private HptGetVersionDelegate _getVersion;

        private HptGetControllerCountDelegate _getControllerCount;

        private HptGetControllerInfoDelegate _getControllerInfo;

        private HptGetControllerInfoV2Delegate _getControllerInfoV2;

        private HptGetControllerInfoV3Delegate _getControllerInfoV3;

        private HptGetPhysicalDevicesDelegate _getPhysicalDevices;

        private HptGetDeviceInfoDelegate _getDeviceInfo;

        private HptGetDeviceInfoV2Delegate _getDeviceInfoV2;

        private HptGetDeviceInfoV3Delegate _getDeviceInfoV3;

        private HptGetDeviceInfoV4Delegate _getDeviceInfoV4;

        private HptIdePassThroughDelegate _idePassThrough;

        private HptIdePassThroughV2Delegate _idePassThroughV2;

        private HptScsiPassThroughDelegate _scsiPassThrough;

        private HptNvmePassThroughDelegate _nvmePassThrough;

        #endregion

        #region Properties

        public bool IsAvailable
        {
            get
            {
                SafeLibraryHandle handle = _libraries.GetHighPointLibrary();
                return handle != null && !handle.IsInvalid;
            }
        }

        public HighPointBackendCapabilities Capabilities
        {
            get
            {
                EnsureExportsResolved();
                return _capabilities;
            }
        }

        #endregion

        #region Public

        public bool TryProbe(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            var handle = _libraries.GetHighPointLibrary();
            if (handle == null || handle.IsInvalid)
            {
                device.ProbeTrace.Add("Vendor backend: HighPoint library is not available.");
                return false;
            }

            EnsureExportsResolved();
            if (!_capabilities.HasCoreExports || _getControllerCount == null)
            {
                device.ProbeTrace.Add("Vendor backend: HighPoint core exports are incomplete.");
                return false;
            }

            uint version = _getVersion != null ? _getVersion() : 0U;
            int controllerCount = SafeCall(_getControllerCount);
            device.ProbeTrace.Add($"Vendor backend: HighPoint version=0x{version:X8}, controllers={controllerCount}.");

            if (version == 0U || controllerCount < 0)
            {
                device.ProbeTrace.Add("Vendor backend: HighPoint library exports were loaded, but the runtime interface is not usable for this controller/driver instance.");
            }

            bool success = false;
            if (controllerCount > 0)
            {
                success |= TryPopulateControllerInfo(device, 0);
            }

            if (_getPhysicalDevices != null)
            {
                var ids = new uint[128];
                int deviceCount = SafeCallPhysicalDevices(ids, ids.Length);

                if (deviceCount < 0)
                {
                    device.ProbeTrace.Add($"Vendor backend: HighPoint physical device query failed with {deviceCount}.");
                }

                if (deviceCount > 0)
                {
                    device.ProbeTrace.Add($"Vendor backend: HighPoint physical devices reported={deviceCount}.");
                    if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                    {
                        device.Controller.Kind = StorageTextConstants.HighPoint;
                    }

                    success = true;

                    for (int i = 0; i < deviceCount && i < ids.Length; ++i)
                    {
                        if (TryPopulateDeviceInfo(device, ids[i]))
                        {
                            success = true;
                            break;
                        }
                    }

                    if (!HasUsefulDeviceIdentity(device) || !device.SupportsSmart)
                    {
                        ProbePassThrough(device, ids, deviceCount);
                        if (HasUsefulDeviceIdentity(device) || device.SupportsSmart)
                        {
                            success = true;
                        }
                    }
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

                device.ProbeTrace.Add("Vendor backend: HighPoint probe produced controller data.");
                return true;
            }

            device.ProbeTrace.Add("Vendor backend: HighPoint exports resolved but no device/controller data was obtained.");
            return false;
        }

        #endregion

        #region Private

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

        private static int SafeCall(HptGetControllerCountDelegate fn)
        {
            try
            {
                return fn();
            }
            catch
            {
                return -1;
            }
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

        private static T ResolveDelegate<T>(IntPtr module, string name, out bool found)
            where T : class
        {
            var export = Kernel32Native.GetProcAddress(module, name);

            found = export != IntPtr.Zero;
            if (!found)
            {
                return null;
            }

            return Marshal.GetDelegateForFunctionPointer<T>(export);
        }

        private static bool HasExport(IntPtr module, string name)
        {
            return Kernel32Native.GetProcAddress(module, name) != IntPtr.Zero;
        }

        private bool TryPopulateControllerInfo(StorageDevice device, int controllerId)
        {
            if (_getControllerInfoV3 != null)
            {
                if (_getControllerInfoV3(controllerId, out var info) == 0)
                {
                    ApplyControllerInfo(device, info.ProductID, info.VendorID, info.NumBuses);
                    return true;
                }
            }

            if (_getControllerInfoV2 != null)
            {
                if (_getControllerInfoV2(controllerId, out var info) == 0)
                {
                    ApplyControllerInfo(device, info.ProductID, info.VendorID, 0);
                    return true;
                }
            }

            if (_getControllerInfo != null)
            {
                if (_getControllerInfo(controllerId, out var info) == 0)
                {
                    ApplyControllerInfo(device, info.ProductID, info.VendorID, info.NumBuses);
                    return true;
                }
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
                device.ProbeTrace.Add($"Vendor backend: HighPoint controller vendor='{vendor}', product='{product}', buses={busCount}.");
            }
        }

        private bool TryPopulateDeviceInfo(StorageDevice device, uint deviceId)
        {
            if (_getDeviceInfoV4 != null)
            {
                if (_getDeviceInfoV4(deviceId, out var info) == 0)
                {
                    ApplyLogicalDeviceInfo(device, deviceId, info.Type, info.Capacity, info.Device);
                    return true;
                }
            }

            if (_getDeviceInfoV3 != null)
            {
                if (_getDeviceInfoV3(deviceId, out var info) == 0)
                {
                    ApplyLogicalDeviceInfo(device, deviceId, info.Type, info.Capacity, info.Device);
                    return true;
                }
            }

            if (_getDeviceInfoV2 != null)
            {
                if (_getDeviceInfoV2(deviceId, out var info) == 0)
                {
                    ApplyLogicalDeviceInfo(device, deviceId, info.Type, info.Capacity, info.Device);
                    return true;
                }
            }

            if (_getDeviceInfo != null)
            {
                if (_getDeviceInfo(deviceId, out var info) == 0)
                {
                    ApplyLogicalDeviceInfo(device, deviceId, info.Type, info.Capacity, info.Device);
                    return true;
                }
            }

            return false;
        }

        private void ApplyLogicalDeviceInfo(StorageDevice device, uint deviceId, byte type, ulong capacity, HPT_DEVICE_INFO info)
        {
            var model    = DecodeSwappedWords(info.IdentifyData.ModelNumber);
            var serial   = DecodeSwappedWords(info.IdentifyData.SerialNumber);
            var firmware = DecodeSwappedWords(info.IdentifyData.FirmwareRevision);

            if (!string.IsNullOrWhiteSpace(model))
            {
                device.ProductName = model;
                if (string.IsNullOrWhiteSpace(device.DisplayName) || device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase))
                {
                    device.DisplayName = model;
                }
            }

            if (!string.IsNullOrWhiteSpace(serial) && string.IsNullOrWhiteSpace(device.SerialNumber))
            {
                device.SerialNumber = serial;
            }

            if (!string.IsNullOrWhiteSpace(firmware) && string.IsNullOrWhiteSpace(device.ProductRevision))
            {
                device.ProductRevision = firmware;
            }

            if (capacity > 0 && device.DiskSizeBytes.GetValueOrDefault() == 0)
            {
                device.DiskSizeBytes = capacity;
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

            device.ProbeTrace.Add($"Vendor backend: HighPoint device info id={deviceId}, type={type}, path={info.PathID}, target={info.TargetID}, model='{model}', serial='{serial}'.");
        }

        private void ApplyLogicalDeviceInfo(StorageDevice device, uint deviceId, byte type, ulong capacity, HPT_DEVICE_INFO_V2 info)
        {
            var model    = DecodeSwappedWords(info.IdentifyData.ModelNumber);
            var serial   = DecodeSwappedWords(info.IdentifyData.SerialNumber);
            var firmware = DecodeSwappedWords(info.IdentifyData.FirmwareRevision);

            if (!string.IsNullOrWhiteSpace(model))
            {
                device.ProductName = model;
                if (string.IsNullOrWhiteSpace(device.DisplayName) || device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase))
                {
                    device.DisplayName = model;
                }
            }

            if (!string.IsNullOrWhiteSpace(serial) && string.IsNullOrWhiteSpace(device.SerialNumber))
            {
                device.SerialNumber = serial;
            }

            if (!string.IsNullOrWhiteSpace(firmware) && string.IsNullOrWhiteSpace(device.ProductRevision))
            {
                device.ProductRevision = firmware;
            }

            if (capacity > 0 && device.DiskSizeBytes.GetValueOrDefault() == 0)
            {
                device.DiskSizeBytes = capacity;
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

            device.ProbeTrace.Add($"Vendor backend: HighPoint device info id={deviceId}, type={type}, path={info.PathID}, target={info.TargetID}, model='{model}', serial='{serial}'.");
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
                    device.ProbeTrace.Add($"Vendor backend: HighPoint passthrough probing succeeded for id {deviceId} (identify={identified}, smart={smart}, inquiry={inquiry}, nvme={nvme}, capacity={capacity}).");
                    return;
                }
            }
        }

        private bool TryPopulateViaNvmePassThrough(StorageDevice device, uint deviceId)
        {
            if (_nvmePassThrough == null)
            {
                return false;
            }

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
            if (_nvmePassThrough == null)
            {
                return false;
            }

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
                if (_nvmePassThrough(requestHandle.AddrOfPinnedObject(), (uint)request.Length, responseHandle.AddrOfPinnedObject(), (uint)response.Length) != 0)
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
            if (_idePassThrough == null)
            {
                return false;
            }

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

            byte[] headerBytes = StructureHelper.GetBytes(header);
            Buffer.BlockCopy(headerBytes, 0, buffer, 0, headerBytes.Length);

            var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);

            try
            {
                if (_idePassThrough(pin.AddrOfPinnedObject()) != 0)
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
            if (_idePassThroughV2 == null)
            {
                return false;
            }

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
                if (_idePassThroughV2(pin.AddrOfPinnedObject()) != 0)
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
            if (_idePassThrough == null)
            {
                return false;
            }

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
            if (_idePassThroughV2 == null)
            {
                return false;
            }

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
                if (_idePassThroughV2 == null)
                {
                    return false;
                }

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
                    if (_idePassThroughV2(pin.AddrOfPinnedObject()) != 0)
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

            if (_idePassThrough == null)
            {
                return false;
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
                if (_idePassThrough(legacyPin.AddrOfPinnedObject()) != 0)
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
                if (_idePassThroughV2 == null)
                {
                    return false;
                }

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
                    return _idePassThroughV2(pin.AddrOfPinnedObject()) == 0;
                }
                finally
                {
                    pin.Free();
                }
            }

            if (_idePassThrough == null)
            {
                return false;
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
                return _idePassThrough(legacyPin.AddrOfPinnedObject()) == 0;
            }
            finally
            {
                legacyPin.Free();
            }
        }

        private bool TryInquirySerialViaScsiPassThrough(StorageDevice device, uint deviceId)
        {
            if (_scsiPassThrough == null)
            {
                return false;
            }

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
                if (_scsiPassThrough(inPin.AddrOfPinnedObject(), (uint)inBytes.Length, outPin.AddrOfPinnedObject(), (uint)outBytes.Length) != 0)
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
            if (_scsiPassThrough == null)
            {
                return false;
            }

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
                if (_scsiPassThrough(inPin.AddrOfPinnedObject(), (uint)inBytes.Length, outPin.AddrOfPinnedObject(), (uint)outBytes.Length) != 0)
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
            if (_scsiPassThrough == null)
            {
                return false;
            }

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
                if (_scsiPassThrough(inPin.AddrOfPinnedObject(), (uint)inBytes.Length, outPin.AddrOfPinnedObject(), (uint)outBytes.Length) != 0)
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

        private void EnsureExportsResolved()
        {
            if (_exportsResolved)
            {
                return;
            }

            _exportsResolved = true;
            var handle = _libraries.GetHighPointLibrary();
            if (handle == null || handle.IsInvalid)
            {
                return;
            }

            var module = handle.DangerousGetHandle();

            bool found;
            _getVersion                   = ResolveDelegate<HptGetVersionDelegate         >(module, HptGetVersion               , out found); _capabilities.HasVersion          = found;
            _getControllerCount           = ResolveDelegate<HptGetControllerCountDelegate >(module, "hpt_get_controller_count"  , out found); _capabilities.HasControllerCount  = found;
            _getControllerInfo            = ResolveDelegate<HptGetControllerInfoDelegate  >(module, "hpt_get_controller_info"   , out found); _capabilities.HasControllerInfo   = found;
            _getControllerInfoV2          = ResolveDelegate<HptGetControllerInfoV2Delegate>(module, "hpt_get_controller_info_v2", out found); _capabilities.HasControllerInfoV2 = found;
            _getControllerInfoV3          = ResolveDelegate<HptGetControllerInfoV3Delegate>(module, "hpt_get_controller_info_v3", out found); _capabilities.HasControllerInfoV3 = found;
            _getPhysicalDevices           = ResolveDelegate<HptGetPhysicalDevicesDelegate >(module, "hpt_get_physical_devices"  , out found); _capabilities.HasPhysicalDevices  = found;
            _getDeviceInfo                = ResolveDelegate<HptGetDeviceInfoDelegate      >(module, "hpt_get_device_info"       , out found); _capabilities.HasDeviceInfo       = found;
            _getDeviceInfoV2              = ResolveDelegate<HptGetDeviceInfoV2Delegate    >(module, "hpt_get_device_info_v2"    , out found); _capabilities.HasDeviceInfoV2     = found;
            _getDeviceInfoV3              = ResolveDelegate<HptGetDeviceInfoV3Delegate    >(module, "hpt_get_device_info_v3"    , out found); _capabilities.HasDeviceInfoV3     = found;
            _getDeviceInfoV4              = ResolveDelegate<HptGetDeviceInfoV4Delegate    >(module, "hpt_get_device_info_v4"    , out found); _capabilities.HasDeviceInfoV4     = found;
            _capabilities.HasDeviceInfoV5 = HasExport(module, "hpt_get_device_info_v5");
            _idePassThrough               = ResolveDelegate<HptIdePassThroughDelegate     >(module, "hpt_ide_pass_through"      , out found); _capabilities.HasIdePassThrough   = found;
            _idePassThroughV2             = ResolveDelegate<HptIdePassThroughV2Delegate   >(module, "hpt_ide_pass_through_v2"   , out found); _capabilities.HasIdePassThroughV2 = found;
            _scsiPassThrough              = ResolveDelegate<HptScsiPassThroughDelegate    >(module, "hpt_scsi_passthrough"      , out found); _capabilities.HasScsiPassThrough  = found;
            _nvmePassThrough              = ResolveDelegate<HptNvmePassThroughDelegate    >(module, "hpt_nvme_passthrough"      , out found); _capabilities.HasNvmePassThrough  = found;
        }

        private int SafeCallPhysicalDevices(uint[] ids, int maxCount)
        {
            try
            {
                return _getPhysicalDevices(ids, maxCount);
            }
            catch
            {
                return -1;
            }
        }

        #endregion
    }
}
