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
    internal struct MEGARAID_PHYSICAL_DRIVE_ADDRESS
    {
        #region Fields

        public ushort DeviceId;

        public ushort EnclDeviceId;

        public byte EnclIndex;

        public byte SlotNumber;

        public byte ScsiDevType;

        public byte ConnectPortBitmap;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ulong[] SasAddr;

        #endregion
    }
}
