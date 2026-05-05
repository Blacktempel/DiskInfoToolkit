/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Core;

namespace DiskInfoToolkit.Probes
{
    public static class UsbBridgeProbe
    {
        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null)
            {
                return false;
            }

            UsbBridgeClassifier.Apply(device);
            UsbNvmeSetupModeDetector.Apply(device);

            if (!string.IsNullOrWhiteSpace(device.Usb.BridgeFamily))
            {
                ProbeTraceRecorder.Add(device, "USB path: bridge family classified as " + device.Usb.BridgeFamily + ".");
            }

            if (!string.IsNullOrWhiteSpace(device.Usb.NvmeSetupMode))
            {
                ProbeTraceRecorder.Add(device, "USB path: NVMe setup mode classified as " + device.Usb.NvmeSetupMode + ".");
            }

            if (ControllerServiceProbeRules.ShouldFilterNoSmartSupport(device))
            {
                if (string.IsNullOrWhiteSpace(device.FilterReason))
                {
                    device.FilterReason = "No SMART support on this USB storage path.";
                }

                ProbeTraceRecorder.Add(device, "USB path: filtered because the device matches a known no-SMART USB profile.");
                return true;
            }

            bool isUsbNvmeCandidate = UsbNvmeSetupModeDetector.IsUsbNvmeCandidate(device);
            if (isUsbNvmeCandidate)
            {
                if (UsbNvmeBridgeProbe.TryPopulateData(device, ioControl))
                {
                    UsbBridgeClassifier.ApplyInquiryHeuristics(device);
                    return true;
                }

                if (ShouldStopAfterSevereUsbIoError(device, ioControl, "USB NVMe bridge probe"))
                {
                    return false;
                }
            }
            else
            {
                ProbeTraceRecorder.Add(device, "USB path: skipping USB-NVMe vendor probe because no positive NVMe bridge marker was detected.");
            }

            if (device.Usb.IsMassStorageLike || UsbBridgeClassifier.IsUsbSatCapableBridge(device))
            {
                bool any = UsbMassStorageProbe.TryPopulateData(device, ioControl);
                if (!any && ShouldStopAfterSevereUsbIoError(device, ioControl, "USB mass-storage probe"))
                {
                    return false;
                }

                if (!device.SupportsSmart && UsbVendorScsiSmartProbe.TryPopulateData(device, ioControl))
                {
                    any = true;
                }
                else if (!device.SupportsSmart && ShouldStopAfterSevereUsbIoError(device, ioControl, "USB vendor SCSI SMART probe"))
                {
                    return any;
                }

                if (ScsiInquiryProbe.TryPopulateData(device, ioControl))
                {
                    any = true;
                }
                else if (ShouldStopAfterSevereUsbIoError(device, ioControl, "USB SCSI inquiry probe"))
                {
                    return any;
                }

                if (ScsiCapacityProbe.TryPopulateData(device, ioControl))
                {
                    any = true;
                }
                else if (ShouldStopAfterSevereUsbIoError(device, ioControl, "USB SCSI capacity probe"))
                {
                    return any;
                }

                UsbBridgeClassifier.ApplyInquiryHeuristics(device);

                return any;
            }

            bool vendorSmart = UsbVendorScsiSmartProbe.TryPopulateData(device, ioControl);
            if (!vendorSmart && ShouldStopAfterSevereUsbIoError(device, ioControl, "USB vendor SCSI SMART probe"))
            {
                return false;
            }

            bool inquiry = ScsiInquiryProbe.TryPopulateData(device, ioControl);
            if (!inquiry && ShouldStopAfterSevereUsbIoError(device, ioControl, "USB SCSI inquiry probe"))
            {
                return vendorSmart;
            }

            bool capacity = ScsiCapacityProbe.TryPopulateData(device, ioControl);
            if (!capacity && ShouldStopAfterSevereUsbIoError(device, ioControl, "USB SCSI capacity probe"))
            {
                return vendorSmart || inquiry;
            }

            UsbBridgeClassifier.ApplyInquiryHeuristics(device);

            return vendorSmart || inquiry || capacity;
        }

        #endregion

        #region Internal

        internal static bool ShouldStopAfterSevereUsbIoError(StorageDevice device, IStorageIoControl ioControl, string stage)
        {
            if (ioControl is WindowsStorageIoControl windowsIo && windowsIo.LastIoControlWasSevereDeviceError)
            {
                ProbeTraceRecorder.Add(device, $"USB path: stopping further probing after severe device I/O error during {stage}; ioControl=0x{windowsIo.LastIoControlCode:X8}, lastError={windowsIo.LastIoControlError}.");
                return true;
            }

            return false;
        }

        #endregion
    }
}
