/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Devices
{
    /// <summary>
    /// Represents storage scsi info.
    /// </summary>
    public sealed class StorageScsiInfo
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageScsiInfo"/> class.
        /// </summary>
        public StorageScsiInfo()
        {
            InquiryVendorID = string.Empty;
            InquiryProductID = string.Empty;
            InquiryProductRevision = string.Empty;
            DeviceIdentifier = string.Empty;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the port number.
        /// </summary>
        public byte? PortNumber { get; set; }

        /// <summary>
        /// Gets or sets the path id.
        /// </summary>
        public byte? PathID { get; set; }

        /// <summary>
        /// Gets or sets the target id.
        /// </summary>
        public byte? TargetID { get; set; }

        /// <summary>
        /// Gets or sets the lun.
        /// </summary>
        public byte? Lun { get; set; }

        /// <summary>
        /// Gets or sets the last logical block address.
        /// </summary>
        public ulong? LastLogicalBlockAddress { get; set; }

        /// <summary>
        /// Gets or sets the logical block length.
        /// </summary>
        public uint? LogicalBlockLength { get; set; }

        /// <summary>
        /// Gets or sets the peripheral device type.
        /// </summary>
        public byte? PeripheralDeviceType { get; set; }

        /// <summary>
        /// Gets or sets the removable media.
        /// </summary>
        public bool? RemovableMedia { get; set; }

        /// <summary>
        /// Gets or sets the inquiry vendor id.
        /// </summary>
        public string InquiryVendorID { get; set; }

        /// <summary>
        /// Gets or sets the inquiry product id.
        /// </summary>
        public string InquiryProductID { get; set; }

        /// <summary>
        /// Gets or sets the inquiry product revision.
        /// </summary>
        public string InquiryProductRevision { get; set; }

        /// <summary>
        /// Gets or sets the device identifier.
        /// </summary>
        public string DeviceIdentifier { get; set; }

        #endregion
    }
}
