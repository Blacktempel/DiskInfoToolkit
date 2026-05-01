/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using System.Reflection;

namespace DiskInfoToolkit.Monitoring
{
    internal static class StorageDeviceCloneHelper
    {
        #region Properties

        private static readonly PropertyInfo[] DeviceProperties = typeof(StorageDevice)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();

        #endregion

        #region Public

        public static List<StorageDevice> CloneList(IEnumerable<StorageDevice> devices)
        {
            var result = new List<StorageDevice>();
            if (devices == null)
            {
                return result;
            }

            foreach (StorageDevice device in devices)
            {
                result.Add(Clone(device));
            }

            return result;
        }

        public static StorageDevice Clone(StorageDevice source)
        {
            var clone = new StorageDevice();
            CopyCore(source, clone);
            return clone;
        }

        public static bool CopyInto(StorageDevice source, StorageDevice target)
        {
            if (source == null || target == null)
            {
                return false;
            }

            const string Same = "same";

            var before = StorageDeviceSnapshotComparer.AreDifferent(target, source) ? string.Empty : Same;
            CopyCore(source, target);
            return before != Same;
        }

        #endregion

        #region Private

        private static void CopyCore(StorageDevice source, StorageDevice target)
        {
            foreach (var property in DeviceProperties)
            {
                object value = property.GetValue(source, null);
                property.SetValue(target, ClonePropertyValue(value), null);
            }
        }

        private static object ClonePropertyValue(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string)
            {
                return value;
            }

            if (value is byte[] bytes)
            {
                var clone = new byte[bytes.Length];
                Buffer.BlockCopy(bytes, 0, clone, 0, bytes.Length);
                return clone;
            }

            if (value is List<string> strings)
            {
                return new List<string>(strings);
            }

            if (value is List<SmartAttributeEntry> attributes)
            {
                var clone = new List<SmartAttributeEntry>(attributes.Count);

                foreach (var item in attributes)
                {
                    clone.Add(new SmartAttributeEntry
                    {
                        ID             = item.ID,
                        StatusFlags    = item.StatusFlags,
                        CurrentValue   = item.CurrentValue,
                        WorstValue     = item.WorstValue,
                        RawValue       = item.RawValue,
                        ThresholdValue = item.ThresholdValue,
                        AttributeKey   = item.AttributeKey,
                        Name           = item.Name
                    });
                }

                return clone;
            }

            if (value is List<StoragePartitionInfo> partitions)
            {
                var clone = new List<StoragePartitionInfo>(partitions.Count);

                foreach (var item in partitions)
                {
                    clone.Add(new StoragePartitionInfo
                    {
                        PartitionStyle          = item.PartitionStyle,
                        StartingOffset          = item.StartingOffset,
                        PartitionLength         = item.PartitionLength,
                        PartitionNumber         = item.PartitionNumber,
                        RewritePartition        = item.RewritePartition,
                        IsServicePartition      = item.IsServicePartition,
                        IsDynamicDiskPartition  = item.IsDynamicDiskPartition,
                        DriveLetter             = item.DriveLetter,
                        VolumePath              = item.VolumePath,
                        AvailableFreeSpaceBytes = item.AvailableFreeSpaceBytes,
                        MbrPartitionType        = item.MbrPartitionType,
                        MbrBootIndicator        = item.MbrBootIndicator,
                        MbrRecognizedPartition  = item.MbrRecognizedPartition,
                        MbrPartitionID          = item.MbrPartitionID,
                        GptPartitionType        = item.GptPartitionType,
                        GptPartitionID          = item.GptPartitionID,
                        GptAttributes           = item.GptAttributes,
                        GptName                 = item.GptName
                    });
                }

                return clone;
            }

            if (value is StorageControllerInfo controller)
            {
                return new StorageControllerInfo
                {
                    Name                 = controller.Name,
                    Service              = controller.Service,
                    Class                = controller.Class,
                    Kind                 = controller.Kind,
                    Identifier           = controller.Identifier,
                    Family               = controller.Family,
                    VendorName           = controller.VendorName,
                    DeviceName           = controller.DeviceName,
                    HardwareID           = controller.HardwareID,
                    VendorID             = controller.VendorID,
                    DeviceID             = controller.DeviceID,
                    Revision             = controller.Revision,
                    IsUsbStyleHardwareID = controller.IsUsbStyleHardwareID
                };
            }

