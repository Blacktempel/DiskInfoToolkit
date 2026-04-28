/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Monitoring;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Globalization;

namespace DiskInfoToolkit.Core
{
    internal class MicrosoftStorageSpacesEnumerator
    {
        #region Fields

        private const int PhysicalDriveFallbackScanLimit = 256;

        #endregion

        #region Public

        public static void Enumerate(List<StorageDevice> alreadyDetectedStorageDevices, IStorageIoControl ioControl, out List<StorageDevice> newlyDetectedStorageDevices)
        {
            newlyDetectedStorageDevices = new List<StorageDevice>();

            if (alreadyDetectedStorageDevices == null || !ContainsStorageSpacesAggregate(alreadyDetectedStorageDevices))
            {
                return;
            }

            var knownStorageDeviceNumbers = CreateKnownStorageDeviceNumberSet(alreadyDetectedStorageDevices);
            var candidatePhysicalDriveNumbers = GetStorageSpacesPhysicalDriveNumbers();

            foreach (var physicalDriveNumber in candidatePhysicalDriveNumbers)
            {
                if (knownStorageDeviceNumbers.Contains(physicalDriveNumber))
                {
                    continue;
                }

                var fallbackDevice = TryCreatePhysicalDriveFallbackDevice(physicalDriveNumber, ioControl);

                if (fallbackDevice == null)
                {
                    continue;
                }

                if (fallbackDevice.StorageDeviceNumber.HasValue && knownStorageDeviceNumbers.Contains(fallbackDevice.StorageDeviceNumber.Value))
                {
                    continue;
                }

                if (!IsUsablePhysicalDriveFallbackDevice(fallbackDevice))
                {
                    continue;
                }

                if (StorageDeviceIdentityMatcher.FindBestMatch(alreadyDetectedStorageDevices, fallbackDevice) != null)
                {
                    continue;
                }

                ProbeTraceRecorder.Add(fallbackDevice, "Device added through Storage Spaces physical disk fallback.");

                newlyDetectedStorageDevices.Add(fallbackDevice);

                if (fallbackDevice.StorageDeviceNumber.HasValue)
                {
                    knownStorageDeviceNumbers.Add(fallbackDevice.StorageDeviceNumber.Value);
                }
                else
                {
                    knownStorageDeviceNumbers.Add(physicalDriveNumber);
                }
            }
        }

        public static void RemoveStorageSpacesAggregates(List<StorageDevice> result)
        {
            var devicesToRemove = new HashSet<StorageDevice>();

            foreach (var device in result)
            {
                if (device.BusType != StorageBusType.Spaces)
                {
                    continue;
                }

                if (!LooksLikeVirtualOrAggregateDisk(device))
                {
                    continue;
                }

                ProbeTraceRecorder.Add(device, "Logical Storage Spaces device removed from result list because direct PhysicalDrive member disks were discovered.");

                devicesToRemove.Add(device);
            }

            result.RemoveAll(devicesToRemove.Contains);
        }

        #endregion

        #region Private

        private static List<uint> GetStorageSpacesPhysicalDriveNumbers()
        {
            var physicalDriveNumbers = new List<uint>();

            foreach (var physicalDriveNumber in EnumerateExistingDosPhysicalDriveNumbers())
            {
                AddPhysicalDriveNumber(physicalDriveNumbers, physicalDriveNumber);
            }

            physicalDriveNumbers.Sort();
            return physicalDriveNumbers;
        }

        private static void AddPhysicalDriveNumber(List<uint> physicalDriveNumbers, uint physicalDriveNumber)
        {
            if (physicalDriveNumbers == null || physicalDriveNumbers.Contains(physicalDriveNumber))
            {
                return;
            }

            physicalDriveNumbers.Add(physicalDriveNumber);
        }

        private static IEnumerable<uint> EnumerateExistingDosPhysicalDriveNumbers()
        {
            var buffer = new char[256 * PhysicalDriveFallbackScanLimit];
            var length = Kernel32Native.QueryDosDevice(null, buffer, buffer.Length);

            if (length == 0)
            {
                yield break;
            }

            int start = 0;

            for (int i = 0; i < length; ++i)
            {
                if (buffer[i] != '\0')
                {
                    continue;
                }

                int nameLength = i - start;

                if (nameLength > 0 && TryParsePhysicalDriveNumber(new string(buffer, start, nameLength), out uint physicalDriveNumber))
                {
                    yield return physicalDriveNumber;
                }

                start = i + 1;
            }
        }

        private static bool TryParsePhysicalDriveNumber(string value, out uint physicalDriveNumber)
        {
            physicalDriveNumber = 0;

            const string Prefix = "PhysicalDrive";

            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var numberPart = value.Substring(Prefix.Length);

            return uint.TryParse(numberPart, NumberStyles.None, CultureInfo.InvariantCulture, out physicalDriveNumber)
                && physicalDriveNumber < PhysicalDriveFallbackScanLimit;
        }

