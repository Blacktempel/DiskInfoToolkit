/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Utilities;

namespace DiskInfoToolkit.Probes
{
    public static class IntelNvmeProbeUtil
    {
        #region Public

        public static void ApplyIdentifyControllerStrings(StorageDevice device, byte[] controllerData)
        {
            if (device == null || controllerData == null || controllerData.Length < 72)
            {
                return;
            }

            var serial   = NvmeProbeUtil.ReadAscii(controllerData,  4, 20);
            var model    = NvmeProbeUtil.ReadAscii(controllerData, 24, 40);
            var firmware = NvmeProbeUtil.ReadAscii(controllerData, 64,  8);

            bool serialOk   = IsPlausibleNvmeIdentifyField(serial  , 20);
            bool modelOk    = IsPlausibleNvmeIdentifyField(model   , 40);
            bool firmwareOk = IsPlausibleNvmeIdentifyField(firmware,  8);

            if (!serialOk && !modelOk && !firmwareOk)
            {
                return;
            }

            //Prefer using Serial Number from Identify Controller data, as IOCTL_STORAGE_QUERY_PROPERTY may
            //return a different value which does not match the drive's actual serial number.
            if (serialOk)
            {
                serial = StringUtil.TrimStorageString(serial);
                device.SerialNumber = serial;
            }

            if (modelOk)
            {
                model = StringUtil.TrimStorageString(model);
                if (string.IsNullOrWhiteSpace(device.ProductName)
                    || device.ProductName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase)
                    || device.ProductName.Equals(StorageTextConstants.DiskDrive, StringComparison.OrdinalIgnoreCase))
                {
                    device.ProductName = model;
                }

                if (string.IsNullOrWhiteSpace(device.DisplayName)
                    || device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase)
                    || device.DisplayName.Equals(StorageTextConstants.DiskDrive, StringComparison.OrdinalIgnoreCase))
                {
                    device.DisplayName = model;
                }
            }

            //Prefer using Firmware / Revision from Identify Controller data, as IOCTL_STORAGE_QUERY_PROPERTY may
            //return a different value which does not match the drive's actual version.
            if (firmwareOk)
            {
                firmware = StringUtil.TrimStorageString(firmware);
                device.ProductRevision = firmware;
            }
        }

        #endregion

        #region Private

        private static bool IsPlausibleNvmeIdentifyField(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = StringUtil.TrimStorageString(value);
            if (value.Length == 0 || value.Length > maxLength)
            {
                return false;
            }

            int printableCount = 0;
            int alphaNumericCount = 0;

            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];

                if (char.IsLetterOrDigit(c))
                {
                    ++alphaNumericCount;
                }

                if (c >= 0x20 && c <= 0x7E)
                {
                    ++printableCount;
                    continue;
                }

                return false;
            }

            if (printableCount != value.Length)
            {
                return false;
            }

            return alphaNumericCount > 0;
        }

        #endregion
    }
}
