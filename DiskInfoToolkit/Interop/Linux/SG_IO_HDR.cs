/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Interop.Linux
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SG_IO_HDR
    {
        public int interface_id;
        public int dxfer_direction;
        public byte cmd_len;
        public byte mx_sb_len;
        public ushort iovec_count;
        public uint dxfer_len;
        public IntPtr dxferp;
        public IntPtr cmdp;
        public IntPtr sbp;
        public uint timeout;
        public uint flags;
        public int pack_id;
        public IntPtr usr_ptr;
        public byte status;
        public byte masked_status;
        public byte msg_status;
        public byte sb_len_wr;
        public ushort host_status;
        public ushort driver_status;
        public int resid;
        public uint duration;
        public uint info;
    }
}
