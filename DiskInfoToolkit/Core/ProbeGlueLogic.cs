/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Smart;
using DiskInfoToolkit.Utilities;
using System.Globalization;
using System.Text;

namespace DiskInfoToolkit.Core
{
    public static class ProbeGlueLogic
    {
        #region Public

        public static void FinalizeDevice(StorageDevice device)
        {
            if (device == null)
            {
                return;
            }

            var beforeSize           = device.DiskSizeBytes;
            var beforeDisplay        = device.DisplayName;
            var beforeControllerKind = device.Controller.Kind;
            var beforeVendor         = device.VendorName;
            var beforeProduct        = device.ProductName;
            var beforeSerial         = device.SerialNumber;
            var beforePath           = device.DevicePath;
            var beforeTransport      = device.TransportKind;
            var beforeBusType        = device.BusType;
            var beforeFamily         = device.Controller.Family;
            var beforeSmart          = device.SupportsSmart;
            var beforeRemovable      = device.IsRemovable;

            ApplyAlternatePathPromotion(device);
            ApplyPathConsistency(device);
            ApplyNvmeDerivedState(device);
            ApplyScsiDerivedIdentity(device);
            ApplyVendorBackendDerivedState(device);
            ApplyInquiryDerivedFlags(device);
            ApplyCapacityConsistency(device);
            ApplyTransportConsistency(device);
            ApplyControllerFamilyConsistency(device);
            ApplyRaidGlueConsistency(device);
            ApplyInquiryDerivedTransport(device);
            ApplyUsbBridgeConsistency(device);
            ApplyRaidPathPreference(device);
            ApplyIdentityPromotion(device);
            ApplyControllerConsistency(device);
            ApplyIdentityNormalization(device);
            ApplyDisplayConsistency(device);
            ApplyRevisionConsistency(device);
            ApplyDeviceTypeConsistency(device);
            ApplyPredictionConsistency(device);
            ApplyPortIdentityConsistency(device);
            ApplySmartConsistency(device);

            SmartAttributeMetadataApplicator.Apply(device);

            if (beforeSize != device.DiskSizeBytes)
            {
                ProbeTraceRecorder.Add(device, "Glue logic: disk size normalized.");
            }

            if (!string.Equals(beforeDisplay ?? string.Empty, device.DisplayName ?? string.Empty, StringComparison.Ordinal))
            {
                ProbeTraceRecorder.Add(device, "Glue logic: display name normalized.");
            }

            if (!string.Equals(beforeControllerKind ?? string.Empty, device.Controller.Kind ?? string.Empty, StringComparison.Ordinal))
            {
                ProbeTraceRecorder.Add(device, "Glue logic: controller kind normalized.");
            }

            if (!string.Equals(beforeVendor ?? string.Empty, device.VendorName ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(beforeProduct ?? string.Empty, device.ProductName ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(beforeSerial ?? string.Empty, device.SerialNumber ?? string.Empty, StringComparison.Ordinal))
            {
                ProbeTraceRecorder.Add(device, "Glue logic: identity normalization applied.");
            }

            if (!string.Equals(beforePath ?? string.Empty, device.DevicePath ?? string.Empty, StringComparison.Ordinal))
            {
                ProbeTraceRecorder.Add(device, "Glue logic: path consistency applied.");
            }

            if (beforeTransport != device.TransportKind || beforeBusType != device.BusType || beforeFamily != device.Controller.Family)
            {
                ProbeTraceRecorder.Add(device, "Glue logic: transport/controller consistency applied.");
            }

            if (!beforeRemovable && device.IsRemovable)
            {
                ProbeTraceRecorder.Add(device, "Glue logic: removable-media state inferred.");
            }

            if (!beforeSmart && device.SupportsSmart)
            {
                ProbeTraceRecorder.Add(device, "Glue logic: SMART support inferred from collected data.");
            }
        }

        #endregion

        #region Private

        private static void ApplyAlternatePathPromotion(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.DevicePath) && !string.IsNullOrWhiteSpace(device.AlternateDevicePath))
            {
                device.DevicePath = device.AlternateDevicePath;
            }
        }

