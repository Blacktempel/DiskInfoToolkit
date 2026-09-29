/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using BlackSharp.Core.Interop.Windows.Native;
using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core.Windows;
using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Models;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.Core
{
    /// <summary>
    /// Opens Windows storage devices and sends device control requests with bounded waits.
    /// </summary>
    public sealed class WindowsStorageIoControl : IStorageIoControl
    {
        #region Fields

        private const int DefaultOpenDeviceTimeoutMilliseconds = 3000;

        private const int DefaultOpenDeviceTimeoutCooldownMilliseconds = 300000;

        private const int DefaultIoControlTimeoutMilliseconds = 3000;

        private const int ErrorTimeout = 1460;

        private static readonly ConcurrentDictionary<string, DateTime> OpenDeviceTimeouts = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConditionalWeakTable<SafeFileHandle, DeviceHandleState> DeviceHandles = new();

        private static readonly object OpenDeviceWorkerSyncRoot = new object();

        private static OpenDeviceWorker _openDeviceWorker;

        private static int _openDeviceWorkerSequence;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the timeout for opening a Windows device handle. A value of zero or less disables the timeout.
        /// </summary>
        public static int OpenDeviceTimeoutMilliseconds { get; set; } = DefaultOpenDeviceTimeoutMilliseconds;

        /// <summary>
        /// Gets or sets the timeout for a Windows device control request. A value of zero or less waits indefinitely.
        /// </summary>
        public static int IoControlTimeoutMilliseconds { get; set; } = DefaultIoControlTimeoutMilliseconds;

        /// <summary>
        /// Gets or sets how long a device path is skipped after an open or device control timeout. A value of zero or less disables the cooldown.
        /// </summary>
        public static int OpenDeviceTimeoutCooldownMilliseconds { get; set; } = DefaultOpenDeviceTimeoutCooldownMilliseconds;

        /// <summary>
        /// Gets the control code of the most recent request.
        /// </summary>
        public uint LastIoControlCode { get; private set; }

        /// <summary>
        /// Gets the Win32 error code of the most recent failed request, including ERROR_TIMEOUT (1460), or zero after success.
        /// </summary>
        public int LastIoControlError { get; private set; }

        /// <summary>
        /// Gets whether the most recent device control request succeeded.
        /// </summary>
        public bool LastIoControlSucceeded { get; private set; }

        /// <summary>
        /// Gets whether the most recent failure indicates that further device requests should be skipped.
        /// </summary>
        public bool LastIoControlWasSevereDeviceError
        {
            get { return !LastIoControlSucceeded && IsSevereDeviceIoError(LastIoControlError); }
        }

        #endregion

        #region Public

        /// <inheritdoc />
        /// <remarks>Windows handles opened through this method use overlapped I/O.</remarks>
        public SafeFileHandle OpenDevice(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes)
        {
            return OpenDeviceInternal(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes | IoFlags.Overlapped, true);
        }

        /// <inheritdoc />
        /// <remarks>A timed-out request returns ERROR_TIMEOUT and requests cancellation. Native resources remain valid until the driver completes the request.</remarks>
        public bool SendRawIoControl(SafeFileHandle handle, uint ioControlCode, byte[] inBuffer, byte[] outBuffer, out int bytesReturned)
        {
            bool success;
            int lastError;

            if (handle != null && DeviceHandles.TryGetValue(handle, out DeviceHandleState state))
            {
                if (state.TimedOut || !OverlappedIoControlRequest.TryReserve())
                {
                    bytesReturned = 0;
                    success = false;

                    lastError = ErrorTimeout;
                }
                else
                {
                    var request = new OverlappedIoControlRequest(handle, inBuffer, outBuffer);

                    success = request.Execute(ioControlCode, inBuffer, outBuffer, IoControlTimeoutMilliseconds, out bytesReturned, out lastError);

                    if (lastError == ErrorTimeout)
                    {
                        state.TimedOut = true;

                        RememberOpenDeviceTimeout(state.Path);
                    }
                }
            }
            else
            {
                success = Kernel32.DeviceIoControl(
                    handle,
                    ioControlCode,
                    inBuffer,
                    inBuffer != null ? inBuffer.Length : 0,
                    outBuffer,
                    outBuffer != null ? outBuffer.Length : 0,
                    out bytesReturned,
                    IntPtr.Zero);

                lastError = success ? 0 : Marshal.GetLastWin32Error();
            }

            LastIoControlCode      = ioControlCode;
            LastIoControlSucceeded = success;
            LastIoControlError     = success ? 0 : lastError;

            return success;
        }

        /// <inheritdoc />
        public bool TryGetStorageDeviceDescriptor(SafeFileHandle handle, out StorageDeviceDescriptorInfo descriptor)
        {
            descriptor = new StorageDeviceDescriptorInfo();
            STORAGE_PROPERTY_QUERY query = STORAGE_PROPERTY_QUERY.CreateDefault();
            query.PropertyID = 0;
            query.QueryType = 0;
            byte[] inBuffer = StructureHelper.GetBytes(query);

            byte[] outBuffer = new byte[1024];
            int bytesReturned;
            if (!SendRawIoControl(handle, IoControlCodes.IOCTL_STORAGE_QUERY_PROPERTY, inBuffer, outBuffer, out bytesReturned))
            {
                return false;
            }

            if (bytesReturned < Marshal.SizeOf<STORAGE_DEVICE_DESCRIPTOR>())
            {
                return false;
            }

            STORAGE_DEVICE_DESCRIPTOR nativeDescriptor = StructureHelper.FromBytes<STORAGE_DEVICE_DESCRIPTOR>(outBuffer);
            descriptor.RemovableMedia  = nativeDescriptor.RemovableMedia != 0;
            descriptor.BusType         = (StorageBusType)nativeDescriptor.BusType;
            descriptor.VendorID        = ReadAnsiString(outBuffer, (int)nativeDescriptor.VendorIDOffset);
            descriptor.ProductID       = ReadAnsiString(outBuffer, (int)nativeDescriptor.ProductIDOffset);
            descriptor.ProductRevision = ReadAnsiString(outBuffer, (int)nativeDescriptor.ProductRevisionOffset);
            descriptor.SerialNumber    = ReadAnsiString(outBuffer, (int)nativeDescriptor.SerialNumberOffset);

            return true;
        }

        /// <inheritdoc />
        public bool TryGetStorageAdapterDescriptor(SafeFileHandle handle, out StorageAdapterDescriptorInfo descriptor)
        {
            descriptor = new StorageAdapterDescriptorInfo();
            STORAGE_PROPERTY_QUERY query = STORAGE_PROPERTY_QUERY.CreateDefault();
            query.PropertyID = 1;
            query.QueryType = 0;
            var inBuffer = StructureHelper.GetBytes(query);

            var outBuffer = new byte[256];
            if (!SendRawIoControl(handle, IoControlCodes.IOCTL_STORAGE_QUERY_PROPERTY, inBuffer, outBuffer, out var bytesReturned))
            {
                return false;
            }

            if (bytesReturned < Marshal.SizeOf<STORAGE_ADAPTER_DESCRIPTOR>())
            {
                return false;
            }

            STORAGE_ADAPTER_DESCRIPTOR nativeDescriptor = StructureHelper.FromBytes<STORAGE_ADAPTER_DESCRIPTOR>(outBuffer);
            descriptor.BusType = (StorageBusType)nativeDescriptor.BusType;
            descriptor.MaximumTransferLength = nativeDescriptor.MaximumTransferLength;
            descriptor.MaximumPhysicalPages = nativeDescriptor.MaximumPhysicalPages;
            descriptor.AlignmentMask = nativeDescriptor.AlignmentMask;

            return true;
        }

        /// <inheritdoc />
        public bool TryGetDriveLayout(SafeFileHandle handle, out byte[] rawLayout)
        {
            rawLayout = new byte[BufferSizeConstants.Size4K];

            if (!SendRawIoControl(handle, IoControlCodes.IOCTL_DISK_GET_DRIVE_LAYOUT_EX, null, rawLayout, out var bytesReturned))
            {
                rawLayout = null;
                return false;
            }

            if (bytesReturned <= 0)
            {
                rawLayout = null;
                return false;
            }

            if (bytesReturned != rawLayout.Length)
            {
                var trimmed = new byte[bytesReturned];
                Buffer.BlockCopy(rawLayout, 0, trimmed, 0, bytesReturned);
                rawLayout = trimmed;
            }

            return true;
        }

        /// <inheritdoc />
        public bool TryGetScsiAddress(SafeFileHandle handle, out ScsiAddressInfo scsiAddress)
        {
            scsiAddress = new ScsiAddressInfo();
            var outBuffer = new byte[Marshal.SizeOf<SCSI_ADDRESS>()];

            if (!SendRawIoControl(handle, IoControlCodes.IOCTL_SCSI_GET_ADDRESS, null, outBuffer, out var bytesReturned))
            {
                return false;
            }

            if (bytesReturned < Marshal.SizeOf<SCSI_ADDRESS>())
            {
                return false;
            }

            SCSI_ADDRESS nativeAddress = StructureHelper.FromBytes<SCSI_ADDRESS>(outBuffer);
            scsiAddress.Length     = nativeAddress.Length;
            scsiAddress.PortNumber = nativeAddress.PortNumber;
            scsiAddress.PathID     = nativeAddress.PathID;
            scsiAddress.TargetID   = nativeAddress.TargetID;
            scsiAddress.Lun        = nativeAddress.Lun;

            return true;
        }

        /// <inheritdoc />
        public bool TryGetStorageDeviceNumber(SafeFileHandle handle, out StorageDeviceNumberInfo info)
        {
            info = new StorageDeviceNumberInfo();
            var outBuffer = new byte[Marshal.SizeOf<STORAGE_DEVICE_NUMBER>()];

            if (!SendRawIoControl(handle, IoControlCodes.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, outBuffer, out var bytesReturned))
            {
                return false;
            }

            if (bytesReturned < Marshal.SizeOf<STORAGE_DEVICE_NUMBER>())
            {
                return false;
            }

            STORAGE_DEVICE_NUMBER nativeInfo = StructureHelper.FromBytes<STORAGE_DEVICE_NUMBER>(outBuffer);
            info.DeviceType      = nativeInfo.DeviceType;
            info.DeviceNumber    = nativeInfo.DeviceNumber;
            info.PartitionNumber = nativeInfo.PartitionNumber;

            return true;
        }

        /// <inheritdoc />
        public bool TryGetDriveGeometryEx(SafeFileHandle handle, out DiskGeometryInfo info)
        {
            info = new DiskGeometryInfo();
            var outBuffer = new byte[Marshal.SizeOf<DISK_GEOMETRY_EX>()];

            if (!SendRawIoControl(handle, IoControlCodes.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, outBuffer, out var bytesReturned))
            {
                return false;
            }

            if (bytesReturned < Marshal.SizeOf<DISK_GEOMETRY_EX>())
            {
                return false;
            }

            DISK_GEOMETRY_EX nativeGeometry = StructureHelper.FromBytes<DISK_GEOMETRY_EX>(outBuffer);
            info.Cylinders         = nativeGeometry.Geometry.Cylinders;
            info.MediaType         = nativeGeometry.Geometry.MediaType;
            info.TracksPerCylinder = nativeGeometry.Geometry.TracksPerCylinder;
            info.SectorsPerTrack   = nativeGeometry.Geometry.SectorsPerTrack;
            info.BytesPerSector    = nativeGeometry.Geometry.BytesPerSector;
            info.DiskSize          = (ulong)nativeGeometry.DiskSize;

            return true;
        }

        /// <inheritdoc />
        public bool TryGetPredictFailure(SafeFileHandle handle, out PredictFailureInfo info)
        {
            info = new PredictFailureInfo();
            var outBuffer = new byte[Marshal.SizeOf<STORAGE_PREDICT_FAILURE>()];

            if (!SendRawIoControl(handle, IoControlCodes.IOCTL_STORAGE_PREDICT_FAILURE, null, outBuffer, out var bytesReturned))
            {
                return false;
            }

            if (bytesReturned < Marshal.SizeOf<STORAGE_PREDICT_FAILURE>())
            {
                return false;
            }

            STORAGE_PREDICT_FAILURE nativeInfo = StructureHelper.FromBytes<STORAGE_PREDICT_FAILURE>(outBuffer);
            info.PredictsFailure    = nativeInfo.PredictFailure != 0;
            info.VendorSpecificData = nativeInfo.VendorSpecific ?? [];

            return true;
        }

        /// <inheritdoc />
        public bool TryGetSffDiskDeviceProtocol(SafeFileHandle handle, out StorageProtocolType protocolType)
        {
            protocolType = StorageProtocolType.Unknown;

            SFFDISK_QUERY_DEVICE_PROTOCOL_DATA queryResult = new SFFDISK_QUERY_DEVICE_PROTOCOL_DATA();
            queryResult.Size = (ushort)Marshal.SizeOf<SFFDISK_QUERY_DEVICE_PROTOCOL_DATA>();

            var outBuffer = StructureHelper.GetBytes(queryResult);

            if (!SendRawIoControl(handle, IoControlCodes.IOCTL_SFFDISK_QUERY_DEVICE_PROTOCOL, null, outBuffer, out var bytesReturned))
            {
                return false;
            }

            if (bytesReturned < Marshal.SizeOf<SFFDISK_QUERY_DEVICE_PROTOCOL_DATA>())
            {
                return false;
            }

            SFFDISK_QUERY_DEVICE_PROTOCOL_DATA nativeInfo = StructureHelper.FromBytes<SFFDISK_QUERY_DEVICE_PROTOCOL_DATA>(outBuffer);
            if (nativeInfo.ProtocolGuid == SffDiskProtocolGuids.SecureDigital)
            {
                protocolType = StorageProtocolType.SecureDigital;
            }
            else if (nativeInfo.ProtocolGuid == SffDiskProtocolGuids.MultiMediaCard)
            {
                protocolType = StorageProtocolType.MultiMediaCard;
            }
            else
            {
                protocolType = StorageProtocolType.Unknown;
            }

            return true;
        }

        /// <inheritdoc />
        public bool TryGetSmartVersion(SafeFileHandle handle, out SmartVersionInfo info)
        {
            info = new SmartVersionInfo();
            byte[] outBuffer = new byte[Marshal.SizeOf<GETVERSIONINPARAMS>()];
            int bytesReturned;
            if (!SendRawIoControl(handle, IoControlCodes.IOCTL_SMART_GET_VERSION, null, outBuffer, out bytesReturned))
            {
                return false;
            }

            if (bytesReturned < Marshal.SizeOf<GETVERSIONINPARAMS>())
            {
                return false;
            }

            GETVERSIONINPARAMS nativeInfo = StructureHelper.FromBytes<GETVERSIONINPARAMS>(outBuffer);
            info.Version        = nativeInfo.bVersion;
            info.Revision       = nativeInfo.bRevision;
            info.Reserved       = nativeInfo.bReserved;
            info.IdeDeviceMap   = nativeInfo.bIDEDeviceMap;
            info.Capabilities   = nativeInfo.fCapabilities;
            info.ReservedValues = nativeInfo.dwReserved ?? [];

            return true;
        }

        /// <inheritdoc />
        public bool TryScsiPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return SendRawIoControl(handle, IoControlCodes.IOCTL_SCSI_PASS_THROUGH, requestBuffer, responseBuffer, out bytesReturned);
        }

        /// <inheritdoc />
        public bool TryScsiMiniport(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return SendRawIoControl(handle, IoControlCodes.IOCTL_SCSI_MINIPORT, requestBuffer, responseBuffer, out bytesReturned);
        }

        /// <inheritdoc />
        public bool TryAtaPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return SendRawIoControl(handle, IoControlCodes.IOCTL_ATA_PASS_THROUGH, requestBuffer, responseBuffer, out bytesReturned);
        }

        /// <inheritdoc />
        public bool TryIdePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return SendRawIoControl(handle, IoControlCodes.IOCTL_IDE_PASS_THROUGH, requestBuffer, responseBuffer, out bytesReturned);
        }

        /// <inheritdoc />
        public bool TrySmartReceiveDriveData(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return SendRawIoControl(handle, IoControlCodes.DFP_RECEIVE_DRIVE_DATA, requestBuffer, responseBuffer, out bytesReturned);
        }

        /// <inheritdoc />
        public bool TrySmartSendDriveCommand(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return SendRawIoControl(handle, IoControlCodes.DFP_SEND_DRIVE_COMMAND, requestBuffer, responseBuffer, out bytesReturned);
        }

        /// <inheritdoc />
        public bool TryIntelNvmePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
        {
            return SendRawIoControl(handle, IoControlCodes.IOCTL_INTEL_NVME_PASS_THROUGH, requestBuffer, responseBuffer, out bytesReturned);
        }

        /// <summary>
        /// Determines whether a Win32 error indicates a severe device I/O failure or timeout.
        /// </summary>
        /// <param name="error">The Win32 error code.</param>
        /// <returns>Whether further requests to the device should be skipped.</returns>
        public static bool IsSevereDeviceIoError(int error)
        {
            return error == 55    //ERROR_DEV_NOT_EXIST
                || error == 1117  //ERROR_IO_DEVICE
                || error == 1167  //ERROR_DEVICE_NOT_CONNECTED
                || error == ErrorTimeout;
        }

        #endregion

        #region Internal

        /// <summary>
        /// Opens a Windows device without overlapped I/O for callers that perform synchronous reads.
        /// </summary>
        /// <param name="path">The device path.</param>
        /// <param name="desiredAccess">The requested access flags.</param>
        /// <param name="shareMode">The requested sharing flags.</param>
        /// <param name="creationDisposition">The handle creation mode.</param>
        /// <param name="flagsAndAttributes">Additional handle flags.</param>
        /// <returns>The opened handle, or an invalid handle when opening fails or times out.</returns>
        internal SafeFileHandle OpenDeviceSynchronous(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes)
        {
            return OpenDeviceInternal(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes, false);
        }

        /// <summary>
        /// Calls CreateFile for an open request on the current worker thread.
        /// </summary>
        /// <param name="path">The device path.</param>
        /// <param name="desiredAccess">The requested access flags.</param>
        /// <param name="shareMode">The requested sharing flags.</param>
        /// <param name="creationDisposition">The handle creation mode.</param>
        /// <param name="flagsAndAttributes">Additional handle flags.</param>
        /// <returns>The handle returned by Windows.</returns>
        internal static SafeFileHandle OpenDeviceCore(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes)
        {
            return Kernel32Native.CreateFile(path, desiredAccess, shareMode, IntPtr.Zero, creationDisposition, flagsAndAttributes, IntPtr.Zero);
        }

        #endregion

        #region Private

        /// <summary>
        /// Applies the open timeout and tracks handles that support overlapped requests.
        /// </summary>
        /// <param name="path">The device path.</param>
        /// <param name="desiredAccess">The requested access flags.</param>
        /// <param name="shareMode">The requested sharing flags.</param>
        /// <param name="creationDisposition">The handle creation mode.</param>
        /// <param name="flagsAndAttributes">Additional handle flags.</param>
        /// <param name="overlapped">Whether the handle will be used for asynchronous device control requests.</param>
        /// <returns>The opened handle, or an invalid handle when opening fails or times out.</returns>
        private SafeFileHandle OpenDeviceInternal(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes, bool overlapped)
        {
            LastIoControlCode      = 0;
            LastIoControlError     = 0;
            LastIoControlSucceeded = true;

            if (IsOpenDeviceInTimeoutCooldown(path))
            {
                return CreateInvalidFileHandle();
            }

            SafeFileHandle handle;
            if (OpenDeviceTimeoutMilliseconds <= 0)
            {
                handle = OpenDeviceCore(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes);
            }
            else
            {
                handle = OpenDeviceWithTimeout(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes, OpenDeviceTimeoutMilliseconds);
            }

            if (overlapped && handle != null && !handle.IsInvalid)
            {
                DeviceHandles.Add(handle, new DeviceHandleState(path));
            }

            return handle;
        }

        private static string ReadAnsiString(byte[] buffer, int offset)
        {
            if (offset <= 0 || offset >= buffer.Length)
            {
                return string.Empty;
            }

            int end = offset;
            while (end < buffer.Length && buffer[end] != 0)
            {
                ++end;
            }

            return StringUtil.TrimStorageString(Encoding.ASCII.GetString(buffer, offset, end - offset));
        }

        private static SafeFileHandle OpenDeviceWithTimeout(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes, int timeoutMilliseconds)
        {
            var request = new OpenDeviceRequest(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes);

            try
            {
                OpenDeviceWorker worker;

                lock (OpenDeviceWorkerSyncRoot)
                {
                    worker = GetOrCreateOpenDeviceWorker();
                    if (!worker.TryEnqueue(request))
                    {
                        worker.MarkAbandoned();
                        worker = CreateOpenDeviceWorker();

                        if (!worker.TryEnqueue(request))
                        {
                            RememberOpenDeviceTimeout(path);
                            return CreateInvalidFileHandle();
                        }
                    }

                    if (!request.Wait(timeoutMilliseconds) && request.MarkTimedOut())
                    {
                        worker.MarkAbandoned();

                        if (ReferenceEquals(_openDeviceWorker, worker))
                        {
                            _openDeviceWorker = null;
                        }

                        RememberOpenDeviceTimeout(path);
                        return CreateInvalidFileHandle();
                    }
                }

                if (request.Exception != null)
                {
                    throw request.Exception;
                }

                return request.Result ?? CreateInvalidFileHandle();
            }
            finally
            {
                request.Dispose();
            }
        }

        private static OpenDeviceWorker GetOrCreateOpenDeviceWorker()
        {
            if (_openDeviceWorker == null || !_openDeviceWorker.CanAcceptWork)
            {
                _openDeviceWorker = CreateOpenDeviceWorker();
            }

            return _openDeviceWorker;
        }

        private static OpenDeviceWorker CreateOpenDeviceWorker()
        {
            int id = Interlocked.Increment(ref _openDeviceWorkerSequence);
            return new OpenDeviceWorker(id);
        }

        private static bool IsOpenDeviceInTimeoutCooldown(string path)
        {
            string key = StringUtil.TrimStorageString(path);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!OpenDeviceTimeouts.TryGetValue(key, out var blockedUntilUtc))
            {
                return false;
            }

            if (DateTime.UtcNow < blockedUntilUtc)
            {
                return true;
            }

            OpenDeviceTimeouts.TryRemove(key, out _);
            return false;
        }

        private static void RememberOpenDeviceTimeout(string path)
        {
            string key = StringUtil.TrimStorageString(path);
            if (string.IsNullOrWhiteSpace(key) || OpenDeviceTimeoutCooldownMilliseconds <= 0)
            {
                return;
            }

            var blockedUntilUtc = DateTime.UtcNow.AddMilliseconds(OpenDeviceTimeoutCooldownMilliseconds);
            OpenDeviceTimeouts[key] = blockedUntilUtc;
        }

        private static SafeFileHandle CreateInvalidFileHandle()
        {
            return new SafeFileHandle(IntPtr.Zero, true);
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Tracks the device path and whether an I/O timeout has made its handle unsafe to reuse.
        /// </summary>
        private sealed class DeviceHandleState
        {
            #region Constructor

            /// <summary>
            /// Creates state for one opened device path.
            /// </summary>
            /// <param name="path">The path used to open the handle.</param>
            public DeviceHandleState(string path)
            {
                Path = path;
            }

            #endregion

            #region Fields

            private int _timedOut;

            #endregion

            #region Properties

            /// <summary>
            /// Gets the path used to open the handle.
            /// </summary>
            public string Path { get; }

            /// <summary>
            /// Gets or sets whether this handle has timed out and must not be reused.
            /// </summary>
            public bool TimedOut
            {
                get { return Volatile.Read(ref _timedOut) != 0; }
                set { Volatile.Write(ref _timedOut, value ? 1 : 0); }
            }

            #endregion
        }

        #endregion
    }
}
