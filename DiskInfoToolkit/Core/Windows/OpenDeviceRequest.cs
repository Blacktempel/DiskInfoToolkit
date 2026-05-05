/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using Microsoft.Win32.SafeHandles;
using OS = BlackSharp.Core.Platform.OperatingSystem;

namespace DiskInfoToolkit.Core.Windows
{
    internal sealed class OpenDeviceRequest : IDisposable
    {
        #region Constructor

        public OpenDeviceRequest(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes)
        {
            if (!OS.IsWindows())
            {
                throw new PlatformNotSupportedException();
            }

            Path = path;
            DesiredAccess = desiredAccess;
            ShareMode = shareMode;
            CreationDisposition = creationDisposition;
            FlagsAndAttributes = flagsAndAttributes;
        }

        #endregion

        #region Fields

        private readonly ManualResetEvent _completed = new ManualResetEvent(false);

        private readonly object _syncRoot = new object();

        private bool _completedState;

        private bool _timedOut;

        #endregion

        #region Properties

        public string Path { get; }

        public uint DesiredAccess { get; }

        public uint ShareMode { get; }

        public uint CreationDisposition { get; }

        public uint FlagsAndAttributes { get; }

        public SafeFileHandle Result { get; private set; }

        public Exception Exception { get; private set; }

        #endregion

        #region Public

        public bool Wait(int timeoutMilliseconds)
        {
            return _completed.WaitOne(timeoutMilliseconds);
        }

        public bool MarkTimedOut()
        {
            lock (_syncRoot)
            {
                if (_completedState)
                {
                    return false;
                }

                _timedOut = true;
                return true;
            }
        }

        public void Complete(SafeFileHandle result, Exception exception)
        {
            lock (_syncRoot)
            {
                if (_timedOut)
                {
                    if (result != null)
                    {
                        result.Dispose();
                    }

                    return;
                }

                Result = result;
                Exception = exception;
                _completedState = true;
                _completed.Set();
            }
        }

        public void Dispose()
        {
            _completed.Dispose();
        }

        #endregion
    }
}
