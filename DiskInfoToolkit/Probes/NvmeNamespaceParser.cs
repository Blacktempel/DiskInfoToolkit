/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;

namespace DiskInfoToolkit.Probes
{
    public static class NvmeNamespaceParser
    {
        #region Public

        public static void ApplyNamespaceData(StorageDevice device, byte[] data)
        {
            if (device == null || data == null || data.Length < 128)
            {
                return;
            }

            device.Nvme.NamespaceSize        = ReadUInt64(data,  0);
            device.Nvme.NamespaceCapacity    = ReadUInt64(data,  8);
            device.Nvme.NamespaceUtilization = ReadUInt64(data, 16);

            byte flbas = data[26];
            uint formattedIndex = (uint)(flbas & 0x0F);

            device.Nvme.NamespaceFormattedLbaIndex = formattedIndex;

            int lbafOffset = 128 + (int)(formattedIndex * 4);
            if (lbafOffset + 4 <= data.Length)
            {
                uint lbads = (uint)data[lbafOffset];
                if (lbads <= 31)
                {
                    device.Nvme.NamespaceLbaDataSize = 1u << (int)lbads;
                }
            }
        }

        #endregion

        #region Private

        private static ulong ReadUInt64(byte[] data, int offset)
        {
            if (offset < 0 || offset + 8 > data.Length)
            {
                return 0;
            }

            return BitConverter.ToUInt64(data, offset);
        }

        #endregion
    }
}
