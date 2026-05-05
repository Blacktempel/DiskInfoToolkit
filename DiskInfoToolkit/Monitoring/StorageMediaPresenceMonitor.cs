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
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Monitoring
{
    public static class StorageMediaPresenceMonitor
    {
        #region Public

        public static List<StorageDevice> ExtractMediaWatchDevices(List<StorageDevice> devices)
        {
            var result = new List<StorageDevice>();
            if (devices == null)
            {
                return result;
            }

            foreach (var device in devices)
            {
                if (!IsMediaWatchCandidate(device))
                {
                    continue;
                }

                result.Add(StorageDeviceCloneHelper.Clone(device));
            }

            return result;
        }

        public static Dictionary<string, bool?> BuildStateSnapshot(List<StorageDevice> devices)
        {
            var result = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

            if (devices == null)
            {
                return result;
            }

            foreach (var device in devices)
            {
                if (!IsMediaWatchCandidate(device))
                {
                    continue;
                }

                var key = StorageDeviceIdentityMatcher.GetStableKey(device);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                result[key] = GetMediaPresentState(device);
            }

            return result;
        }

        public static void FilterNoMediaDevices(List<StorageDevice> devices, Dictionary<string, bool?> mediaStates)
        {
            if (devices == null)
            {
                return;
            }

            for (int i = devices.Count - 1; i >= 0; --i)
            {
                var device = devices[i];

                bool? mediaPresent;
                string key = StorageDeviceIdentityMatcher.GetStableKey(device);

                if (mediaStates != null
                 && !string.IsNullOrWhiteSpace(key)
                 && mediaStates.TryGetValue(key, out var cachedState))
                {
                    mediaPresent = cachedState;
                }
                else
                {
                    mediaPresent = GetMediaPresentState(device);
                }

                if (mediaPresent.HasValue && !mediaPresent.Value)
                {
                    devices.RemoveAt(i);
                }
            }
        }

        public static bool IsMediaWatchCandidate(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            if (device.TransportKind == StorageTransportKind.Sd || device.TransportKind == StorageTransportKind.Mmc)
            {
                return true;
            }

            if (device.BusType == StorageBusType.Sd || device.BusType == StorageBusType.Mmc)
            {
                return true;
            }

            if (device.Controller.Family == StorageControllerFamily.RealtekSd)
            {
                return true;
            }

            if (IsUsbMassStorageDevice(device))
            {
                return IsUsbMassStorageCardReaderCandidate(device);
            }

            if (device.IsRemovable)
            {
                return true;
            }

            var service = StringUtil.TrimStorageString(device.Controller.Service);
            if (service.Equals(ControllerServiceNames.RtsUer, StringComparison.OrdinalIgnoreCase)
                || service.Equals(ControllerServiceNames.SdStor, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsUsbMassStorageCardReaderCandidate(device);
        }

        public static bool? GetMediaPresentState(StorageDevice device)
        {
            if (!IsMediaWatchCandidate(device))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(device.DevicePath))
            {
                return false;
            }

            IStorageIoControl ioControl = StorageIoControlFactory.Create();
            SafeFileHandle handle = ioControl.OpenDevice(
                device.DevicePath,
                IoAccess.ReadAttributes,
                IoShare.All,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            if (handle == null || handle.IsInvalid)
            {
                return false;
            }

            using (handle)
            {
                var outBuffer = new byte[sizeof(uint)];

                if (ioControl.SendRawIoControl(handle, IoControlCodes.IOCTL_STORAGE_CHECK_VERIFY2, null, outBuffer, out var bytesReturned))
                {
                    return true;
                }

                if (ioControl.SendRawIoControl(handle, IoControlCodes.IOCTL_STORAGE_CHECK_VERIFY, null, outBuffer, out bytesReturned))
                {
                    return true;
                }

                if (ioControl.TryGetDriveGeometryEx(handle, out var geometryInfo) && geometryInfo.DiskSize > 0)
                {
                    return true;
                }

                if (ioControl.TryGetDriveLayout(handle, out var rawLayout)
                    && rawLayout != null
                    && rawLayout.Length >= Marshal.SizeOf<DRIVE_LAYOUT_INFORMATION_EX_RAW>())
                {
                    var layoutHeader = StructureHelper.FromBytes<DRIVE_LAYOUT_INFORMATION_EX_RAW>(rawLayout);

                    int partitionOffset = (int)Marshal.OffsetOf<DRIVE_LAYOUT_INFORMATION_EX_RAW>(nameof(DRIVE_LAYOUT_INFORMATION_EX_RAW.PartitionInformation));
                    int partitionSize = Marshal.SizeOf<PARTITION_INFORMATION_EX_RAW>();

                    for (int i = 0; i < layoutHeader.PartitionCount; ++i)
                    {
                        int offset = partitionOffset + (i * partitionSize);
                        if (offset < 0 || offset + partitionSize > rawLayout.Length)
                        {
                            break;
                        }

                        var partitionBytes = new byte[partitionSize];
                        Buffer.BlockCopy(rawLayout, offset, partitionBytes, 0, partitionSize);

                        var partition = StructureHelper.FromBytes<PARTITION_INFORMATION_EX_RAW>(partitionBytes);
                        if (partition.PartitionLength > 0)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        #endregion

        #region Private

        private static bool IsUsbMassStorageDevice(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            if (device.TransportKind != StorageTransportKind.Usb && device.BusType != StorageBusType.Usb)
            {
                return false;
            }

            return device.Usb != null && device.Usb.IsMassStorageLike;
        }

        private static bool IsUsbMassStorageCardReaderCandidate(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            if (device.TransportKind != StorageTransportKind.Usb && device.BusType != StorageBusType.Usb)
            {
                return false;
            }

            if (device.Usb == null || !device.Usb.IsMassStorageLike)
            {
                return false;
            }

            var service = StringUtil.TrimStorageString(device.Controller.Service);
            if (!service.Equals(ControllerServiceNames.UsbStor, StringComparison.OrdinalIgnoreCase)
                && !service.Equals(ControllerServiceNames.UsbStorWithTrailingSpace, StringComparison.OrdinalIgnoreCase)
                && !service.Equals(ControllerServiceNames.UaspStor, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            //Important: do not filter on properties that change depending on whether media is inserted.
            //The same reader must remain a media-watch candidate both when empty and when a card is present.

            //Some card readers expose themselves as a disk device with no media, no partitions and no volume.
            //This is a strong indicator of a card reader, even if the driver doesn't follow typical patterns.
            if (HasEmptyPseudoVolumeSignature(device))
            {
                return true;
            }

            //Final fallback to look for "card reader" indicators in the various string properties.
            //This is not ideal but may catch some card readers that don't follow usual conventions.
            return HasCardReaderIndicator(device);
        }

        private static bool HasCardReaderIndicator(StorageDevice device)
        {
            string combined = string.Join(string.Empty,
                device.DisplayName ?? string.Empty,
                device.DeviceDescription ?? string.Empty,
                device.ProductName ?? string.Empty,
                device.DevicePath ?? string.Empty,
                device.AlternateDevicePath ?? string.Empty,
                device.DeviceInstanceID ?? string.Empty,
                device.ParentInstanceID ?? string.Empty,
                device.Controller.HardwareID ?? string.Empty);

            return combined.IndexOf("card reader", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("sd card", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("sd_card", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("mmc", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasEmptyPseudoVolumeSignature(StorageDevice device)
        {
            if (device.Partitions == null || device.Partitions.Count == 0)
            {
                return false;
            }

            bool hasVolumeMarker = false;
            foreach (var partition in device.Partitions)
            {
                if (!(partition.DriveLetter == null || char.IsWhiteSpace(partition.DriveLetter.Value))
                 || !string.IsNullOrWhiteSpace(partition.VolumePath))
                {
                    hasVolumeMarker = true;
                }

                if (partition.PartitionLength > 0)
                {
                    return false;
                }

                if (partition.PartitionNumber != 0)
                {
                    return false;
                }

                if (partition.MbrPartitionType != 0)
                {
                    return false;
                }

                if (partition.GptPartitionType.HasValue || partition.GptPartitionID.HasValue)
                {
                    return false;
                }
            }

            return hasVolumeMarker;
        }

        #endregion
    }
}
