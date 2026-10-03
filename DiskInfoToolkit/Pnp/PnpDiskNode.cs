/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Pnp
{
    public sealed class PnpDiskNode
    {
        #region Constructor

        public PnpDiskNode()
        {
            DevicePath = string.Empty;
            DeviceInstanceID = string.Empty;
            ParentInstanceID = string.Empty;
            DeviceDescription = string.Empty;
            FriendlyName = string.Empty;
            HardwareID = string.Empty;
            ParentHardwareID = string.Empty;
            ParentClass = string.Empty;
            ParentService = string.Empty;
            ParentDisplayName = string.Empty;
            ControllerIdentifier = string.Empty;
        }

        #endregion

        #region Properties

        public string DevicePath { get; set; }

        public string DeviceInstanceID { get; set; }

        public string ParentInstanceID { get; set; }

        public string DeviceDescription { get; set; }

        public string FriendlyName { get; set; }

        public string HardwareID { get; set; }

        public string ParentHardwareID { get; set; }

        public string ParentClass { get; set; }

        public string ParentService { get; set; }

        public string ParentDisplayName { get; set; }

        public string ControllerIdentifier { get; set; }

        /// <summary>
        /// Gets or sets whether the disk interface belongs to a USB device.
        /// </summary>
        public bool IsUsbConnected { get; set; }

        #endregion
    }
}
