/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using System.Text;

namespace DiskInfoToolkit.Utilities
{
    public static class StringUtil
    {
        #region Public

        public static bool EqualsAny(string value, params string[] candidates)
        {
            for (int i = 0; i < candidates.Length; ++i)
            {
                if (value.Equals(candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool StartsWithAny(string value, params string[] prefixes)
        {
            for (int i = 0; i < prefixes.Length; ++i)
            {
                if (value.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsAny(string value, params string[] candidates)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate)
                 && value.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static string FirstNonEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; ++i)
            {
                var candidate = TrimStorageString(values[i]);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        public static string TrimStorageString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Trim().Trim('\0').Trim('\n');
        }

        public static string CleanAscii(byte[] value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return TrimStorageString(Encoding.ASCII.GetString(value));
        }

        #endregion
    }
}
