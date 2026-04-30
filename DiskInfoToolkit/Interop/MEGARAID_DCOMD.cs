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
    internal struct MEGARAID_DCOMD
    {
        #region Fields

        public byte Cmd;

        public byte Reserved0;

        public byte CmdStatus;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] Reserved1;

        public byte SenseInfoLength;

        public uint Context;

        public uint Padding0;

        public ushort Flags;

        public ushort TimeOutValue;

        public uint DataTransferLength;

        public uint Opcode;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MegaRaidMiniportConstants.MailboxLength)]
        public byte[] Mbox;

        #endregion

        #region Public

        public static MEGARAID_DCOMD Create()
        {
            return new MEGARAID_DCOMD
            {
                Reserved1 = new byte[4],
                Mbox = new byte[MegaRaidMiniportConstants.MailboxLength]
            };
        }

        #endregion
    }
}
