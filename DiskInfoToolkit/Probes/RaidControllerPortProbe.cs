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
    public static class RaidControllerPortProbe
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
                return TryPopulateDataFromHandle(device, ioControl, handle, scsiPortPath);
            }
        }

        public static bool TryPopulateDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle, string scsiPortPath)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            bool changed = false;

            if (ioControl.TryGetStorageAdapterDescriptor(handle, out var adapterDescriptor))
            {
                if (device.BusType == StorageBusType.Unknown && adapterDescriptor.BusType != StorageBusType.Unknown)
                {
                    device.BusType = adapterDescriptor.BusType;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                {
                    device.Controller.Kind = ControllerKindNames.RaidPort;
                    changed = true;
                }
            }

            if (ioControl.TryGetStorageDeviceDescriptor(handle, out var deviceDescriptor))
            {
                if (string.IsNullOrWhiteSpace(device.VendorName) && !string.IsNullOrWhiteSpace(deviceDescriptor.VendorID))
                {
                    device.VendorName = deviceDescriptor.VendorID;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(device.ProductName) && !string.IsNullOrWhiteSpace(deviceDescriptor.ProductID))
                {
                    device.ProductName = deviceDescriptor.ProductID;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(device.ProductRevision) && !string.IsNullOrWhiteSpace(deviceDescriptor.ProductRevision))
                {
                    device.ProductRevision = deviceDescriptor.ProductRevision;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(device.SerialNumber) && !string.IsNullOrWhiteSpace(deviceDescriptor.SerialNumber))
                {
                    device.SerialNumber = deviceDescriptor.SerialNumber;
                    changed = true;
                }
            }

            if (ioControl.TryGetScsiAddress(handle, out var scsiAddress))
            {
                device.Scsi.PathID = scsiAddress.PathID;
                device.Scsi.TargetID = scsiAddress.TargetID;
                device.Scsi.Lun = scsiAddress.Lun;
                changed = true;
            }

            if (ioControl.TryGetSmartVersion(handle, out var smartVersionInfo))
            {
                device.SupportsSmart = true;
                device.SmartVersionRaw = smartVersionInfo.Capabilities;
                changed = true;
            }

            if (changed)
            {
                device.AlternateDevicePath = scsiPortPath;
            }

            return changed;
        }

        #endregion
    }
}
