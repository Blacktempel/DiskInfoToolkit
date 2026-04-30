/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Interop
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct MEGARAID_PASS_THROUGH_IOCTL
    {
        #region Fields

        public SRB_IO_CONTROL SrbIoCtrl;

        public MEGARAID_PASS_THROUGH Mpt;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MegaRaidMiniportConstants.SenseBufferLengthForPassThrough)]
        public byte[] SenseBuf;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MegaRaidMiniportConstants.DataBufferLength)]
        public byte[] DataBuf;

        #endregion

        #region Public

        public static MEGARAID_PASS_THROUGH_IOCTL Create(string signature, uint payloadDataLength)
        {
            return new MEGARAID_PASS_THROUGH_IOCTL
            {
                SrbIoCtrl = MegaRaidMiniportStructureFactory.CreateSrbIoControl(signature, (uint)(Marshal.SizeOf<MEGARAID_PASS_THROUGH_IOCTL>() - MegaRaidMiniportConstants.DataBufferLength + payloadDataLength)),
                Mpt = MEGARAID_PASS_THROUGH.Create(),
                SenseBuf = new byte[MegaRaidMiniportConstants.SenseBufferLengthForPassThrough],
                DataBuf = new byte[MegaRaidMiniportConstants.DataBufferLength]
            };
        }

        #endregion
    }
}
