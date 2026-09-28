/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Core;

namespace DiskInfoToolkit.Tests.Core
{
    /// <summary>
    /// Verifies complete volume extent responses with synthetic IOCTL data only.
    /// </summary>
    [TestClass]
    public sealed class WindowsVolumeDiskExtentReaderTest
    {
        #region Public

        /// <summary>
        /// Retries a response larger than the former four-kilobyte buffer and parses every extent.
        /// </summary>
        [TestMethod]
        public void ReadsExtentTableLargerThanFourKilobytes()
        {
            const int ExtentCount = 180;
            const int ExtentOffset = 8;
            const int ExtentSize = 24;

            int requestCount = 0;

            bool success = WindowsVolumeDiskExtentReader.TryReadResponse(buffer =>
            {
                ++requestCount;

                BitConverter.GetBytes((uint)ExtentCount).CopyTo(buffer, 0);

                if (requestCount == 1)
                {
                    Assert.AreEqual(4096, buffer.Length);
                    return (false, 32);
                }

                Assert.AreEqual(ExtentOffset + (ExtentCount * ExtentSize), buffer.Length);

                for (int i = 0; i < ExtentCount; ++i)
                {
                    int offset = ExtentOffset + (i * ExtentSize);

                    BitConverter.GetBytes((uint)(i == ExtentCount - 1 ? 9 : 7)).CopyTo(buffer, offset);
                    BitConverter.GetBytes((long)i * 4096).CopyTo(buffer, offset + 8);
                    BitConverter.GetBytes(4096L).CopyTo(buffer, offset + 16);
                }

                return (true, buffer.Length);
            }, out var extents);

            Assert.IsTrue(success);
            Assert.AreEqual(2, requestCount);
            Assert.AreEqual(ExtentCount, extents.Count);
            Assert.AreEqual((uint)7, extents[0].DiskNumber);
            Assert.AreEqual(0L, extents[0].StartingOffset);
            Assert.AreEqual((uint)9, extents[ExtentCount - 1].DiskNumber);
            Assert.AreEqual((long)(ExtentCount - 1) * 4096, extents[ExtentCount - 1].StartingOffset);
            Assert.AreEqual(4096L, extents[ExtentCount - 1].ExtentLength);
        }

        /// <summary>
        /// Rejects a response claiming more extents than its returned bytes contain.
        /// </summary>
        [TestMethod]
        public void RejectsTruncatedExtentTable()
        {
            bool success = WindowsVolumeDiskExtentReader.TryReadResponse(buffer =>
            {
                BitConverter.GetBytes(2U).CopyTo(buffer, 0);

                return (true, 32);
            }, out var extents);

            Assert.IsFalse(success);
            Assert.IsNull(extents);
        }

        #endregion
    }
}
