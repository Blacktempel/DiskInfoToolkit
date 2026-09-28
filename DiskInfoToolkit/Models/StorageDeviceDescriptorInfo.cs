/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;

namespace DiskInfoToolkit.Models
{
    public sealed class StorageDeviceDescriptorInfo
    {
        #region Constructor

        public StorageDeviceDescriptorInfo()
        {
            VendorID = string.Empty;
            ProductID = string.Empty;
            ProductRevision = string.Empty;
            SerialNumber = string.Empty;
            BusType = StorageBusType.Unknown;
        }

        #endregion

        #region Properties

        public string VendorID { get; set; }

        public string ProductID { get; set; }

        public string ProductRevision { get; set; }

        public string SerialNumber { get; set; }

        public bool RemovableMedia { get; set; }

        public StorageBusType BusType { get; set; }

        #endregion
    }
}
