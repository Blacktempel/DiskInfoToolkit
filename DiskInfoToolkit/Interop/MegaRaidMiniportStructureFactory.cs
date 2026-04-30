/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.Interop
{
    internal static class MegaRaidMiniportStructureFactory
    {
        #region Public

        public static SRB_IO_CONTROL CreateSrbIoControl(string signature, uint length)
        {
            var srb = new SRB_IO_CONTROL();
            srb.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            srb.Signature = new byte[8];
            srb.Timeout = 0;
            srb.ControlCode = 0;
            srb.ReturnCode = 0;
            srb.Length = length;

            if (!string.IsNullOrEmpty(signature))
            {
                byte[] signatureBytes = Encoding.ASCII.GetBytes(signature);
                Buffer.BlockCopy(signatureBytes, 0, srb.Signature, 0, Math.Min(signatureBytes.Length, srb.Signature.Length));
            }

            return srb;
        }

        #endregion
    }
}
