/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Utilities;

namespace DiskInfoToolkit.Tests.Utilities
{
    /// <summary>
    /// Verifies optical device detection from NT device names only.
    /// </summary>
    [TestClass]
    public sealed class OpticalDeviceHelperTest
    {
        #region Public

        /// <summary>
        /// Recognizes CD-ROM class device names regardless of case and index.
        /// </summary>
        [TestMethod]
        [DataRow(@"\Device\CdRom0")]
        [DataRow(@"\Device\CdRom12")]
        [DataRow(@"\device\cdrom1")]
        public void DetectsOpticalDeviceNames(string name)
        {
            Assert.IsTrue(OpticalDeviceHelper.IsOpticalDeviceName(name));
        }

        /// <summary>
        /// Does not treat disk, Storage Spaces or empty names as optical devices.
        /// </summary>
        [TestMethod]
        [DataRow(@"\Device\HarddiskVolume3")]
        [DataRow(@"\Device\Volume{042dcf0d-1a8b-3c45-87b7-1a3469d2a54b}")]
        [DataRow(@"\Device\Floppy0")]
        [DataRow(@"\?\Volume{042dcf0d-1a8b-3c45-87b7-1a3469d2a54b}\")]
        [DataRow("")]
        [DataRow(null)]
        public void IgnoresNonOpticalDeviceNames(string name)
        {
            Assert.IsFalse(OpticalDeviceHelper.IsOpticalDeviceName(name));
        }

        /// <summary>
        /// Unassigned or invalid DOS device names are not reported as optical.
        /// </summary>
        [TestMethod]
        [DataRow("")]
        [DataRow(null)]
        [DataRow("NotARealDosDevice_DiskInfoToolkit")]
        public void IgnoresUnknownDosDevices(string name)
        {
            Assert.IsFalse(OpticalDeviceHelper.IsOpticalDosDevice(name));
        }

        #endregion
    }
}
