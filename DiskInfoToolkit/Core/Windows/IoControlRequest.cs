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
    internal sealed class IoControlRequest : IDisposable
    {
        #region Constructor

        public IoControlRequest(SafeFileHandle handle, uint ioControlCode, byte[] inBuffer, byte[] outBuffer)
        {
            if (!OS.IsWindows())
            {
                throw new PlatformNotSupportedException();
            }

            Handle = handle;
            IoControlCode = ioControlCode;
            InBuffer = inBuffer;
            OutBuffer = outBuffer;
        }

        #endregion

        #region Fields

        private readonly ManualResetEvent _completed = new ManualResetEvent(false);

        private readonly object _syncRoot = new object();

        private bool _completedState;

        private bool _timedOut;

        private bool _disposed;

        #endregion

        #region Properties

        public SafeFileHandle Handle { get; }

        public uint IoControlCode { get; }

        public byte[] InBuffer { get; }

        public byte[] OutBuffer { get; }

        public bool Succeeded { get; private set; }

        public int LastError { get; private set; }

        public int BytesReturned { get; private set; }

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

        public void Complete(bool succeeded, int lastError, int bytesReturned, Exception exception)
        {
            bool shouldDispose = false;

            lock (_syncRoot)
            {
                if (_timedOut)
                {
                    return;
                }

                Succeeded = succeeded;
                LastError = lastError;
                BytesReturned = bytesReturned;
                Exception = exception;
                _completedState = true;

                if (_disposed)
                {
                    shouldDispose = true;
                }
                else
                {
                    _completed.Set();
                }
            }

            if (shouldDispose)
            {
                _completed.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (_completedState)
                {
                    _completed.Dispose();
                }
            }
        }

        #endregion
    }
}
