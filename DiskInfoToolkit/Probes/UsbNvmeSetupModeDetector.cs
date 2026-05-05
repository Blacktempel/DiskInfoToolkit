/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Utilities;

namespace DiskInfoToolkit.Probes
{
    public static class UsbNvmeSetupModeDetector
    {
        #region Public

        public static bool IsUsbNvmeCandidate(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            string service = StringUtil.TrimStorageString(device.Controller.Service);
            if (service.Equals(ControllerServiceNames.SecNvme, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsKnownUsbNvmeController(device))
            {
                return true;
            }

            return HasUsbNvmeTextMarker(device);
        }

        public static void Apply(StorageDevice device)
        {
            if (device == null)
            {
                return;
            }

            device.Usb.NvmeSetupMode = string.Empty;

            string service = StringUtil.TrimStorageString(device.Controller.Service);
            ushort vendorId = device.Controller.VendorID.GetValueOrDefault();

            if (service.Equals(ControllerServiceNames.SecNvme, StringComparison.OrdinalIgnoreCase))
            {
                device.Usb.NvmeSetupMode = UsbNvmeSetupModeNames.SamsungVendorScsi;
                if (string.IsNullOrWhiteSpace(device.Usb.BridgeFamily))
                {
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Samsung;
                }
                return;
            }

            if (!IsUsbNvmeCandidate(device))
            {
                return;
            }

            if (vendorId == VendorIDConstants.JMicron)
            {
                device.Usb.NvmeSetupMode = UsbNvmeSetupModeNames.JMicronPassThrough;
                return;
            }

            if (vendorId == VendorIDConstants.Realtek)
            {
                device.Usb.NvmeSetupMode = UsbNvmeSetupModeNames.RealtekPassThrough;
                return;
            }

            if (vendorId == VendorIDConstants.Asmedia)
            {
                device.Usb.NvmeSetupMode = UsbNvmeSetupModeNames.ASMediaPassThrough;
                return;
            }

            if (vendorId == VendorIDConstants.Samsung)
            {
                device.Usb.NvmeSetupMode = UsbNvmeSetupModeNames.SamsungVendorScsi;
                return;
            }

            if (ControllerServiceProbeRules.IsUsbMassStorageService(service))
            {
                device.Usb.NvmeSetupMode = UsbNvmeSetupModeNames.StandardStorageQuery;
            }
        }

        #endregion

        #region Private

        private static bool IsKnownUsbNvmeController(StorageDevice device)
        {
            if (device == null || !device.Controller.VendorID.HasValue || !device.Controller.DeviceID.HasValue)
            {
                return false;
            }

            ushort vendorId = device.Controller.VendorID.Value;
            ushort productId = device.Controller.DeviceID.Value;

            if (vendorId == VendorIDConstants.Asmedia)
            {
                return productId == 0x2362
                    || productId == 0x2364;
            }

            if (vendorId == VendorIDConstants.Realtek)
            {
                return productId == 0x9210
                    || productId == 0x9211
                    || productId == 0x9220
                    || productId == 0x9221;
            }

            if (vendorId == VendorIDConstants.JMicron)
            {
                return productId == 0x0583
                    || productId == 0x0586;
            }

            return false;
        }

        private static bool HasUsbNvmeTextMarker(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            string text = string.Join(" ", new[]
            {
                device.DisplayName ?? string.Empty,
                device.DeviceDescription ?? string.Empty,
                device.ProductName ?? string.Empty,
                device.VendorName ?? string.Empty,
                device.Controller.Name ?? string.Empty,
                device.Controller.DeviceName ?? string.Empty,
                device.Controller.HardwareID ?? string.Empty,
                device.Controller.Identifier ?? string.Empty,
                device.DeviceInstanceID ?? string.Empty
            });

            return StringUtil.ContainsAny(text,
                "NVME",
                "NVM EXPRESS",
                "ASM236",
                "ASM246",
                "JMS583",
                "JMS586",
                "RTL921",
                "RTL922");
        }

        #endregion
    }
}
