/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Smart;

namespace DiskInfoToolkit.Tests.Smart
{
    [TestClass]
    public class SmartAttributeSummaryBytesTest
    {
        #region Public

        [TestMethod]
        public void NvmeDataUnitsAreReportedInBytes()
        {
            var device = CreateDevice(SmartAttributeProfile.NVMe,
                CreateAttribute(0xE5, 1),
                CreateAttribute(0xE6, 3));

            Assert.AreEqual((ulong)512000, device.HostReads);
            Assert.AreEqual((ulong)1536000, device.HostWrites);
            Assert.IsNull(device.NandWrites);
        }

        [TestMethod]
        public void SectorAndMibUnitsKeepSubGibibyteValues()
        {
            var samsung = CreateDevice(SmartAttributeProfile.Samsung,
                CreateAttribute(0xF2, 1),
                CreateAttribute(0xF1, 3));

            var intel = CreateDevice(SmartAttributeProfile.Intel,
                CreateAttribute(0xF2, 1),
                CreateAttribute(0xE1, 1),
                CreateAttribute(0xF9, 2));

            Assert.AreEqual((ulong)512, samsung.HostReads);
            Assert.AreEqual((ulong)1536, samsung.HostWrites);

            Assert.AreEqual((ulong)33554432, intel.HostReads);
            Assert.AreEqual((ulong)33554432, intel.HostWrites);
            Assert.AreEqual((ulong)2000000000, intel.NandWrites);
        }

        [TestMethod]
        public void VendorGigabytesAndErasedBytesUseDecimalScale()
        {
            var wdc = CreateDevice(SmartAttributeProfile.WDC,
                CreateAttribute(0xF2, 2),
                CreateAttribute(0xF1, 3));

            var sandForce = CreateDevice(SmartAttributeProfile.SandForce,
                CreateAttribute(0x64, 4));

            Assert.AreEqual((ulong)2000000000, wdc.HostReads);
            Assert.AreEqual((ulong)3000000000, wdc.HostWrites);

            Assert.AreEqual((ulong)4000000000, sandForce.BytesErased);
        }

        [TestMethod]
        public void ConfiguredMiBUnitsUseTheirExactByteScale()
        {
            var corsair = CreateDevice(SmartAttributeProfile.Corsair, CreateAttribute(0xF2, 1));
            corsair.ProductName = "Voyager GTX";

            var wdc = CreateDevice(SmartAttributeProfile.WDC, CreateAttribute(0xF1, 1));
            wdc.ProductName = "SA530";

            Assert.AreEqual((ulong)1048576, corsair.HostReads);
            Assert.AreEqual((ulong)16777216, wdc.HostWrites);
        }

        [TestMethod]
        public void NandVendorUnitsAreReportedInBytes()
        {
            var ocz        = CreateDevice(SmartAttributeProfile.OczVector , CreateAttribute(0xF9, 1));
            var micron     = CreateDevice(SmartAttributeProfile.Micron    , CreateAttribute(0xF5, 1));
            var micronMu03 = CreateDevice(SmartAttributeProfile.MicronMU03, CreateAttribute(0xF5, 1));

            Assert.AreEqual((ulong)16384, ocz.NandWrites);
            Assert.AreEqual((ulong)8192, micron.NandWrites);
            Assert.AreEqual((ulong)33554432, micronMu03.NandWrites);
        }

        [TestMethod]
        public void ValuesThatCannotFitInUlongReturnNull()
        {
            var nvme    = CreateDevice(SmartAttributeProfile.NVMe   , CreateAttribute(0xE5, ulong.MaxValue));
            var samsung = CreateDevice(SmartAttributeProfile.Samsung, CreateAttribute(0xF1, ulong.MaxValue));
            var wdc     = CreateDevice(SmartAttributeProfile.WDC    , CreateAttribute(0xF2, ulong.MaxValue));

            Assert.IsNull(nvme.HostReads);
            Assert.IsNull(samsung.HostWrites);
            Assert.IsNull(wdc.HostReads);
        }

        #endregion

        #region Private

        private static StorageDevice CreateDevice(SmartAttributeProfile profile, params SmartAttributeEntry[] attributes)
        {
            return new StorageDevice
            {
                SmartAttributeProfile = profile,
                SmartAttributes = new List<SmartAttributeEntry>(attributes)
            };
        }

        private static SmartAttributeEntry CreateAttribute(byte id, ulong rawValue)
        {
            return new SmartAttributeEntry
            {
                ID = id,
                RawValue = rawValue
            };
        }

        #endregion
    }
}
