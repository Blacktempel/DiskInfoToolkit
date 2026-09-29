/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using BlackSharp.Core.Interop.Windows.Native;
using BlackSharp.Core.Interop.Windows.Structures;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Core.Windows
{
    /// <summary>
    /// Owns the native memory and device handle reference for one asynchronous Windows device control request.
    /// </summary>
    internal sealed unsafe class OverlappedIoControlRequest : IDisposable
    {
        #region Constructor

        /// <summary>
        /// Allocates native buffers and retains the device handle until the request completes.
        /// </summary>
        /// <param name="handle">The overlapped device handle used for the request.</param>
        /// <param name="input">The request bytes to copy into native memory.</param>
        /// <param name="output">The response buffer whose initial contents must be preserved.</param>
        public OverlappedIoControlRequest(SafeFileHandle handle, byte[] input, byte[] output)
        {
            _handle = handle;

            try
            {
                handle.DangerousAddRef(ref _handleReferenceAdded);

                _device = handle.DangerousGetHandle();

                _overlapped = Marshal.AllocHGlobal(sizeof(NativeOverlappedData));
                NativeOverlappedData* nativeOverlapped = (NativeOverlappedData*)_overlapped;

                *nativeOverlapped = default;

                nativeOverlapped->EventHandle = _completion.SafeWaitHandle.DangerousGetHandle();

                _bytesReturned = Marshal.AllocHGlobal(sizeof(uint));
                Marshal.WriteInt32(_bytesReturned, 0);

                if (input != null && input.Length > 0)
                {
                    _input = Marshal.AllocHGlobal(input.Length);

                    Marshal.Copy(input, 0, _input, input.Length);
                }

                if (output != null && output.Length > 0)
                {
                    _output = Marshal.AllocHGlobal(output.Length);

                    Marshal.Copy(output, 0, _output, output.Length);
                }
            }
            catch
            {
                Dispose();

                throw;
            }
        }

        #endregion

        #region Fields

        private const int ErrorIoPending = 997;

        private const int ErrorTimeout = 1460;

        private const int MaximumConcurrentRequests = 64;

        private static int _activeRequests;

        private readonly SafeFileHandle _handle;

        private readonly EventWaitHandle _completion = new EventWaitHandle(false, EventResetMode.ManualReset);

        private readonly object _waitSyncRoot = new object();

        private IntPtr _device;

        private IntPtr _overlapped;

        private IntPtr _bytesReturned;

        private IntPtr _input;

        private IntPtr _output;

        private RegisteredWaitHandle _registeredWait;

        private bool _handleReferenceAdded;

        private int _disposed;

        #endregion

        #region Public

        /// <summary>
        /// Reserves one of the limited request slots so stalled drivers cannot retain unlimited native resources.
        /// </summary>
        /// <remarks>A successful reservation is released when the corresponding request is disposed.</remarks>
        /// <returns>Whether a slot was reserved.</returns>
        public static bool TryReserve()
        {
            while (true)
            {
                int active = Volatile.Read(ref _activeRequests);

                if (active >= MaximumConcurrentRequests)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _activeRequests, active + 1, active) == active)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Runs the device control request and asks Windows to cancel it when the wait expires.
        /// </summary>
        /// <param name="ioControlCode">The device control code to send.</param>
        /// <param name="input">The managed request buffer.</param>
        /// <param name="output">The managed response buffer, copied only after the operation completes.</param>
        /// <param name="timeoutMilliseconds">The wait in milliseconds; zero or less waits indefinitely.</param>
        /// <param name="bytesReturned">The number of response bytes reported by Windows.</param>
        /// <param name="error">The Win32 error code, including ERROR_TIMEOUT when cancellation was requested.</param>
        /// <returns>Whether the operation completed successfully before the timeout.</returns>
        public bool Execute(uint ioControlCode, byte[] input, byte[] output, int timeoutMilliseconds, out int bytesReturned, out int error)
        {
            bytesReturned = 0;
            error = 0;

            bool releaseNow = true;

            try
            {
                bool started = Kernel32.DeviceIoControl(
                    _handle,
                    ioControlCode,
                    _input,
                    (uint)(input != null ? input.Length : 0),
                    _output,
                    (uint)(output != null ? output.Length : 0),
                    out *((uint*)_bytesReturned),
                    ref *((NativeOverlappedData*)_overlapped));

                if (!started)
                {
                    error = Marshal.GetLastWin32Error();

                    if (error != ErrorIoPending)
                    {
                        bytesReturned = ToManagedLength(*((uint*)_bytesReturned));
                        CopyOutput(output);

                        return false;
                    }

                    int waitMilliseconds = timeoutMilliseconds > 0 ? timeoutMilliseconds : Timeout.Infinite;

                    if (!_completion.WaitOne(waitMilliseconds))
                    {
                        // Cancellation is only a request. Keep every native buffer and the handle
                        // alive until the driver actually completes the I/O operation.
                        releaseNow = false;

                        Kernel32.CancelIoEx(_device, _overlapped);

                        RegisterCompletionCleanup();

                        error = ErrorTimeout;
                        return false;
                    }
                }

                bool completed = Kernel32.GetOverlappedResult(_device, _overlapped, out uint nativeBytesReturned, false);

                bytesReturned = ToManagedLength(nativeBytesReturned);

                if (!completed)
                {
                    error = Marshal.GetLastWin32Error();

                    if (bytesReturned == 0)
                    {
                        bytesReturned = ToManagedLength(*((uint*)_bytesReturned));
                    }

                    CopyOutput(output);

                    return false;
                }

                CopyOutput(output);

                return true;
            }
            finally
            {
                if (releaseNow)
                {
                    Dispose();
                }
            }
        }

        /// <summary>
        /// Releases the native buffers, completion event, and retained handle reference.
        /// </summary>
        /// <remarks>Call only after the I/O operation completes. Timed-out requests arrange deferred cleanup.</remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_input != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_input);
            }

            if (_output != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_output);
            }

            if (_overlapped != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_overlapped);
            }

            if (_bytesReturned != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bytesReturned);
            }

            _completion.Dispose();

            if (_handleReferenceAdded)
            {
                _handle.DangerousRelease();
            }

            Interlocked.Decrement(ref _activeRequests);
        }

        #endregion

        #region Private

        /// <summary>
        /// Converts a native byte count to the managed response length.
        /// </summary>
        /// <param name="length">The byte count returned by Windows.</param>
        /// <returns>The byte count clamped to the managed integer range.</returns>
        private static int ToManagedLength(uint length)
        {
            return length > int.MaxValue ? int.MaxValue : (int)length;
        }

        /// <summary>
        /// Copies the completed native response into the caller's managed buffer.
        /// </summary>
        /// <param name="output">The managed response buffer.</param>
        private void CopyOutput(byte[] output)
        {
            if (output != null && _output != IntPtr.Zero)
            {
                Marshal.Copy(_output, output, 0, output.Length);
            }
        }

        /// <summary>
        /// Schedules resource cleanup for a timed-out request after the driver signals completion.
        /// </summary>
        private void RegisterCompletionCleanup()
        {
            try
            {
                lock (_waitSyncRoot)
                {
                    _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                        _completion,
                        (state, _) => ((OverlappedIoControlRequest)state).CompleteCancellation(),
                        this,
                        Timeout.Infinite,
                        true);
                }
            }
            catch
            {
                // A dedicated background thread is the fallback when a registered wait
                // cannot be created. Releasing the buffers before completion is unsafe.
                var thread = new Thread(() =>
                {
                    _completion.WaitOne();
                    CompleteCancellation();
                })
                {
                    IsBackground = true,
                    Name = nameof(OverlappedIoControlRequest)
                };

                thread.Start();
            }
        }

        /// <summary>
        /// Finishes cleanup after a timed-out request has completed or been canceled.
        /// </summary>
        private void CompleteCancellation()
        {
            lock (_waitSyncRoot)
            {
                _registeredWait?.Unregister(null);
                _registeredWait = null;
            }

            Kernel32.GetOverlappedResult(_device, _overlapped, out _, false);

            Dispose();
        }

        #endregion
    }
}
