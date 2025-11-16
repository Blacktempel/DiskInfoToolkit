/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Florian K.
 *
 * Code inspiration, improvements and fixes are from, but not limited to, following projects:
 * CrystalDiskInfo
 */

using System.Text;

namespace DiskInfoToolkit.Utilities
{
    internal static class ByteHandler
    {
        #region Public

        public static string ChangeByteOrder(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(str);

            for (int i = 0; i < sb.Length; i += 2)
            {
                if (i + 1 >= sb.Length)
                {
                    break;
                }

                var temp = sb[i];
                sb[i] = sb[i + 1];
                sb[i + 1] = temp;
            }

            return sb.ToString();
        }

        #endregion
    }
}
