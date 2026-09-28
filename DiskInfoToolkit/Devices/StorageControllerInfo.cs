/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;

namespace DiskInfoToolkit.Devices
{
    /// <summary>
    /// Represents the storage controller info.
    /// </summary>
    public sealed class StorageControllerInfo
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageControllerInfo"/> class.
        /// </summary>
        public StorageControllerInfo()
        {
            Name = StorageTextConstants.DriveController;
            Service = string.Empty;
            Class = string.Empty;
            Kind = string.Empty;
            Identifier = string.Empty;
            Family = StorageControllerFamily.Unknown;
            VendorName = string.Empty;
            DeviceName = string.Empty;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the service.
        /// </summary>
        public string Service { get; set; }

        /// <summary>
        /// Gets or sets the class.
        /// </summary>
        public string Class { get; set; }

        /// <summary>
        /// Gets or sets the kind.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        public string Identifier { get; set; }

        /// <summary>
        /// Gets or sets the family.
        /// </summary>
        public StorageControllerFamily Family { get; set; }

        /// <summary>
        /// Gets or sets the resolved controller vendor name.
        /// </summary>
        public string VendorName { get; set; }

        /// <summary>
        /// Gets or sets the resolved controller device or product name.
        /// </summary>
        public string DeviceName { get; set; }

        /// <summary>
        /// Gets or sets the hardware id.
        /// </summary>
        public string HardwareID { get; set; }

        /// <summary>
        /// Gets or sets the vendor id.
        /// </summary>
        public ushort? VendorID { get; set; }

        /// <summary>
        /// Gets or sets the device id.
        /// </summary>
        public ushort? DeviceID { get; set; }

        /// <summary>
        /// Gets or sets the revision.
        /// </summary>
        public ushort? Revision { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether usb style hardware id.
        /// </summary>
        public bool IsUsbStyleHardwareID { get; set; }

        #endregion
    }
}
