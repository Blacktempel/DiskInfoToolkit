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
    public static class RaidMasterPortCompositeProbe
    {
        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || !device.Scsi.PortNumber.HasValue)
            {
                return false;
            }

            string scsiPortPath = StoragePathBuilder.BuildScsiPortPath(device.Scsi.PortNumber.Value);
            SafeFileHandle handle = ioControl.OpenDevice(
                scsiPortPath,
                IoAccess.GenericRead | IoAccess.GenericWrite,
                IoShare.ReadWrite,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            if (handle == null || handle.IsInvalid)
            {
                return false;
            }

            using (handle)
            {
                bool changed = false;

                if (device.Controller.Family == StorageControllerFamily.IntelRst || device.Controller.Family == StorageControllerFamily.IntelVroc)
                {
                    if (IntelRaidMiniportProbe.TryPopulateDataWithSignatureSweepFromHandle(device, ioControl, handle, scsiPortPath))
                    {
                        changed = true;
                    }
                }

                if (CsmiPortCompositeProbe.TryPopulateDataFromHandle(device, ioControl, handle, scsiPortPath))
                {
                    changed = true;
                }

                if (RaidPortCompositeProbe.TryPopulateDataFromHandle(device, ioControl, handle, scsiPortPath))
                {
                    changed = true;
                }

                if (changed && string.IsNullOrWhiteSpace(device.Controller.Kind))
                {
                    device.Controller.Kind = ControllerKindNames.RaidPort;
                }

                return changed;
            }
        }

        #endregion
    }
}
