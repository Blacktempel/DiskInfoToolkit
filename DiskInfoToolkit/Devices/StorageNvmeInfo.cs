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
    /// Represents storage nvme info.
    /// </summary>
    public sealed class StorageNvmeInfo
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageNvmeInfo"/> class.
        /// </summary>
        public StorageNvmeInfo()
        {
            IdentifyControllerData = Array.Empty<byte>();
            IdentifyNamespaceData = Array.Empty<byte>();
            SmartLogData = Array.Empty<byte>();
            IntelIdentifyControllerData = Array.Empty<byte>();
            IntelSmartLogData = Array.Empty<byte>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the identify controller data.
        /// </summary>
        public byte[] IdentifyControllerData { get; set; }

        /// <summary>
        /// Gets or sets the identify namespace data.
        /// </summary>
        public byte[] IdentifyNamespaceData { get; set; }

        /// <summary>
        /// Gets or sets the smart log data.
        /// </summary>
        public byte[] SmartLogData { get; set; }

        /// <summary>
        /// Gets or sets the namespace size.
        /// </summary>
        public ulong? NamespaceSize { get; set; }

        /// <summary>
        /// Gets or sets the namespace capacity.
        /// </summary>
        public ulong? NamespaceCapacity { get; set; }

        /// <summary>
        /// Gets or sets the namespace utilization.
        /// </summary>
        public ulong? NamespaceUtilization { get; set; }

        /// <summary>
        /// Gets or sets the namespace lba data size.
        /// </summary>
        public uint? NamespaceLbaDataSize { get; set; }

        /// <summary>
        /// Gets or sets the namespace formatted lba index.
        /// </summary>
        public uint? NamespaceFormattedLbaIndex { get; set; }

        /// <summary>
        /// Gets or sets the intel identify controller data.
        /// </summary>
        public byte[] IntelIdentifyControllerData { get; set; }

        /// <summary>
        /// Gets or sets the intel smart log data.
        /// </summary>
        public byte[] IntelSmartLogData { get; set; }

        #endregion
    }
}
