/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Monitoring;
using DiskInfoToolkit.Smart;
using DiskInfoToolkit.Utilities;

namespace DiskInfoToolkit
{
    /// <summary>
    /// Represents a storage device.
    /// </summary>
    public sealed class StorageDevice : IEquatable<StorageDevice>
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageDevice"/> class.
        /// </summary>
        public StorageDevice()
        {
            DevicePath = string.Empty;
            AlternateDevicePath = string.Empty;
            DeviceInstanceID = string.Empty;
            ParentInstanceID = string.Empty;
            DisplayName = string.Empty;
            DeviceDescription = string.Empty;
            DeviceTypeLabel = StorageTextConstants.DiskDrive;
            VendorName = string.Empty;
            ProductName = string.Empty;
            ProductRevision = string.Empty;
            SerialNumber = string.Empty;
            SdProtocolName = string.Empty;
            TransportKind = StorageTransportKind.Unknown;
            Controller = new StorageControllerInfo();
            ProbeStrategy = ProbeStrategy.GenericStorageProbe;
            BusType = StorageBusType.Unknown;
            FilterReason = string.Empty;
            Usb = new StorageUsbInfo();
            CapacitySource = string.Empty;
            SmartAttributes = new List<SmartAttributeEntry>();
            SmartAttributeProfile = SmartAttributeProfile.Unknown;
            ProbeTrace = new List<string>();
            ProbePlan = new StorageProbePlan();
            PredictFailureVendorData = Array.Empty<byte>();
            Scsi = new StorageScsiInfo();
            Nvme = new StorageNvmeInfo();
            Csmi = new StorageCsmiInfo();
            Partitions = new List<StoragePartitionInfo>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Device path is the primary unique identifier for a storage device and is used for all direct interactions with the device.
        /// </summary>
        public string DevicePath { get; set; }

        /// <summary>
        /// Alternate device path is an additional unique identifier for a storage device that may be used for direct interactions with the device, but is not guaranteed to be present on all devices.
        /// </summary>
        public string AlternateDevicePath { get; set; }

        /// <summary>
        /// Device instance ID is the unique identifier assigned to a device by the Windows Plug and Play manager.<br/>
        /// It is used for correlating with other sources of device information such as WMI or SetupAPI, but is not used for direct interactions with the device.
        /// </summary>
        public string DeviceInstanceID { get; set; }

        /// <summary>
        /// Parent instance ID is the device instance ID of the parent device as assigned by the Windows Plug and Play manager, or null if there is no parent device.
        /// </summary>
        public string ParentInstanceID { get; set; }

        /// <summary>
        /// Display name is a user-friendly name for the device that may be constructed from various properties of the device.<br/>
        /// Usually it is the model name of the drive.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Device description is fetched via SetupAPIs device description property.
        /// </summary>
        public string DeviceDescription { get; set; }

        /// <summary>
        /// Device type label is a user-friendly label describing the type of the device, such as "Disk drive" or "NVMe drive".<br/>
        /// </summary>
        public string DeviceTypeLabel { get; set; }

        /// <summary>
        /// Gets or sets information about the associated storage controller.
        /// </summary>
        public StorageControllerInfo Controller { get; set; }

        /// <summary>
        /// Gets or sets the vendor name.
        /// </summary>
        public string VendorName { get; set; }

        /// <summary>
        /// Gets or sets the product name.
        /// </summary>
        public string ProductName { get; set; }

        /// <summary>
        /// Gets or sets the product revision.
        /// </summary>
        public string ProductRevision { get; set; }

        /// <summary>
        /// Gets or sets the serial number.
        /// </summary>
        public string SerialNumber { get; set; }

        /// <summary>
        /// Gets or sets the sd protocol name.
        /// </summary>
        public string SdProtocolName { get; set; }

        /// <summary>
        /// Gets or sets the transport kind.
        /// </summary>
        public StorageTransportKind TransportKind { get; set; }

        /// <summary>
        /// Gets or sets the probe strategy.
        /// </summary>
        public ProbeStrategy ProbeStrategy { get; set; }

        /// <summary>
        /// Gets or sets the bus type.
        /// </summary>
        public StorageBusType BusType { get; set; }

        /// <summary>
        /// Gets or sets the sd protocol type.
        /// </summary>
        public StorageProtocolType? SdProtocolType { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether disk is removable.
        /// </summary>
        public bool IsRemovable { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the device is currently powered on.
        /// </summary>
        public bool? IsDevicePowerOn
        {
            get { return StoragePowerStateHelper.TryGetDevicePowerState(DevicePath, out var isPoweredOn) ? isPoweredOn : null; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether disk is filtered.
        /// </summary>
        public bool IsFiltered { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether disk supports SMART.
        /// </summary>
        public bool SupportsSmart { get; set; }

        /// <summary>
        /// Filter reason is a user-friendly explanation of why a disk was filtered.<br/>
        /// </summary>
        public string FilterReason { get; set; }

        /// <summary>
        /// Gets or sets the SCSI information associated with the device.
        /// </summary>
        public StorageScsiInfo Scsi { get; set; }

        /// <summary>
        /// Gets or sets the smart version raw.
        /// </summary>
        public uint SmartVersionRaw { get; set; }

        /// <summary>
        /// Gets or sets the USB storage information associated with the device.
        /// </summary>
        public StorageUsbInfo Usb { get; set; }

        /// <summary>
        /// Gets or sets the storage device number.
        /// </summary>
        public uint? StorageDeviceNumber { get; set; }

        /// <summary>
        /// Gets or sets the disk size in bytes.
        /// </summary>
        public ulong? DiskSizeBytes { get; set; }

        /// <summary>
        /// Gets or sets the capacity source.
        /// </summary>
        public string CapacitySource { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the disk predicts failure.
        /// </summary>
        public bool? PredictsFailure { get; set; }

        /// <summary>
        /// Gets or sets the predict failure vendor data.
        /// </summary>
        public byte[] PredictFailureVendorData { get; set; }

        /// <summary>
        /// Gets or sets the SMART attributes.
        /// </summary>
        /// <remarks>You can use <see cref="Storage.ResourceCulture"/> to set the culture for localized attribute names.</remarks>
        public List<SmartAttributeEntry> SmartAttributes { get; set; }

        /// <summary>
        /// Gets or sets the SMART attribute profile.
        /// </summary>
        public SmartAttributeProfile SmartAttributeProfile { get; set; }

        /// <summary>
        /// Probe trace is a list of user-friendly descriptions of the probes that were performed to retrieve information about the disk.<br/>
        /// They can be useful for debugging and understanding how information about the disk was obtained.
        /// </summary>
        public List<string> ProbeTrace { get; set; }

        /// <summary>
        /// Gets or sets the cached probe plan containing the probe operations that succeeded during the initial full probe.
        /// </summary>
        public StorageProbePlan ProbePlan { get; internal set; }

        /// <summary>
        /// Gets or sets the NVMe information associated with the device.
        /// </summary>
        public StorageNvmeInfo Nvme { get; set; }

        /// <summary>
        /// Gets the normalized drive temperature in degrees Celsius when available.
        /// </summary>
        public int? Temperature
        {
            get { return SmartAttributeSummaryReader.GetTemperature(this); }
        }

        /// <summary>
        /// Gets the drive health or percentage used value when available.
        /// </summary>
        public int? Health
        {
            get { return SmartAttributeSummaryReader.GetHealth(this); }
        }

        /// <summary>
        /// Gets the summarized SMART health status when available.
        /// </summary>
        public StorageHealthStatus? HealthStatus
        {
            get { return StorageHealthStatusReader.GetHealthStatus(this); }
        }

        /// <summary>
        /// Gets the localized reason text for the summarized SMART health status when available.
        /// </summary>
        /// <remarks>This <see cref="string"/> can be multiple lines long and contains more detailed information about the health status.<br/>
        /// You can use <see cref="Storage.ResourceCulture"/> to set the culture for localization.</remarks>
        public string HealthStatusReason
        {
            get { return StorageHealthStatusReasonReader.GetHealthStatusReason(this); }
        }

        /// <summary>
        /// Gets the total host reads value when available.
        /// </summary>
        public ulong? HostReads
        {
            get { return SmartAttributeSummaryReader.GetHostReads(this); }
        }

        /// <summary>
        /// Gets the total host writes value when available.
        /// </summary>
        public ulong? HostWrites
        {
            get { return SmartAttributeSummaryReader.GetHostWrites(this); }
        }


        /// <summary>
        /// Gets the total NAND writes in gigabytes when available.
        /// </summary>
        public ulong? NandWrites
        {
            get { return SmartAttributeSummaryReader.GetNandWrites(this); }
        }

        /// <summary>
        /// Gets the total gigabytes erased when available.
        /// </summary>
        public ulong? GBytesErased
        {
            get { return SmartAttributeSummaryReader.GetGBytesErased(this); }
        }

        /// <summary>
        /// Gets the wear leveling count when available.
        /// </summary>
        public int? WearLevelingCount
        {
            get { return SmartAttributeSummaryReader.GetWearLevelingCount(this); }
        }

        /// <summary>
        /// Gets the power-on count when available.
        /// </summary>
        public ulong? PowerOnCount
        {
            get { return SmartAttributeSummaryReader.GetPowerOnCount(this); }
        }

        /// <summary>
        /// Gets the power-on hours when available.
        /// </summary>
        public ulong? PowerOnHours
        {
            get { return SmartAttributeSummaryReader.GetPowerOnHours(this); }
        }

        /// <summary>
        /// Gets the device warning temperature threshold in degrees Celsius when available.
        /// </summary>
        public int? TemperatureWarning
        {
            get { return SmartAttributeSummaryReader.GetTemperatureWarning(this); }
        }

        /// <summary>
        /// Gets the device critical temperature threshold in degrees Celsius when available.
        /// </summary>
        public int? TemperatureCritical
        {
            get { return SmartAttributeSummaryReader.GetTemperatureCritical(this); }
        }

        /// <summary>
        /// Gets or sets the CSMI information associated with the device.
        /// </summary>
        public StorageCsmiInfo Csmi { get; set; }

        /// <summary>
        /// Represents the last time when the information about the disk was updated.<br/>
        /// This can be useful for caching scenarios to determine how fresh the information is.
        /// </summary>
        public DateTime? LastUpdatedUtc { get; set; }

        /// <summary>
        /// Gets or sets the partitions associated with the device.
        /// </summary>
        public List<StoragePartitionInfo> Partitions { get; set; }

        /// <summary>
        /// Gets a value indicating whether any partition on this disk belongs to a dynamic disk layout.
        /// </summary>
        public bool IsDynamicDisk
        {
            get
            {
                return Partitions != null && Partitions.Any(p => p.IsDynamicDiskPartition);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the disk contains at least one partition recognized as belonging to another
        /// operating system.
        /// </summary>
        public bool IsOtherOperatingSystemDisk
        {
            get
            {
                return Partitions != null && Partitions.Any(p => p.IsOtherOperatingSystemPartition);
            }
        }

        /// <summary>
        /// Gets the sum of all known partition free-space values on the disk.
        /// </summary>
        /// <remarks>If <see cref="IsOtherOperatingSystemDisk"/> is true, this is not supported and will always return null.</remarks>
        public ulong? TotalPartitionFreeSpaceBytes
        {
            get
            {
                //If any partition contains another operating system, the available free space being reported is not accurate
                if (IsOtherOperatingSystemDisk)
                {
                    return null;
                }

                if (Partitions == null || Partitions.Count == 0)
                {
                    return null;
                }

                ulong total = 0;
                bool any = false;

                foreach (var partition in Partitions)
                {
                    if (partition.AvailableFreeSpaceBytes.HasValue)
                    {
                        total += partition.AvailableFreeSpaceBytes.Value;
                        any = true;
                    }
                }

                return any ? total : null;
            }
        }

        #endregion

        #region Operators

        public static bool operator ==(StorageDevice left, StorageDevice right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null)
            {
                return false;
            }

            return left.Equals(right);
        }

        public static bool operator !=(StorageDevice left, StorageDevice right)
        {
            return !(left == right);
        }

        #endregion

        #region Public

        public override bool Equals(object obj)
        {
            return Equals(obj as StorageDevice);
        }

        public override int GetHashCode()
        {
            var identity = StorageDeviceIdentityMatcher.GetStableKey(this);
            if (string.IsNullOrWhiteSpace(identity))
            {
                return 0;
            }

            return StringComparer.OrdinalIgnoreCase.GetHashCode(identity);
        }

        public bool Equals(StorageDevice other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            var thisIdentity = StorageDeviceIdentityMatcher.GetStableKey(this);
            var otherIdentity = StorageDeviceIdentityMatcher.GetStableKey(other);

            if (string.IsNullOrWhiteSpace(thisIdentity) || string.IsNullOrWhiteSpace(otherIdentity))
            {
                return false;
            }

            return string.Equals(thisIdentity, otherIdentity, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
