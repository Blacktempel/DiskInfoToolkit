/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;
using System.Text;

namespace DiskInfoToolkit.Monitoring
{
    internal static class StorageDeviceSnapshotComparer
    {
        #region Public

        public static bool AreDifferent(StorageDevice left, StorageDevice right)
        {
            return !string.Equals(BuildFingerprint(left), BuildFingerprint(right), StringComparison.Ordinal);
        }

        #endregion

        #region Private

        private static string BuildFingerprint(StorageDevice device)
        {
            if (device == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            Append(builder, device.DevicePath);
            Append(builder, device.AlternateDevicePath);
            Append(builder, device.DeviceInstanceID);
            Append(builder, device.DisplayName);
            Append(builder, device.DeviceDescription);
            Append(builder, device.Controller.Name);
            Append(builder, device.Controller.Service);
            Append(builder, device.Controller.Class);
            Append(builder, device.Controller.Kind);
            Append(builder, device.Controller.Identifier);
            Append(builder, device.Controller.VendorName);
            Append(builder, device.Controller.DeviceName);
            Append(builder, device.Controller.HardwareID);
            Append(builder, device.Controller.VendorID);
            Append(builder, device.Controller.DeviceID);
            Append(builder, device.Controller.Revision);
            Append(builder, device.Controller.IsUsbStyleHardwareID);
            Append(builder, device.VendorName);
            Append(builder, device.ProductName);
            Append(builder, device.ProductRevision);
            Append(builder, device.SerialNumber);
            Append(builder, device.TransportKind);
            Append(builder, device.Controller.Family);
            Append(builder, device.ProbeStrategy);
            Append(builder, device.BusType);
            Append(builder, device.SupportsSmart);
            Append(builder, device.IsRemovable);
            Append(builder, device.IsDevicePowerOn);
            Append(builder, device.IsFiltered);
            Append(builder, device.FilterReason);
            Append(builder, device.StorageDeviceNumber);
            Append(builder, device.DiskSizeBytes);
            Append(builder, device.PredictsFailure);
            Append(builder, device.Temperature);
            Append(builder, device.Health);
            Append(builder, device.HealthStatus);
            Append(builder, device.HealthStatusReason);
            Append(builder, device.PowerOnHours);
            Append(builder, device.Usb.BridgeFamily);
            Append(builder, device.Usb.MassStorageProtocolName);
            Append(builder, device.Usb.IsMassStorageLike);
            Append(builder, device.Usb.NvmeSetupMode);
            Append(builder, device.Scsi.PortNumber);
            Append(builder, device.Scsi.PathID);
            Append(builder, device.Scsi.TargetID);
            Append(builder, device.Scsi.Lun);
            Append(builder, device.Scsi.LastLogicalBlockAddress);
            Append(builder, device.Scsi.LogicalBlockLength);
            Append(builder, device.Scsi.PeripheralDeviceType);
            Append(builder, device.Scsi.RemovableMedia);
            Append(builder, device.Scsi.InquiryVendorID);
            Append(builder, device.Scsi.InquiryProductID);
            Append(builder, device.Scsi.InquiryProductRevision);
            Append(builder, device.Scsi.DeviceIdentifier);
            Append(builder, device.Nvme.NamespaceSize);
            Append(builder, device.Nvme.NamespaceCapacity);
            Append(builder, device.Nvme.NamespaceUtilization);
            Append(builder, device.Nvme.NamespaceLbaDataSize);
            Append(builder, device.Nvme.NamespaceFormattedLbaIndex);
            Append(builder, device.Csmi.PhyCount);
            Append(builder, device.Csmi.PortIdentifier);
            Append(builder, device.Csmi.AttachedPhyIdentifier);
            Append(builder, device.Csmi.NegotiatedLinkRate);
            Append(builder, device.Csmi.NegotiatedLinkRateName);
            Append(builder, device.Csmi.AttachedSasAddress);
            Append(builder, device.Csmi.TargetProtocol);
            Append(builder, device.PartitionsAreStale);

            if (device.Partitions != null)
            {
                foreach (var partition in device.Partitions)
                {
                    Append(builder, partition.PartitionStyle);
                    Append(builder, partition.StartingOffset);
                    Append(builder, partition.PartitionLength);
                    Append(builder, partition.PartitionNumber);
                    Append(builder, partition.DriveLetter);
                    Append(builder, partition.VolumePath);
                    Append(builder, partition.AvailableFreeSpaceBytes);
                }
            }

            return builder.ToString();
        }

        private static void Append(StringBuilder builder, object value)
        {
            builder.Append(value != null ? value.ToString() : string.Empty);
            builder.Append('|');
        }

        #endregion
    }
}