            if (value is StorageUsbInfo usb)
            {
                return new StorageUsbInfo
                {
                    BridgeFamily            = usb.BridgeFamily,
                    MassStorageProtocolName = usb.MassStorageProtocolName,
                    IsMassStorageLike       = usb.IsMassStorageLike,
                    NvmeSetupMode           = usb.NvmeSetupMode
                };
            }

            if (value is StorageScsiInfo scsi)
            {
                return new StorageScsiInfo
                {
                    PortNumber              = scsi.PortNumber,
                    PathID                  = scsi.PathID,
                    TargetID                = scsi.TargetID,
                    Lun                     = scsi.Lun,
                    LastLogicalBlockAddress = scsi.LastLogicalBlockAddress,
                    LogicalBlockLength      = scsi.LogicalBlockLength,
                    PeripheralDeviceType    = scsi.PeripheralDeviceType,
                    RemovableMedia          = scsi.RemovableMedia,
                    InquiryVendorID         = scsi.InquiryVendorID,
                    InquiryProductID        = scsi.InquiryProductID,
                    InquiryProductRevision  = scsi.InquiryProductRevision,
                    DeviceIdentifier        = scsi.DeviceIdentifier
                };
            }

            if (value is StorageNvmeInfo nvme)
            {
                return new StorageNvmeInfo
                {
                    IdentifyControllerData      = CloneByteArray(nvme.IdentifyControllerData),
                    IdentifyNamespaceData       = CloneByteArray(nvme.IdentifyNamespaceData),
                    SmartLogData                = CloneByteArray(nvme.SmartLogData),
                    NamespaceSize               = nvme.NamespaceSize,
                    NamespaceCapacity           = nvme.NamespaceCapacity,
                    NamespaceUtilization        = nvme.NamespaceUtilization,
                    NamespaceLbaDataSize        = nvme.NamespaceLbaDataSize,
                    NamespaceFormattedLbaIndex  = nvme.NamespaceFormattedLbaIndex,
                    IntelIdentifyControllerData = CloneByteArray(nvme.IntelIdentifyControllerData),
                    IntelSmartLogData           = CloneByteArray(nvme.IntelSmartLogData)
                };
            }

            if (value is StorageProbePlan probePlan)
            {
                return new StorageProbePlan
                {
                    IsInitialized       = probePlan.IsInitialized,
                    Strategy            = probePlan.Strategy,
                    ControllerFamily    = probePlan.ControllerFamily,
                    DevicePath          = probePlan.DevicePath,
                    DeviceInstanceID    = probePlan.DeviceInstanceID,
                    ControllerService   = probePlan.ControllerService,
                    ControllerIdentifier = probePlan.ControllerIdentifier,
                    SuccessfulOperations = probePlan.SuccessfulOperations != null
                        ? new List<StorageProbeOperation>(probePlan.SuccessfulOperations)
                        : new List<StorageProbeOperation>()
                };
            }

            if (value is StorageCsmiInfo csmi)
            {
                return new StorageCsmiInfo
                {
                    PhyCount               = csmi.PhyCount,
                    PortIdentifier         = csmi.PortIdentifier,
                    AttachedPhyIdentifier  = csmi.AttachedPhyIdentifier,
                    NegotiatedLinkRate     = csmi.NegotiatedLinkRate,
                    NegotiatedLinkRateName = csmi.NegotiatedLinkRateName,
                    AttachedSasAddress     = csmi.AttachedSasAddress,
                    TargetProtocol         = csmi.TargetProtocol
                };
            }

            return value;
        }

        private static byte[] CloneByteArray(byte[] source)
        {
            if (source == null)
            {
                return null;
            }

            var clone = new byte[source.Length];
            Buffer.BlockCopy(source, 0, clone, 0, source.Length);
            return clone;
        }

        #endregion
    }
}
