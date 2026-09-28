/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Utilities;
using System.Text;

namespace DiskInfoToolkit.Probes
{
    public static class NvmeProbeUtil
    {
        #region Public

        public static void ApplyIdentifyControllerStrings(StorageDevice device, byte[] controllerData)
        {
            IntelNvmeProbeUtil.ApplyIdentifyControllerStrings(device, controllerData);
        }

        public static string ReadAscii(byte[] data, int offset, int count)
        {
            if (data == null || offset < 0 || offset + count > data.Length)
            {
                return string.Empty;
            }

            return StringUtil.TrimStorageString(Encoding.ASCII.GetString(data, offset, count));
        }

        public static bool HasAnyNonZeroByte(byte[] data)
        {
            if (data == null)
            {
                return false;
            }

            for (int i = 0; i < data.Length; ++i)
            {
                if (data[i] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
