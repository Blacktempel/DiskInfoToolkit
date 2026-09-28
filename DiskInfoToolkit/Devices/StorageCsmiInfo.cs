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
    /// Represents storage csmi info.
    /// </summary>
    public sealed class StorageCsmiInfo
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageCsmiInfo"/> class.
        /// </summary>
        public StorageCsmiInfo()
        {
            NegotiatedLinkRateName = string.Empty;
            AttachedSasAddress = string.Empty;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the phy count.
        /// </summary>
        public int? PhyCount { get; set; }

        /// <summary>
        /// Gets or sets the port identifier.
        /// </summary>
        public byte? PortIdentifier { get; set; }

        /// <summary>
        /// Gets or sets the attached phy identifier.
        /// </summary>
        public byte? AttachedPhyIdentifier { get; set; }

        /// <summary>
        /// Gets or sets the negotiated link rate.
        /// </summary>
        public byte? NegotiatedLinkRate { get; set; }

        /// <summary>
        /// Gets or sets the negotiated link rate name.
        /// </summary>
        public string NegotiatedLinkRateName { get; set; }

        /// <summary>
        /// Gets or sets the attached sas address.
        /// </summary>
        public string AttachedSasAddress { get; set; }

        /// <summary>
        /// Gets or sets the target protocol.
        /// </summary>
        public byte? TargetProtocol { get; set; }

        #endregion
    }
}
