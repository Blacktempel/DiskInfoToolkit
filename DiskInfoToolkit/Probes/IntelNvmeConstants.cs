/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using System.Text;

namespace DiskInfoToolkit.Probes
{
    public static class IntelNvmeConstants
    {
        #region Public

        public static byte[] CreateIntelSignature()
        {
            byte[] signature = new byte[8];
            byte[] source = Encoding.ASCII.GetBytes("IntelNvm");
            Array.Copy(source, signature, Math.Min(source.Length, signature.Length));
            return signature;
        }

        public static uint MakeCdw0(byte opcode, ushort commandId)
        {
            return (uint)opcode | ((uint)commandId << 16);
        }

        public static uint MakeGetLogPageCdw10(byte logPageId, int dataLength)
        {
            int dwords = dataLength / 4;
            if (dwords > 0)
            {
                dwords -= 1;
            }
            return (uint)logPageId | ((uint)dwords << 16);
        }

        #endregion
    }
}
