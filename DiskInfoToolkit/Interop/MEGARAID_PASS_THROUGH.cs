/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Interop
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct MEGARAID_PASS_THROUGH
    {
        #region Fields

        public byte Cmd;

        public byte SenseLength;

        public byte CmdStatus;

        public byte ScsiStatus;

        public byte TargetId;

        public byte Lun;

        public byte CdbLength;

        public byte SenseInfoLength;

        public uint Context;

        public uint Padding0;

        public ushort Flags;

        public ushort TimeOutValue;

        public uint DataTransferLength;

        public uint SenseInfoOffsetLo;

        public uint SenseInfoOffsetHi;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Cdb;

        #endregion

        #region Public

        public static MEGARAID_PASS_THROUGH Create()
        {
            return new MEGARAID_PASS_THROUGH
            {
                Cdb = new byte[16]
            };
        }

        #endregion
    }
}
