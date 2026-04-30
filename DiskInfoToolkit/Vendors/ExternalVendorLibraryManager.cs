/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Native;
using DiskInfoToolkit.Utilities;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Vendors
{
    public sealed class ExternalVendorLibraryManager
    {
        #region Constructor

        public ExternalVendorLibraryManager()
        {
            HighPointLibraryName = "hptintf.dll";
        }

        #endregion

        #region Fields

        private static readonly string[] HighPointFallbackLibraryNames =
        {
            "hptintf.dll",
            "hptdev.dll",
            "HptDriver.dll"
        };

        private SafeLibraryHandle _highPointHandle;

        #endregion

        #region Properties

        public string HighPointLibraryName { get; set; }

        #endregion

        #region Public

        public SafeLibraryHandle GetHighPointLibrary()
        {
            if (_highPointHandle == null || _highPointHandle.IsInvalid)
            {
                _highPointHandle = LoadFirstAvailableHighPointLibrary();
            }

            return _highPointHandle;
        }

        #endregion

        #region Private

        private SafeLibraryHandle LoadFirstAvailableHighPointLibrary()
        {
            foreach (var candidate in EnumerateHighPointLibraryCandidates())
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                SafeLibraryHandle handle = Kernel32Native.LoadLibrarySafe(candidate);

                if (handle != null && !handle.IsInvalid)
                {
                    if (IsUsableHighPointLibrary(handle))
                    {
                        return handle;
                    }

                    handle.Dispose();
                }
            }

            return new SafeLibraryHandle();
        }

        private static bool IsUsableHighPointLibrary(SafeLibraryHandle handle)
        {
            var getVersionPointer = Kernel32Native.GetProcAddress(handle.DangerousGetHandle(), HighPointBackend.HptGetVersion);

            if (getVersionPointer == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var getVersion = Marshal.GetDelegateForFunctionPointer<HptGetVersionDelegate>(getVersionPointer);
                return getVersion() != 0U;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<string> EnumerateHighPointLibraryCandidates()
        {
            yield return HighPointLibraryName;

            for (int i = 0; i < HighPointFallbackLibraryNames.Length; ++i)
            {
                if (!HighPointFallbackLibraryNames[i].Equals(HighPointLibraryName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return HighPointFallbackLibraryNames[i];
                }
            }

            foreach (string root in EnumerateHighPointInstallRoots())
            {
                yield return Path.Combine(root, HighPointLibraryName);

                for (int i = 0; i < HighPointFallbackLibraryNames.Length; ++i)
                {
                    if (!HighPointFallbackLibraryNames[i].Equals(HighPointLibraryName, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return Path.Combine(root, HighPointFallbackLibraryNames[i]);
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateHighPointInstallRoots()
        {
            yield return AppContext.BaseDirectory;
            yield return Environment.CurrentDirectory;

            string programFiles    = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles   );
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "HighPoint Technologies, Inc", "HighPoint RAID Management");
                yield return Path.Combine(programFiles, "HighPoint RAID Management");
            }

            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, "HighPoint Technologies, Inc", "HighPoint RAID Management");
                yield return Path.Combine(programFilesX86, "HighPoint RAID Management");
            }
        }

        #endregion
    }
}
