/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using BlackSharp.Core.Interop.Windows.Native;
using Microsoft.Win32.SafeHandles;

namespace DiskInfoToolkit.Utilities
{
    public sealed class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        #region Constructor

        public SafeLibraryHandle() : base(true)
        {
        }

        public SafeLibraryHandle(IntPtr existingHandle, bool ownsHandle) : base(ownsHandle)
        {
            SetHandle(existingHandle);
        }

        #endregion

        #region Protected

        protected override bool ReleaseHandle()
        {
            return Kernel32.FreeLibrary(handle);
        }

        #endregion
    }
}
