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
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct LINUX_NVME_ADMIN_CMD
    {
        public byte opcode;
        public byte flags;
        public ushort rsvd1;
        public uint nsid;
        public uint cdw2;
        public uint cdw3;
        public ulong metadata;
        public ulong addr;
        public uint metadata_len;
        public uint data_len;
        public uint cdw10;
        public uint cdw11;
        public uint cdw12;
        public uint cdw13;
        public uint cdw14;
        public uint cdw15;
        public uint timeout_ms;
        public uint result;
    }
}
