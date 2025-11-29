/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Florian K.
 *
 * Code inspiration, improvements and fixes are from, but not limited to, following projects:
 * CrystalDiskInfo
 */

using DiskInfoToolkit.Enums.Interop;
using DiskInfoToolkit.Interop.Enums;
using OS = BlackSharp.Core.Platform.OperatingSystem;

namespace DiskInfoToolkit.HardDrive
{
    /// <summary>
    /// ATA information.
    /// </summary>
    public sealed class ATAInfo
    {
        #region Constructor

        static ATAInfo()
        {
            if (OS.GetOSVersion(out var major, out var minor))
            {
                //Minimum Windows XP
                if (major == 5
                 && minor >= 1)
                {
                    AtaPassThrough = true;
                    AtaPassThroughSmart = true;
                }

                if (major > 5)
                {
                    AtaPassThrough = true;
                    AtaPassThroughSmart = true;
                }
            }
        }

        internal ATAInfo()
        {
        }

        #endregion

        #region Properties

        public static bool AtaPassThrough { get; }
        public static bool AtaPassThroughSmart { get; }

        /// <summary>
        /// Transfer mode of device.
        /// </summary>
        public TransferMode TransferMode { get; internal set; }

        public ushort Cylinders { get; internal set; }
        public ushort Heads { get; internal set; }
        public ushort Sectors { get; internal set; }
        public ushort LogicalSectorSize { get; internal set; }
        public ushort PhysicalSectorSize { get; internal set; }
        public uint DiskSizeChs { get; internal set; }
        public ulong NumberOfSectors { get; internal set; }

        public uint Sector28 { get; internal set; }
        public uint DiskSizeLba28 { get; internal set; }
        public ulong Sector48 { get; internal set; }
        public ulong DiskSizeLba48 { get; internal set; }

        internal VendorIDs VendorID { get; set; }
        internal FlagLife FlagLife { get; set; }

        #endregion
    }
}
