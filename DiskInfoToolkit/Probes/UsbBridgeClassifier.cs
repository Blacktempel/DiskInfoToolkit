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
    public static class UsbBridgeClassifier
    {
        #region Public

        public static void Apply(StorageDevice device)
        {
            if (device == null)
            {
                return;
            }

            ushort vendorId = device.Controller.VendorID.GetValueOrDefault();
            string service = StringUtil.TrimStorageString(device.Controller.Service);

            device.Usb.IsMassStorageLike =
                ControllerServiceProbeRules.IsUsbMassStorageService(service)
                || string.Equals(device.Controller.Class, ControllerClassNames.Usb, StringComparison.OrdinalIgnoreCase);

            if (service.Equals(ControllerServiceNames.UaspStor, StringComparison.OrdinalIgnoreCase))
            {
                device.Usb.MassStorageProtocolName = UsbMassStorageProtocolNames.Uasp;
            }
            else if (service.Equals(ControllerServiceNames.UsbStor, StringComparison.OrdinalIgnoreCase) || service.Equals(ControllerServiceNames.UsbStorWithTrailingSpace, StringComparison.OrdinalIgnoreCase))
            {
                device.Usb.MassStorageProtocolName = UsbMassStorageProtocolNames.Bot;
            }
            else if (service.Equals(ControllerServiceNames.AsusStpt, StringComparison.OrdinalIgnoreCase))
            {
                device.Usb.MassStorageProtocolName = UsbMassStorageProtocolNames.Asus;
            }

            switch (vendorId)
            {
                case VendorIDConstants.Asmedia:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Asmedia;
                    break;
                case VendorIDConstants.Realtek:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Realtek;
                    break;
                case VendorIDConstants.JMicron:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.JMicron;
                    break;
                case VendorIDConstants.Samsung:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Samsung;
                    break;
                case VendorIDConstants.Buffalo:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Buffalo;
                    break;
                case VendorIDConstants.IoData:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.IoData;
                    break;
                case VendorIDConstants.Sunplus:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Sunplus;
                    break;
                case VendorIDConstants.Logitec:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Logitec;
                    break;
                case VendorIDConstants.Initio:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Initio;
                    break;
                case VendorIDConstants.Cypress:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Cypress;
                    break;
                case VendorIDConstants.Oxford:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Oxford;
                    break;
                case VendorIDConstants.Prolific:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Prolific;
                    break;
                case VendorIDConstants.Genesys:
                    device.Usb.BridgeFamily = UsbBridgeFamilyNames.Genesys;
                    break;
            }

            if (string.IsNullOrWhiteSpace(device.VendorName) && !string.IsNullOrWhiteSpace(device.Usb.BridgeFamily))
            {
                device.VendorName = device.Usb.BridgeFamily;
            }
        }

        public static bool IsNvmeBridge(StorageDevice device)
        {
            return UsbNvmeSetupModeDetector.IsUsbNvmeCandidate(device);
        }

        public static bool IsUsbSatCapableBridge(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            if (IsNvmeBridge(device))
            {
                return false;
            }

            string family = device.Usb.BridgeFamily ?? string.Empty;
            if (family.Equals(UsbBridgeFamilyNames.Asmedia, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.JMicron, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.Realtek, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.Buffalo, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.IoData, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.Sunplus, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.Logitec, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.Initio, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.Cypress, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.Oxford, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.Prolific, StringComparison.OrdinalIgnoreCase)
                || family.Equals(UsbBridgeFamilyNames.Genesys, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ControllerServiceProbeRules.IsUsbMassStorageService(device.Controller.Service);
        }

        public static void ApplyInquiryHeuristics(StorageDevice device)
        {
            if (device == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(device.Usb.BridgeFamily))
            {
                if (string.IsNullOrWhiteSpace(device.VendorName)
                    || device.VendorName.StartsWith(StorageTextConstants.Standard, StringComparison.OrdinalIgnoreCase)
                    || device.VendorName.StartsWith(StorageTextConstants.Vendor, StringComparison.OrdinalIgnoreCase))
                {
                    device.VendorName = device.Usb.BridgeFamily;
                }
            }

            if (!string.IsNullOrWhiteSpace(device.ProductName)
                && device.Usb.BridgeFamily.Equals(UsbBridgeFamilyNames.JMicron, StringComparison.OrdinalIgnoreCase)
                && device.ProductName.StartsWith(UsbBridgeFamilyNames.JMicronJmb3Prefix, StringComparison.OrdinalIgnoreCase)
                && device.Controller.HardwareID.Length >= 21)
            {
                char c1 = device.Controller.HardwareID[19];
                char c2 = device.Controller.HardwareID[20];
                if (device.ProductName.Length >= 14 && device.ProductName[13] == 'X')
                {
                    char[] chars = device.ProductName.ToCharArray();
                    chars[12] = c1;
                    chars[13] = c2;
                    device.ProductName = new string(chars);
                }
            }

            if (string.IsNullOrWhiteSpace(device.Controller.Kind))
            {
                string protocol = string.IsNullOrWhiteSpace(device.Usb.MassStorageProtocolName) ? StorageTextConstants.Usb : (StorageTextConstants.UsbPrefix + device.Usb.MassStorageProtocolName);
                if (!string.IsNullOrWhiteSpace(device.Usb.BridgeFamily))
                {
                    device.Controller.Kind = protocol + " " + device.Usb.BridgeFamily;
                }
                else
                {
                    device.Controller.Kind = protocol;
                }
            }
        }

        #endregion
    }
}
