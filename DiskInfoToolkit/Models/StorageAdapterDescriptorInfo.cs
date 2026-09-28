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
    public sealed class StorageAdapterDescriptorInfo
    {
        #region Constructor

        public StorageAdapterDescriptorInfo()
        {
            BusType = StorageBusType.Unknown;
        }

        #endregion

        #region Properties

        public StorageBusType BusType { get; set; }

        public uint MaximumTransferLength { get; set; }

        public uint MaximumPhysicalPages { get; set; }

        public uint AlignmentMask { get; set; }

        #endregion
    }
}
