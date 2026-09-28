/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using OS = BlackSharp.Core.Platform.OperatingSystem;

namespace DiskInfoToolkit.Core.Windows
{
    internal sealed class IoControlWorker
    {
        #region Constructor

        public IoControlWorker(int id)
        {
            if (!OS.IsWindows())
            {
                throw new PlatformNotSupportedException();
            }

            ID = id;

            _thread = new Thread(ProcessRequests)
            {
                IsBackground = true,
                Name = $"{nameof(WindowsStorageIoControl)}.{nameof(IoControlWorker)}"
            };

            _thread.Start();
        }

        #endregion

        #region Fields

        private readonly object _syncRoot = new object();

        private readonly AutoResetEvent _requestAvailable = new AutoResetEvent(false);

        private readonly Thread _thread;

        private IoControlRequest _request;

        private bool _abandoned;

        #endregion

        #region Properties

        public int ID { get; }

        public bool CanAcceptWork
        {
            get
            {
                lock (_syncRoot)
                {
                    return !_abandoned;
                }
            }
        }

        #endregion

        #region Public

        public bool TryEnqueue(IoControlRequest request)
        {
            lock (_syncRoot)
            {
                if (_abandoned || _request != null)
                {
                    return false;
                }

                _request = request;
                _requestAvailable.Set();

                return true;
            }
        }

        public void MarkAbandoned()
        {
            lock (_syncRoot)
            {
                _abandoned = true;
            }
        }

        #endregion

        #region Private

        private void ProcessRequests()
        {
            try
            {
                while (true)
                {
                    _requestAvailable.WaitOne();

                    IoControlRequest request;
                    lock (_syncRoot)
                    {
                        request = _request;
                        _request = null;
                    }

                    if (request != null)
                    {
                        ProcessRequest(request);
                    }

                    lock (_syncRoot)
                    {
                        if (_abandoned)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                _requestAvailable.Dispose();
            }
        }

        private static void ProcessRequest(IoControlRequest request)
        {
            bool success = false;
            int lastError = 0;
            int bytesReturned = 0;
            Exception exception = null;

            try
            {
                success = WindowsStorageIoControl.SendRawIoControlCore(
                    request.Handle,
                    request.IoControlCode,
                    request.InBuffer,
                    request.OutBuffer,
                    out bytesReturned,
                    out lastError);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            request.Complete(success, lastError, bytesReturned, exception);
        }

        #endregion
    }
}