        private static void ApplyPathConsistency(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.AlternateDevicePath) && !string.IsNullOrWhiteSpace(device.DevicePath))
            {
                device.AlternateDevicePath = device.DevicePath;
            }

            if (!string.IsNullOrWhiteSpace(device.AlternateDevicePath)
                && string.IsNullOrWhiteSpace(device.DevicePath)
                && (device.AlternateDevicePath.StartsWith(PathConstants.ScsiDevicePathPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                device.DevicePath = device.AlternateDevicePath;
            }
        }

        private static void ApplyNvmeDerivedState(StorageDevice device)
        {
            if ((device.Nvme.IdentifyControllerData != null && device.Nvme.IdentifyControllerData.Length > 0)
                || (device.Nvme.IntelIdentifyControllerData != null && device.Nvme.IntelIdentifyControllerData.Length > 0)
                || (device.Nvme.SmartLogData != null && device.Nvme.SmartLogData.Length > 0)
                || device.Nvme.NamespaceSize.HasValue
                || device.Nvme.NamespaceCapacity.HasValue)
            {
                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Nvme;
                }

                if (device.BusType == StorageBusType.Unknown)
                {
                    device.BusType = StorageBusType.Nvme;
                }
            }
        }

        private static void ApplyScsiDerivedIdentity(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.VendorName) && !string.IsNullOrWhiteSpace(device.Scsi.InquiryVendorID))
            {
                device.VendorName = StringUtil.TrimStorageString(device.Scsi.InquiryVendorID);
            }

            if (string.IsNullOrWhiteSpace(device.ProductName) && !string.IsNullOrWhiteSpace(device.Scsi.InquiryProductID))
            {
                device.ProductName = StringUtil.TrimStorageString(device.Scsi.InquiryProductID);
            }

            if (string.IsNullOrWhiteSpace(device.ProductRevision) && !string.IsNullOrWhiteSpace(device.Scsi.InquiryProductRevision))
            {
                device.ProductRevision = StringUtil.TrimStorageString(device.Scsi.InquiryProductRevision);
            }

            if (string.IsNullOrWhiteSpace(device.SerialNumber) && !string.IsNullOrWhiteSpace(device.Scsi.DeviceIdentifier))
            {
                device.SerialNumber = StringUtil.TrimStorageString(device.Scsi.DeviceIdentifier);
            }
        }

        private static void ApplyVendorBackendDerivedState(StorageDevice device)
        {
            if (device.Controller.Family == StorageControllerFamily.RocketRaid)
            {
                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }

                if (device.BusType == StorageBusType.Unknown)
                {
                    device.BusType = StorageBusType.RAID;
                }

                if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                {
                    device.Controller.Kind = StorageTextConstants.HighPoint;
                }
            }

            if (device.Controller.Family == StorageControllerFamily.MegaRaid)
            {
                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }

                if (device.BusType == StorageBusType.Unknown)
                {
                    device.BusType = StorageBusType.RAID;
                }

                if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                {
                    device.Controller.Kind = StorageTextConstants.MegaRaid;
                }
            }

            if (device.Usb.IsMassStorageLike && device.BusType == StorageBusType.Unknown)
            {
                device.BusType = StorageBusType.Usb;
            }

            if (device.Usb.IsMassStorageLike && device.TransportKind == StorageTransportKind.Unknown)
            {
                device.TransportKind = StorageTransportKind.Usb;
            }

            if (!string.IsNullOrWhiteSpace(device.Usb.BridgeFamily) && string.IsNullOrWhiteSpace(device.Controller.Kind))
            {
                device.Controller.Kind = device.Usb.BridgeFamily + StorageTextConstants.UsbBridgeSuffix;
            }
        }

        private static void ApplyInquiryDerivedFlags(StorageDevice device)
        {
            if (device.Scsi.RemovableMedia.HasValue && device.Scsi.RemovableMedia.Value)
            {
                device.IsRemovable = true;
            }

            if ((!device.Scsi.PeripheralDeviceType.HasValue || device.Scsi.PeripheralDeviceType.Value == 0)
                && string.IsNullOrWhiteSpace(device.DeviceTypeLabel)
                && !string.IsNullOrWhiteSpace(device.ProductName))
            {
                device.DeviceTypeLabel = StorageTextConstants.DiskDrive;
            }
        }

        private static void ApplyControllerFamilyConsistency(StorageDevice device)
        {
            if (device.Controller.Family != StorageControllerFamily.Unknown)
            {
                return;
            }

            string service = device.Controller.Service ?? string.Empty;
            string controllerClass = device.Controller.Class ?? string.Empty;

            if (service.Equals(ControllerServiceNames.MegaSas, StringComparison.OrdinalIgnoreCase))
            {
                device.Controller.Family = StorageControllerFamily.MegaRaid;
                return;
            }

            if (service.StartsWith(ControllerServiceNames.LsiSas, StringComparison.OrdinalIgnoreCase) || service.StartsWith(ControllerServiceNames.ItSas35, StringComparison.OrdinalIgnoreCase))
            {
                device.Controller.Family = StorageControllerFamily.LsiSas;
                return;
            }

            if (service.Equals(ControllerServiceNames.UaspStor, StringComparison.OrdinalIgnoreCase))
            {
                device.Controller.Family = StorageControllerFamily.UaspStor;
                return;
            }

            if (service.Equals(ControllerServiceNames.UsbStor, StringComparison.OrdinalIgnoreCase))
            {
                device.Controller.Family = StorageControllerFamily.UsbStor;
                return;
            }

            if (service.Equals(ControllerServiceNames.Storahci, StringComparison.OrdinalIgnoreCase))
            {
                device.Controller.Family = StorageControllerFamily.Ahci;
                return;
            }

            if (service.Equals(ControllerServiceNames.AmdSata, StringComparison.OrdinalIgnoreCase) || service.Equals(ControllerServiceNames.AmdSataAlt, StringComparison.OrdinalIgnoreCase))
            {
                device.Controller.Family = StorageControllerFamily.AmdSata;
                return;
            }

            if (controllerClass.Equals(ControllerClassNames.Raid, StringComparison.OrdinalIgnoreCase) || controllerClass.Equals(ControllerClassNames.Sas, StringComparison.OrdinalIgnoreCase))
            {
                device.Controller.Family = StorageControllerFamily.Generic;
            }
        }

        private static void ApplyRaidGlueConsistency(StorageDevice device)
        {
            if (device.Controller.Family == StorageControllerFamily.IntelRst
                || device.Controller.Family == StorageControllerFamily.IntelVroc
                || device.Controller.Family == StorageControllerFamily.MegaRaid
                || device.Controller.Family == StorageControllerFamily.RocketRaid
                || device.Controller.Family == StorageControllerFamily.LsiSas)
            {
                if (device.TransportKind == StorageTransportKind.Unknown)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }

                if (device.BusType == StorageBusType.Unknown)
                {
                    device.BusType = StorageBusType.RAID;
                }
            }
        }

        private static void ApplyInquiryDerivedTransport(StorageDevice device)
        {
            if (!device.Scsi.PeripheralDeviceType.HasValue)
            {
                return;
            }

            if (device.TransportKind == StorageTransportKind.Unknown)
            {
                if (device.Scsi.PeripheralDeviceType.Value == 0)
                {
                    if (device.BusType == StorageBusType.Usb)
                    {
                        device.TransportKind = StorageTransportKind.Usb;
                    }
                    else if (device.BusType == StorageBusType.RAID)
                    {
                        device.TransportKind = StorageTransportKind.Raid;
                    }
                    else
                    {
                        device.TransportKind = StorageTransportKind.Scsi;
                    }
                }
            }
        }

        private static void ApplyUsbBridgeConsistency(StorageDevice device)
        {
            if (!device.Usb.IsMassStorageLike && string.IsNullOrWhiteSpace(device.Usb.BridgeFamily))
            {
                return;
            }

            if (device.BusType == StorageBusType.Unknown)
            {
                device.BusType = StorageBusType.Usb;
            }

            if (device.TransportKind == StorageTransportKind.Unknown)
            {
                if ((device.Nvme.IdentifyControllerData != null && device.Nvme.IdentifyControllerData.Length > 0)
                    || (device.Nvme.SmartLogData != null && device.Nvme.SmartLogData.Length > 0)
                    || (device.Nvme.IntelIdentifyControllerData != null && device.Nvme.IntelIdentifyControllerData.Length > 0))
                {
                    device.TransportKind = StorageTransportKind.Nvme;
                }
                else
                {
                    device.TransportKind = StorageTransportKind.Usb;
                }
            }

            if (string.IsNullOrWhiteSpace(device.Controller.Kind) && !string.IsNullOrWhiteSpace(device.Usb.BridgeFamily))
            {
                device.Controller.Kind = device.Usb.BridgeFamily + StorageTextConstants.UsbBridgeSuffix;
            }
        }

        private static void ApplyRaidPathPreference(StorageDevice device)
        {
            if (device.Controller.Family != StorageControllerFamily.IntelRst
                && device.Controller.Family != StorageControllerFamily.IntelVroc
                && device.Controller.Family != StorageControllerFamily.MegaRaid
                && device.Controller.Family != StorageControllerFamily.RocketRaid
                && device.Controller.Family != StorageControllerFamily.LsiSas)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(device.AlternateDevicePath)
                && device.AlternateDevicePath.StartsWith(PathConstants.ScsiDevicePathPrefix, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(device.DevicePath)
                    || !device.DevicePath.StartsWith(PathConstants.ScsiDevicePathPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                device.DevicePath = device.AlternateDevicePath;
            }
        }

        private static void ApplyIdentityPromotion(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.ProductName) && !string.IsNullOrWhiteSpace(device.DisplayName)
                && !device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase)
                && !device.DisplayName.Equals(StorageTextConstants.DiskDrive, StringComparison.OrdinalIgnoreCase))
            {
                device.ProductName = device.DisplayName;
            }

            if (string.IsNullOrWhiteSpace(device.DisplayName) && !string.IsNullOrWhiteSpace(device.ProductName))
            {
                device.DisplayName = device.ProductName;
            }

            if (string.IsNullOrWhiteSpace(device.Controller.Name) && !string.IsNullOrWhiteSpace(device.Controller.Kind))
            {
                device.Controller.Name = device.Controller.Kind;
            }

            if (string.IsNullOrWhiteSpace(device.Controller.Identifier) && device.Scsi.PortNumber.HasValue)
            {
                device.Controller.Identifier = "SCSI-PORT-" + device.Scsi.PortNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static void ApplyRevisionConsistency(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.ProductRevision) && !string.IsNullOrWhiteSpace(device.Scsi.InquiryProductRevision))
            {
                device.ProductRevision = device.Scsi.InquiryProductRevision;
            }

            if (string.IsNullOrWhiteSpace(device.ProductRevision) && device.Controller.Revision.HasValue)
            {
                device.ProductRevision = device.Controller.Revision.Value.ToString("X2", CultureInfo.InvariantCulture);
            }
        }

        private static void ApplyDeviceTypeConsistency(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceTypeLabel))
            {
                if (device.TransportKind == StorageTransportKind.Nvme)
                {
                    device.DeviceTypeLabel = StorageTextConstants.NvmeDrive;
                }
                else if (device.TransportKind == StorageTransportKind.Raid)
                {
                    device.DeviceTypeLabel = StorageTextConstants.RaidDisk;
                }
                else if (device.TransportKind == StorageTransportKind.Usb)
                {
                    device.DeviceTypeLabel = ControllerKindNames.UsbStorage;
                }
                else if (device.TransportKind == StorageTransportKind.Sd)
                {
                    device.DeviceTypeLabel = StorageTextConstants.SdCard;
                }
                else if (device.TransportKind == StorageTransportKind.Mmc)
                {
                    device.DeviceTypeLabel = StorageTextConstants.MmcCard;
                }
                else if (!string.IsNullOrWhiteSpace(device.ProductName) || !string.IsNullOrWhiteSpace(device.Scsi.InquiryProductID))
                {
                    device.DeviceTypeLabel = StorageTextConstants.DiskDrive;
                }
            }

            if (device.Scsi.PeripheralDeviceType.HasValue && device.Scsi.PeripheralDeviceType.Value == 5 && string.IsNullOrWhiteSpace(device.DeviceTypeLabel))
            {
                device.DeviceTypeLabel = StorageTextConstants.CdDvdDevice;
            }
        }

        private static void ApplyPredictionConsistency(StorageDevice device)
        {
            if (device.PredictsFailure.HasValue && device.PredictsFailure.Value)
            {
                device.SupportsSmart = true;
            }
        }

        private static void ApplyPortIdentityConsistency(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.Controller.Identifier) && device.Scsi.PortNumber.HasValue)
            {
                StringBuilder builder = new StringBuilder();
                builder.Append("SCSI-");
                builder.Append(device.Scsi.PortNumber.Value.ToString(CultureInfo.InvariantCulture));

                if (device.Scsi.PathID.HasValue)
                {
                    builder.Append('-');
                    builder.Append(device.Scsi.PathID.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (device.Scsi.TargetID.HasValue)
                {
                    builder.Append('-');
                    builder.Append(device.Scsi.TargetID.Value.ToString(CultureInfo.InvariantCulture));
                }

                if (device.Scsi.Lun.HasValue)
                {
                    builder.Append('-');
                    builder.Append(device.Scsi.Lun.Value.ToString(CultureInfo.InvariantCulture));
                }

                device.Controller.Identifier = builder.ToString();
            }

            if (string.IsNullOrWhiteSpace(device.Controller.Name) && !string.IsNullOrWhiteSpace(device.Controller.Identifier))
            {
                device.Controller.Name = device.Controller.Identifier;
            }
        }

        private static void ApplyIdentityNormalization(StorageDevice device)
        {
            device.VendorName                  = NormalizePrintableIdentity(device.VendorName);
            device.ProductName                 = NormalizePrintableIdentity(device.ProductName);
            device.ProductRevision             = NormalizePrintableIdentity(device.ProductRevision);
            device.SerialNumber                = NormalizePrintableIdentity(device.SerialNumber);
            device.Controller.Name             = NormalizePrintableIdentity(device.Controller.Name);
            device.Controller.Kind             = NormalizePrintableIdentity(device.Controller.Kind);
            device.Controller.Identifier       = NormalizePrintableIdentity(device.Controller.Identifier);
            device.Scsi.InquiryVendorID        = NormalizePrintableIdentity(device.Scsi.InquiryVendorID);
            device.Scsi.InquiryProductID       = NormalizePrintableIdentity(device.Scsi.InquiryProductID);
            device.Scsi.InquiryProductRevision = NormalizePrintableIdentity(device.Scsi.InquiryProductRevision);
            device.Scsi.DeviceIdentifier       = NormalizePrintableIdentity(device.Scsi.DeviceIdentifier);

            if (string.IsNullOrWhiteSpace(device.SerialNumber) && !string.IsNullOrWhiteSpace(device.Scsi.DeviceIdentifier))
            {
                device.SerialNumber = device.Scsi.DeviceIdentifier;
            }
        }

        private static string NormalizePrintableIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            bool previousSpace = false;

            for (int i = 0; i < value.Length; ++i)
            {
                char ch = value[i];
                if (char.IsControl(ch))
                {
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (!previousSpace)
                    {
                        builder.Append(' ');
                        previousSpace = true;
                    }
                    continue;
                }

                previousSpace = false;
                builder.Append(ch);
            }

            return StringUtil.TrimStorageString(builder.ToString());
        }

        private static void ApplyCapacityConsistency(StorageDevice device)
        {
            if ((!device.DiskSizeBytes.HasValue || device.DiskSizeBytes.Value <= 0)
                && device.Nvme.NamespaceCapacity.HasValue
                && device.Nvme.NamespaceLbaDataSize.HasValue
                && device.Nvme.NamespaceCapacity.Value > 0
                && device.Nvme.NamespaceLbaDataSize.Value > 0)
            {
                try
                {
                    checked
                    {
                        device.DiskSizeBytes = device.Nvme.NamespaceCapacity.Value * device.Nvme.NamespaceLbaDataSize.Value;
                    }
                }
                catch
                {
                }

                if (string.IsNullOrWhiteSpace(device.CapacitySource))
                {
                    device.CapacitySource = "NVMe Namespace Capacity";
                }
            }

            if ((!device.DiskSizeBytes.HasValue || device.DiskSizeBytes.Value <= 0)
                && device.Nvme.NamespaceSize.HasValue
                && device.Nvme.NamespaceLbaDataSize.HasValue
                && device.Nvme.NamespaceSize.Value > 0
                && device.Nvme.NamespaceLbaDataSize.Value > 0)
            {
                try
                {
                    checked
                    {
                        device.DiskSizeBytes = device.Nvme.NamespaceSize.Value * device.Nvme.NamespaceLbaDataSize.Value;
                    }
                }
                catch
                {
                }

                if (string.IsNullOrWhiteSpace(device.CapacitySource))
                {
                    device.CapacitySource = "NVMe Namespace Size";
                }
            }

            if ((!device.DiskSizeBytes.HasValue || device.DiskSizeBytes.Value <= 0)
                && device.Scsi.LastLogicalBlockAddress.HasValue
                && device.Scsi.LogicalBlockLength.HasValue
                && device.Scsi.LogicalBlockLength.Value > 0)
            {
                try
                {
                    checked
                    {
                        device.DiskSizeBytes = (device.Scsi.LastLogicalBlockAddress.Value + 1UL) * device.Scsi.LogicalBlockLength.Value;
                    }
                }
                catch
                {
                }

                if (string.IsNullOrWhiteSpace(device.CapacitySource))
                {
                    device.CapacitySource = "SCSI Capacity";
                }
            }
        }

        private static void ApplyTransportConsistency(StorageDevice device)
        {
            if (device.TransportKind == StorageTransportKind.Unknown)
            {
                if (device.BusType == StorageBusType.Nvme)
                {
                    device.TransportKind = StorageTransportKind.Nvme;
                }
                else if (device.BusType == StorageBusType.Usb)
                {
                    device.TransportKind = StorageTransportKind.Usb;
                }
                else if (device.BusType == StorageBusType.Sas)
                {
                    device.TransportKind = StorageTransportKind.Sas;
                }
                else if (device.BusType == StorageBusType.Sata || device.BusType == StorageBusType.Ata || device.BusType == StorageBusType.Atapi)
                {
                    device.TransportKind = StorageTransportKind.Ata;
                }
                else if (device.BusType == StorageBusType.Scsi)
                {
                    device.TransportKind = StorageTransportKind.Scsi;
                }
                else if (device.BusType == StorageBusType.RAID)
                {
                    device.TransportKind = StorageTransportKind.Raid;
                }
                else if (device.BusType == StorageBusType.Sd)
                {
                    device.TransportKind = StorageTransportKind.Sd;
                }
                else if (device.BusType == StorageBusType.Mmc)
                {
                    device.TransportKind = StorageTransportKind.Mmc;
                }
            }

            if (device.BusType == StorageBusType.Unknown)
            {
                if (device.TransportKind == StorageTransportKind.Nvme)
                {
                    device.BusType = StorageBusType.Nvme;
                }
                else if (device.TransportKind == StorageTransportKind.Usb)
                {
                    device.BusType = StorageBusType.Usb;
                }
                else if (device.TransportKind == StorageTransportKind.Sas)
                {
                    device.BusType = StorageBusType.Sas;
                }
                else if (device.TransportKind == StorageTransportKind.Raid)
                {
                    device.BusType = StorageBusType.RAID;
                }
                else if (device.TransportKind == StorageTransportKind.Sd)
                {
                    device.BusType = StorageBusType.Sd;
                }
                else if (device.TransportKind == StorageTransportKind.Mmc)
                {
                    device.BusType = StorageBusType.Mmc;
                }
            }
        }

        private static void ApplyControllerConsistency(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.Controller.Kind))
            {
                if (device.Controller.Family == StorageControllerFamily.MegaRaid)
                {
                    device.Controller.Kind = StorageTextConstants.MegaRaid;
                }
                else if (device.Controller.Family == StorageControllerFamily.RocketRaid)
                {
                    device.Controller.Kind = StorageTextConstants.HighPoint;
                }
                else if (device.Controller.Family == StorageControllerFamily.IntelVroc)
                {
                    device.Controller.Kind = ControllerKindNames.Vroc;
                }
                else if (device.Controller.Family == StorageControllerFamily.IntelRst)
                {
                    device.Controller.Kind = ControllerKindNames.IntelRst;
                }
                else if (device.Controller.Family == StorageControllerFamily.StorNvme)
                {
                    device.Controller.Kind = ControllerKindNames.Nvme;
                }
                else if (device.Controller.Family == StorageControllerFamily.UaspStor)
                {
                    device.Controller.Kind = ControllerKindNames.UsbUasp;
                }
                else if (device.Controller.Family == StorageControllerFamily.UsbStor)
                {
                    device.Controller.Kind = ControllerKindNames.UsbStorage;
                }
                else if (device.Controller.Family == StorageControllerFamily.RealtekSd)
                {
                    device.Controller.Kind = ControllerKindNames.SdMmc;
                }
                else if (!string.IsNullOrWhiteSpace(device.Controller.Service))
                {
                    device.Controller.Kind = StringUtil.TrimStorageString(device.Controller.Service);
                }
                else if (!string.IsNullOrWhiteSpace(device.Controller.Class))
                {
                    device.Controller.Kind = StringUtil.TrimStorageString(device.Controller.Class);
                }
            }
        }

        private static void ApplyDisplayConsistency(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.DisplayName))
            {
                if (!string.IsNullOrWhiteSpace(device.ProductName))
                {
                    device.DisplayName = StringUtil.TrimStorageString(device.ProductName);
                }
                else if (!string.IsNullOrWhiteSpace(device.DeviceDescription))
                {
                    device.DisplayName = StringUtil.TrimStorageString(device.DeviceDescription);
                }
            }

            if (string.IsNullOrWhiteSpace(device.DisplayName)
                || device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase)
                || device.DisplayName.Equals(StorageTextConstants.DiskDrive, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(device.ProductName))
                {
                    device.DisplayName = StringUtil.TrimStorageString(device.ProductName);
                }
                else if (!string.IsNullOrWhiteSpace(device.Scsi.InquiryProductID))
                {
                    device.DisplayName = StringUtil.TrimStorageString(device.Scsi.InquiryProductID);
                }
                else if (!string.IsNullOrWhiteSpace(device.DeviceDescription))
                {
                    device.DisplayName = StringUtil.TrimStorageString(device.DeviceDescription);
                }
            }
        }

        private static void ApplySmartConsistency(StorageDevice device)
        {
            if (!device.SupportsSmart)
            {
                if ((device.SmartAttributes != null && device.SmartAttributes.Count > 0)
                    || (device.Nvme.SmartLogData != null && device.Nvme.SmartLogData.Length > 0)
                    || device.SmartVersionRaw != 0)
                {
                    device.SupportsSmart = true;
                }
            }
        }

        #endregion
    }
}
