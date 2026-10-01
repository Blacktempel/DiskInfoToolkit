/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Core;
using DiskInfoToolkit.Interop;

namespace DiskInfoToolkit.Partitions
{
    /// <summary>
    /// Shares one volume-to-disk lookup between all disks in a partition scan.
    /// Failed lookups are cached as well, so a problematic volume is not queried again in the same scan.
    /// </summary>
    internal sealed class WindowsVolumeExtentMap
    {
        #region Constructor

        /// <summary>
        /// Creates a lookup cache for one partition scan.
        /// </summary>
        /// <param name="ioControl">The I/O implementation used to query volumes.</param>
        internal WindowsVolumeExtentMap(IStorageIoControl ioControl)
        {
            _ioControl = ioControl ?? throw new ArgumentNullException(nameof(ioControl));
        }

        #endregion

        #region Fields

        private readonly IStorageIoControl _ioControl;

        private readonly Dictionary<char, VolumeExtentLookupResult> _responses = new();

        #endregion

        #region Internal

        /// <summary>
        /// Reads a volume's complete extent table once and reuses the result for later disks in the scan.
        /// </summary>
        /// <param name="driveLetter">The volume's drive letter.</param>
        /// <param name="extents">The complete extent table when the lookup succeeds.</param>
        /// <param name="severeFailure">Whether the request timed out or had a severe device error.</param>
        /// <param name="queryFailed">Whether an opened volume returned an incomplete or failed response.</param>
        /// <returns>Whether a complete extent table was read.</returns>
        internal bool TryGet(char driveLetter, out IReadOnlyList<DISK_EXTENT_RAW> extents,
            out bool severeFailure, out bool queryFailed)
        {
            if (!_responses.TryGetValue(driveLetter, out var response))
            {
                string path = $@"\\.\{driveLetter}:";

                bool success = WindowsVolumeDiskExtentReader.TryRead(path, _ioControl, out var readExtents);

                bool severe = !success && _ioControl is WindowsStorageIoControl failedIo
                    && (failedIo.LastOpenDeviceTimedOut || failedIo.LastIoControlWasSevereDeviceError);

                bool failedAfterOpen = !success && _ioControl is WindowsStorageIoControl queriedIo
                    && queriedIo.LastOpenDeviceSucceeded;

                response = new VolumeExtentLookupResult(success, readExtents, severe, failedAfterOpen);

                _responses.Add(driveLetter, response);

                if (success && _ioControl is WindowsStorageIoControl windowsIo)
                {
                    windowsIo.RegisterVolumeAlias(path, readExtents);
                }
            }

            extents       = response.Extents;
            severeFailure = response.SevereFailure;
            queryFailed   = response.QueryFailed;

            return response.Success;
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Stores one complete volume lookup result for reuse during a partition scan.
        /// </summary>
        private sealed class VolumeExtentLookupResult
        {
            #region Constructor

            /// <summary>
            /// Creates an immutable result from a volume extent lookup.
            /// </summary>
            /// <param name="success">Whether the complete extent table was read.</param>
            /// <param name="extents">The validated extents on success.</param>
            /// <param name="severeFailure">Whether the request timed out or had a severe device error.</param>
            /// <param name="queryFailed">Whether an opened volume returned an incomplete or failed response.</param>
            public VolumeExtentLookupResult(bool success, IReadOnlyList<DISK_EXTENT_RAW> extents,
                bool severeFailure, bool queryFailed)
            {
                Success       = success;
                Extents       = extents;
                SevereFailure = severeFailure;
                QueryFailed   = queryFailed;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Gets whether the complete extent table was read.
            /// </summary>
            public bool Success { get; }

            /// <summary>
            /// Gets the validated extents on success.
            /// </summary>
            public IReadOnlyList<DISK_EXTENT_RAW> Extents { get; }

            /// <summary>
            /// Gets whether the request timed out or had a severe device error.
            /// </summary>
            public bool SevereFailure { get; }

            /// <summary>
            /// Gets whether an opened volume returned an incomplete or failed response.
            /// </summary>
            public bool QueryFailed { get; }

            #endregion
        }

        #endregion
    }
}