        private static bool ContainsStorageSpacesAggregate(List<StorageDevice> result)
        {
            if (result == null)
            {
                return false;
            }

            foreach (var device in result)
            {
                if (device.BusType == StorageBusType.Spaces || LooksLikeStorageSpacesAggregate(device))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<uint> CreateKnownStorageDeviceNumberSet(List<StorageDevice> devices)
        {
            var knownStorageDeviceNumbers = new HashSet<uint>();

            foreach (var device in devices)
            {
                if (device.StorageDeviceNumber.HasValue)
                {
                    knownStorageDeviceNumbers.Add(device.StorageDeviceNumber.Value);
                }
            }

            return knownStorageDeviceNumbers;
        }

        private static StorageDevice TryCreatePhysicalDriveFallbackDevice(uint physicalDriveNumber, IStorageIoControl ioControl)
        {
            string physicalDrivePath = @"\\.\PhysicalDrive" + physicalDriveNumber.ToString(CultureInfo.InvariantCulture);

            SafeFileHandle handle = ioControl.OpenDevice(
                physicalDrivePath,
                IoAccess.GenericRead,
                IoShare.ReadWrite,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            if (handle == null)
            {
                return null;
            }

            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }

            handle.Dispose();

            var device = new StorageDevice
            {
                DevicePath = physicalDrivePath,
                AlternateDevicePath = physicalDrivePath,
                DeviceInstanceID = string.Empty,
                ParentInstanceID = string.Empty,
                DisplayName = physicalDrivePath,
                DeviceDescription = StorageTextConstants.DiskDrive,
                DeviceTypeLabel = StorageTextConstants.DiskDrive,
                StorageDeviceNumber = physicalDriveNumber
            };

            StorageDetectionEngine.AttachStandardStorageProperties(device, ioControl);

            if (device.StorageDeviceNumber == null)
            {
                device.StorageDeviceNumber = physicalDriveNumber;
            }

            ApplyPhysicalDriveFallbackNames(device, physicalDrivePath);
            StorageDetectionEngine.SelectProbeStrategy(device);

            return device;
        }

        private static void ApplyPhysicalDriveFallbackNames(StorageDevice device, string physicalDrivePath)
        {
            string displayName = StringUtil.FirstNonEmpty(device.ProductName, device.DisplayName, physicalDrivePath);

            device.DisplayName = displayName;
            device.DeviceDescription = StringUtil.FirstNonEmpty(device.DeviceDescription, StorageTextConstants.DiskDrive);
            device.DeviceTypeLabel   = StringUtil.FirstNonEmpty(device.DeviceTypeLabel  , StorageTextConstants.DiskDrive);

            if (string.IsNullOrWhiteSpace(device.VendorName) && device.BusType == StorageBusType.Nvme)
            {
                device.VendorName = StorageTextConstants.Nvme;
            }
        }

        private static bool IsUsablePhysicalDriveFallbackDevice(StorageDevice device)
        {
            if (device.BusType == StorageBusType.Virtual
             || device.BusType == StorageBusType.FileBackedVirtual
             || device.BusType == StorageBusType.Spaces)
            {
                return false;
            }

            if (LooksLikeVirtualOrAggregateDisk(device))
            {
                return false;
            }

            if (device.DiskSizeBytes <= 0
             && string.IsNullOrWhiteSpace(device.ProductName)
             && string.IsNullOrWhiteSpace(device.SerialNumber))
            {
                return false;
            }

            return true;
        }

        private static bool LooksLikeStorageSpacesAggregate(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            return StringUtil.ContainsAny(device.DisplayName      , StorageTextConstants.MSStorageSpaceDevice, StorageTextConstants.StorageSpace)
                || StringUtil.ContainsAny(device.ProductName      , StorageTextConstants.MSStorageSpaceDevice, StorageTextConstants.StorageSpace)
                || StringUtil.ContainsAny(device.DeviceDescription, StorageTextConstants.MSStorageSpaceDevice, StorageTextConstants.StorageSpace)
                || StringUtil.ContainsAny(device.DeviceTypeLabel  , StorageTextConstants.MSStorageSpaceDevice, StorageTextConstants.StorageSpace);
        }

        private static bool LooksLikeVirtualOrAggregateDisk(StorageDevice device)
        {
            return StringUtil.ContainsAny(device.DisplayName      , StorageTextConstants.MSStorageSpaceDevice, StorageTextConstants.StorageSpace, StorageTextConstants.VirtualDisk)
                || StringUtil.ContainsAny(device.ProductName      , StorageTextConstants.MSStorageSpaceDevice, StorageTextConstants.StorageSpace, StorageTextConstants.VirtualDisk)
                || StringUtil.ContainsAny(device.DeviceDescription, StorageTextConstants.MSStorageSpaceDevice, StorageTextConstants.StorageSpace, StorageTextConstants.VirtualDisk);
        }

        #endregion
    }
}
