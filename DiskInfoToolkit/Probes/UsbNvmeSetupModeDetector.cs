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
            ushort vendorID = device.Controller.VendorID.GetValueOrDefault();
            string bridgeFamily = StringUtil.TrimStorageString(device.Usb.BridgeFamily);

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

            if (vendorID == VendorIDConstants.JMicron
             || bridgeFamily.Equals(UsbBridgeFamilyNames.JMicron, StringComparison.OrdinalIgnoreCase))
            {
                device.Usb.NvmeSetupMode = UsbNvmeSetupModeNames.JMicronPassThrough;
                return;
            }

            if (vendorID == VendorIDConstants.Realtek
             || bridgeFamily.Equals(UsbBridgeFamilyNames.Realtek, StringComparison.OrdinalIgnoreCase))
            {
                device.Usb.NvmeSetupMode = UsbNvmeSetupModeNames.RealtekPassThrough;
                return;
            }

            if (vendorID == VendorIDConstants.Asmedia
             || bridgeFamily.Equals(UsbBridgeFamilyNames.Asmedia, StringComparison.OrdinalIgnoreCase)
             || IsOtherWorldComputingExpress1M2(device))
            {
                device.Usb.NvmeSetupMode = UsbNvmeSetupModeNames.ASMediaPassThrough;
                return;
            }

            if (vendorID == VendorIDConstants.Samsung
             || bridgeFamily.Equals(UsbBridgeFamilyNames.Samsung, StringComparison.OrdinalIgnoreCase))
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

        private static bool IsOtherWorldComputingExpress1M2(StorageDevice device)
        {
            return device != null
                && device.Controller.VendorID.GetValueOrDefault() == VendorIDConstants.OtherWorldComputing
                && device.Controller.DeviceID.GetValueOrDefault() == UsbNvmeBridgeProductIDConstants.OtherWorldComputingExpress1M2;
        }

        private static bool IsKnownUsbNvmeController(StorageDevice device)
        {
            if (device == null || !device.Controller.VendorID.HasValue || !device.Controller.DeviceID.HasValue)
            {
                return false;
            }

            ushort vendorID = device.Controller.VendorID.Value;
            ushort productID = device.Controller.DeviceID.Value;

            if (vendorID == VendorIDConstants.Asmedia)
            {
                return productID == UsbNvmeBridgeProductIDConstants.AsmediaAsm2362
                    || productID == UsbNvmeBridgeProductIDConstants.AsmediaAsm2364;
            }

            if (vendorID == VendorIDConstants.Realtek)
            {
                return productID == UsbNvmeBridgeProductIDConstants.RealtekRtl9210
                    || productID == UsbNvmeBridgeProductIDConstants.RealtekRtl9211
                    || productID == UsbNvmeBridgeProductIDConstants.RealtekRtl9220
                    || productID == UsbNvmeBridgeProductIDConstants.RealtekRtl9221;
            }

            if (vendorID == VendorIDConstants.JMicron)
            {
                return productID == UsbNvmeBridgeProductIDConstants.JMicronJms583
                    || productID == UsbNvmeBridgeProductIDConstants.JMicronJms586;
            }

            if (vendorID == VendorIDConstants.OtherWorldComputing)
            {
                return IsOtherWorldComputingExpress1M2(device);
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
