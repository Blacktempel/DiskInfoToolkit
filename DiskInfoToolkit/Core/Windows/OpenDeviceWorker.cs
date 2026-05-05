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
    internal sealed class OpenDeviceWorker
    {
        #region Constructor

        public OpenDeviceWorker(int id)
        {
            if (!OS.IsWindows())
            {
                throw new PlatformNotSupportedException();
            }

            ID = id;

            _thread = new Thread(ProcessRequests)
            {
                IsBackground = true,
                Name = $"{nameof(WindowsStorageIoControl)}.{nameof(OpenDeviceWorker)}"
            };

            _thread.Start();
        }

        #endregion

        #region Fields

        private readonly object _syncRoot = new object();

        private readonly AutoResetEvent _requestAvailable = new AutoResetEvent(false);

        private readonly Thread _thread;

        private OpenDeviceRequest _request;

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

        public bool TryEnqueue(OpenDeviceRequest request)
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

                    OpenDeviceRequest request;
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

        private static void ProcessRequest(OpenDeviceRequest request)
        {
            SafeFileHandle result = null;
            Exception exception = null;

            try
            {
                result = WindowsStorageIoControl.OpenDeviceCore(request.Path, request.DesiredAccess, request.ShareMode, request.CreationDisposition, request.FlagsAndAttributes);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            request.Complete(result, exception);
        }

        #endregion
    }
}
