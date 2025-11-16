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

using DiskInfoToolkit.Utilities;

namespace DiskInfoToolkit.Tests.Utilities
{
    [TestClass]
    public class ByteHandlerTests
    {
        [TestMethod]
        public void ChangeByteOrder()
        {
            Assert.AreEqual(string.Empty, ByteHandler.ChangeByteOrder(null));

            Assert.AreEqual(string.Empty, ByteHandler.ChangeByteOrder(string.Empty));

            Assert.AreEqual("1", ByteHandler.ChangeByteOrder("1"));
            Assert.AreEqual("2143", ByteHandler.ChangeByteOrder("1234"));
            Assert.AreEqual("21435", ByteHandler.ChangeByteOrder("12345"));
        }
    }
}
