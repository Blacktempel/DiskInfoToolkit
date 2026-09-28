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
    /// Represents storage USB info.
    /// </summary>
    public sealed class StorageUsbInfo
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageUsbInfo"/> class.
        /// </summary>
        public StorageUsbInfo()
        {
            BridgeFamily = string.Empty;
            MassStorageProtocolName = string.Empty;
            NvmeSetupMode = string.Empty;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the bridge family.
        /// </summary>
        public string BridgeFamily { get; set; }

        /// <summary>
        /// Gets or sets the mass storage protocol name.
        /// </summary>
        public string MassStorageProtocolName { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether mass storage like.
        /// </summary>
        public bool IsMassStorageLike { get; set; }

        /// <summary>
        /// Gets or sets the nvme setup mode.
        /// </summary>
        public string NvmeSetupMode { get; set; }

        #endregion
    }
}
