/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Partitions
{
    /// <summary>
    /// Defines disk partition style values.
    /// </summary>
    public enum DiskPartitionStyle
    {
        Mbr = 0,
        Gpt = 1,
        Raw = 2
    }
}
