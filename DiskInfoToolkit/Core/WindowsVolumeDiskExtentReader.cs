/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Interop;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Core
{
    /// <summary>
    /// Reads the complete Windows disk-extent table of a volume through one shared IOCTL path.
    /// </summary>
    internal static class WindowsVolumeDiskExtentReader
    {
        #region Fields

        private const int MaximumVolumeExtents = 4096;

        #endregion

        #region Internal

        /// <summary>
        /// Opens a volume and reads every disk extent, retrying when the first buffer is too small.
        /// </summary>
        /// <param name="volumeDevicePath">A drive path or volume GUID path without a trailing separator.</param>
        /// <param name="ioControl">The platform IOCTL implementation.</param>
        /// <param name="extents">The complete validated extent table on success.</param>
        /// <returns>Whether the complete table was read.</returns>
        internal static bool TryRead(string volumeDevicePath, IStorageIoControl ioControl, out IReadOnlyList<DISK_EXTENT_RAW> extents)
        {
            extents = null;

            if (string.IsNullOrWhiteSpace(volumeDevicePath) || ioControl == null)
            {
                return false;
            }

            SafeFileHandle handle = ioControl.OpenDevice(
                volumeDevicePath,
                IoAccess.None,
                IoShare.All,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            if (handle == null || handle.IsInvalid)
            {
                return false;
            }

            using (handle)
            {
                return TryReadResponse(buffer =>
                {
                    bool success = ioControl.SendRawIoControl(handle, IoControlCodes.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, buffer, out int bytesReturned);

                    return (success, bytesReturned);
                }, out extents);
            }
        }

        /// <summary>
        /// Reads and validates the IOCTL response without opening hardware, for synthetic tests.
        /// </summary>
        /// <param name="request">Writes one response into the supplied buffer.</param>
        /// <param name="extents">The complete validated extent table on success.</param>
        /// <returns>Whether the complete table was read.</returns>
        internal static bool TryReadResponse(
            Func<byte[], (bool Success, int BytesReturned)> request,
            out IReadOnlyList<DISK_EXTENT_RAW> extents)
        {
            extents = null;

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            int extentOffset = (int)Marshal.OffsetOf<VOLUME_DISK_EXTENTS_RAW>(nameof(VOLUME_DISK_EXTENTS_RAW.FirstExtent));
            int extentSize = Marshal.SizeOf<DISK_EXTENT_RAW>();

            var buffer = new byte[BufferSizeConstants.Size4K];
            var response = request(buffer);

            // ERROR_MORE_DATA returns the extent count in the response header. A retry is
            // useful only when that count requires more space than the supplied buffer.
            if (!response.Success)
            {
                if (response.BytesReturned < sizeof(uint) || response.BytesReturned > buffer.Length
                 || !TryGetTableSize(BitConverter.ToUInt32(buffer, 0), extentOffset, extentSize, out int requiredSize)
                 || requiredSize <= buffer.Length)
                {
                    return false;
                }

                buffer = new byte[requiredSize];
                response = request(buffer);
            }

            if (!response.Success || response.BytesReturned < extentOffset || response.BytesReturned > buffer.Length
             || !TryGetTableSize(BitConverter.ToUInt32(buffer, 0), extentOffset, extentSize, out int tableSize)
             || tableSize > response.BytesReturned)
            {
                return false;
            }

            int count = (int)BitConverter.ToUInt32(buffer, 0);

            int diskNumberOffset = (int)Marshal.OffsetOf<DISK_EXTENT_RAW>(nameof(DISK_EXTENT_RAW.DiskNumber    ));
            int startingOffset   = (int)Marshal.OffsetOf<DISK_EXTENT_RAW>(nameof(DISK_EXTENT_RAW.StartingOffset));
            int lengthOffset     = (int)Marshal.OffsetOf<DISK_EXTENT_RAW>(nameof(DISK_EXTENT_RAW.ExtentLength  ));

            var result = new List<DISK_EXTENT_RAW>(count);

            // Parse each extent record from the response buffer.
            for (int i = 0; i < count; ++i)
            {
                int offset = extentOffset + (i * extentSize);

                result.Add(new DISK_EXTENT_RAW
                {
                    DiskNumber     = BitConverter.ToUInt32(buffer, offset + diskNumberOffset),
                    StartingOffset = BitConverter.ToInt64 (buffer, offset + startingOffset  ),
                    ExtentLength   = BitConverter.ToInt64 (buffer, offset + lengthOffset    )
                });
            }

            extents = result;

            return true;
        }

        #endregion

        #region Private

        /// <summary>
        /// Validates the count before allocating a larger response buffer or parsing extents.
        /// </summary>
        /// <param name="count">The volume manager's extent count.</param>
        /// <param name="extentOffset">The first extent's byte offset.</param>
        /// <param name="extentSize">The size of one extent record.</param>
        /// <param name="tableSize">The required response size in bytes.</param>
        /// <returns>Whether the count and size are within the supported bound.</returns>
        private static bool TryGetTableSize(uint count, int extentOffset, int extentSize, out int tableSize)
        {
            tableSize = 0;

            if (count == 0 || count > MaximumVolumeExtents)
            {
                return false;
            }

            tableSize = checked(extentOffset + ((int)count * extentSize));

            return true;
        }

        #endregion
    }
}
