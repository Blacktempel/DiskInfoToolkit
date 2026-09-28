/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Devices;
using Microsoft.Win32.SafeHandles;

namespace DiskInfoToolkit.Probes
{
    public static class RaidSatPortProbe
    {
        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || !device.Scsi.PortNumber.HasValue)
            {
                return false;
            }

            string scsiPortPath = StoragePathBuilder.BuildScsiPortPath(device.Scsi.PortNumber.Value);
            SafeFileHandle handle = ioControl.OpenDevice(scsiPortPath, IoAccess.GenericRead | IoAccess.GenericWrite, IoShare.ReadWrite, IoCreation.OpenExisting, IoFlags.Normal);
            if (handle == null || handle.IsInvalid)
            {
                return false;
            }

            using (handle)
            {
                return TryPopulateDataFromHandle(device, ioControl, handle, scsiPortPath);
            }
        }

        public static bool TryPopulateDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle, string scsiPortPath)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            bool any = false;
            if (ScsiSatProbe.TryPopulateIdentifyDataFromHandle(device, ioControl, handle))
            {
                any = true;
            }

            if (ScsiSatProbe.TryPopulateSmartDataFromHandle(device, ioControl, handle))
            {
                any = true;
            }

            if (!any)
            {
                return false;
            }

            device.AlternateDevicePath = scsiPortPath;
            if (string.IsNullOrWhiteSpace(device.Controller.Kind))
            {
                device.Controller.Kind = ControllerKindNames.SatPort;
            }

            return true;
        }

        #endregion
    }
}
