/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Constants
{
    internal static class ControllerKindNames
    {
        #region Fields

        public const string NvmePci = "NVMe PCI";

        public const string Raid = "RAID";

        public const string NvmeUsb = "NVMe USB";

        public const string Csmi = "CSMI";

        public const string ScsiMiniport = "SCSI Miniport";

        public const string SatPort = "SAT Port";

        public const string RaidPort = "RAID Port";

        public const string Vroc = "VROC";

        public const string IntelRst = "Intel RST";

        public const string RocketRaid = "RocketRAID";

        public const string MegaRaid = "MegaRAID";

        public const string Nvme = "NVMe";

        public const string UsbUasp = "USB UASP";

        public const string UsbStorage = "USB Storage";

        public const string SdMmc = "SD/MMC";

        #endregion
    }
}
