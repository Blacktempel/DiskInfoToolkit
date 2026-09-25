/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit
{
    /// <summary>
    /// Progress counters for a running Microsoft Storage Spaces repair task.
    /// </summary>
    public sealed class StorageRepairProgress
    {
        #region Constructor

        /// <summary>
        /// Initializes repair progress from validated Spaceport byte counters.
        /// </summary>
        /// <param name="processedBytes">The number of bytes processed.</param>
        /// <param name="totalBytes">The total number of bytes to process.</param>
        internal StorageRepairProgress(ulong processedBytes, ulong totalBytes)
        {
            if (totalBytes == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalBytes));
            }

            if (processedBytes > totalBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(processedBytes));
            }

            ProcessedBytes = processedBytes;
            TotalBytes     = totalBytes;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the number of bytes processed by the repair task.
        /// </summary>
        public ulong ProcessedBytes { get; }

        /// <summary>
        /// Gets the total number of bytes reported for the repair task.
        /// </summary>
        public ulong TotalBytes { get; }

        /// <summary>
        /// Gets the percentage calculated from the byte counters.
        /// </summary>
        public double PercentComplete => (double)ProcessedBytes * 100 / TotalBytes;

        #endregion
    }
}
