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
using DiskInfoToolkit.Pnp;
using DiskInfoToolkit.Probes;

namespace DiskInfoToolkit.Utilities
{
    internal static class UsbStorageDeviceIdentifier
    {
        #region Public

        public static bool IsUsbDevice(PnpDiskNode node)
        {
            if (node == null)
            {
                return false;
            }

            return node.IsUsbConnected
                || IsUsbInstanceId(node.DeviceInstanceID)
                || IsUsbInstanceId(node.ParentInstanceID)
                || IsUsbInstanceId(node.HardwareID)
                || IsUsbInstanceId(node.ParentHardwareID)
                || IsUsbService(node.ParentService)
                || string.Equals(node.ParentClass, ControllerClassNames.Usb, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUsbDevice(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            return device.IsUsbConnected
                || device.BusType == StorageBusType.Usb
                || device.TransportKind == StorageTransportKind.Usb
                || device.ProbeStrategy == ProbeStrategy.UsbProbe
                || IsUsbInstanceId(device.DeviceInstanceID)
                || IsUsbInstanceId(device.ParentInstanceID)
                || (device.Controller != null
                    && (device.Controller.IsUsbStyleHardwareID
                        || IsUsbService(device.Controller.Service)
                        || string.Equals(device.Controller.Class, ControllerClassNames.Usb, StringComparison.OrdinalIgnoreCase)));
        }

        public static bool IsUsbInstanceId(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return false;
            }

            return instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)
                || instanceId.StartsWith(@"USBSTOR\", StringComparison.OrdinalIgnoreCase)
                || instanceId.StartsWith(@"UASPSTOR\", StringComparison.OrdinalIgnoreCase)
                || instanceId.IndexOf("/usb", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsUsbService(string service)
        {
            return ControllerServiceProbeRules.IsUsbMassStorageService(service)
                || string.Equals(service, "usb-storage", StringComparison.OrdinalIgnoreCase)
                || string.Equals(service, "uas", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
