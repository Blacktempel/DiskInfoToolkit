/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Utilities;

namespace DiskInfoToolkit.Core
{
    public static class ControllerServiceProbeRules
    {
        #region Fields

        public static readonly string[] RocketRaidServicePrefixes =
        {
            "hpt",
            "rr"
        };

        public static readonly string[] HighPointControllerMarkers =
        {
            "HighPoint",
            "RocketRAID",
            "Rocket RAID",
            "VEN_1103",
            "PCI\\VEN_1103",
            "VEN_HPT",
            "HPT"
        };

        public static readonly string[] MegaRaidControllerMarkers =
        {
            "MegaRAID",
            "Mega RAID",
            "MegaSAS",
            "Mega SAS",
            "PERC",
            "PowerEdge RAID",
            "Dell PERC",
            "LSI MegaRAID",
            "AVAGO MegaRAID",
            "Broadcom MegaRAID",
            "VEN_1000",
            "PCI\\VEN_1000"
        };

        #endregion

        #region Public

        public static bool IsUsbMassStorageService(string controllerService)
        {
            if (string.IsNullOrWhiteSpace(controllerService))
            {
                return false;
            }

            string service = StringUtil.TrimStorageString(controllerService);
            return service.Equals(ControllerServiceNames.UaspStor, StringComparison.OrdinalIgnoreCase)
                || service.Equals(ControllerServiceNames.UsbStor, StringComparison.OrdinalIgnoreCase)
                || service.Equals(ControllerServiceNames.UsbStorWithTrailingSpace, StringComparison.OrdinalIgnoreCase)
                || service.Equals(ControllerServiceNames.AsusStpt, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMegaRaidService(string controllerService)
        {
            if (string.IsNullOrWhiteSpace(controllerService))
            {
                return false;
            }

            string service = StringUtil.TrimStorageString(controllerService);
            return StringUtil.EqualsAny(service, ControllerServiceGroups.MegaRaidControllerServices);
        }

        public static bool IsMegaRaidController(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            if (device.Controller.Family == StorageControllerFamily.MegaRaid
             || IsMegaRaidService(device.Controller.Service))
            {
                return true;
            }

            return ContainsMarker(MegaRaidControllerMarkers,
                device.Controller.Name,
                device.Controller.Identifier,
                device.Controller.HardwareID,
                device.DevicePath,
                device.DeviceInstanceID,
                device.DeviceTypeLabel,
                device.DisplayName,
                device.ProductName);
        }

        public static bool IsHighPointRocketRaidService(string controllerService)
        {
            if (string.IsNullOrWhiteSpace(controllerService))
            {
                return false;
            }

            string service = StringUtil.TrimStorageString(controllerService);
            if (StringUtil.EqualsAny(service, ControllerServiceGroups.RocketRaidControllerServices))
            {
                return true;
            }

            return StringUtil.StartsWithAny(service, RocketRaidServicePrefixes);
        }

        public static bool IsHighPointRocketRaidController(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            if (device.Controller.Family == StorageControllerFamily.RocketRaid
             || IsHighPointRocketRaidService(device.Controller.Service))
            {
                return true;
            }

            return ContainsMarker(HighPointControllerMarkers,
                device.Controller.Name,
                device.Controller.Identifier,
                device.Controller.HardwareID,
                device.DevicePath,
                device.DeviceInstanceID,
                device.DeviceTypeLabel,
                device.DisplayName,
                device.ProductName);
        }

        public static bool IsAtaLikeScsiController(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            string service         = StringUtil.TrimStorageString(device.Controller.Service);
            string controllerClass = StringUtil.TrimStorageString(device.Controller.Class);
            string deviceTypeLabel = StringUtil.TrimStorageString(device.DeviceTypeLabel);

            if (controllerClass.Equals(ControllerClassNames.Usb, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsHighPointRocketRaidController(device)
             || IsMegaRaidController(device))
            {
                return true;
            }

            if (service.Equals(ControllerServiceNames.AmdSata, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.AmdSataAlt, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.AsusStpt, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.Storahci, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.UaspStor, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if ((service.Equals(ControllerServiceNames.LsiSas, StringComparison.OrdinalIgnoreCase)
                    || service.Equals(ControllerServiceNames.LsiSas2, StringComparison.OrdinalIgnoreCase)
                    || service.Equals(ControllerServiceNames.LsiSas2i, StringComparison.OrdinalIgnoreCase)
                    || service.Equals(ControllerServiceNames.LsiSas3, StringComparison.OrdinalIgnoreCase)
                    || service.Equals(ControllerServiceNames.LsiSas3i, StringComparison.OrdinalIgnoreCase))
              && deviceTypeLabel.StartsWith("ATA ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if ((service.Equals(ControllerServiceNames.ItSas35, StringComparison.OrdinalIgnoreCase)
                    || service.Equals(ControllerServiceNames.ItSas35i, StringComparison.OrdinalIgnoreCase))
                && controllerClass.Equals(ControllerClassNames.Sas, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static bool IsScsiRaidController(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            string service         = StringUtil.TrimStorageString(device.Controller.Service);
            string controllerClass = StringUtil.TrimStorageString(device.Controller.Class);

            if (IsMegaRaidController(device))
            {
                return true;
            }

            if (IsHighPointRocketRaidController(device))
            {
                return true;
            }

            if (service.Equals(ControllerServiceNames.LsiSas, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.LsiSas2, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.LsiSas2i, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.LsiSas3, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.LsiSas3i, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.ItSas35, StringComparison.OrdinalIgnoreCase)
             || service.Equals(ControllerServiceNames.ItSas35i, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (controllerClass.Equals(ControllerClassNames.Sas, StringComparison.OrdinalIgnoreCase)
             || controllerClass.Equals(ControllerClassNames.Raid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static bool ShouldFilterNoSmartSupport(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            string display = device.DisplayName ?? string.Empty;
            return display.StartsWith(StorageDetectionFilter.Drobo5D, StringComparison.OrdinalIgnoreCase)
                || display.StartsWith(StorageDetectionFilter.VirtualDisk, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns whether the device belongs to an Intel RST or Intel VROC storage controller.
        /// </summary>
        public static bool IsIntelRstOrVrocController(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            if (device.Controller.Family == StorageControllerFamily.IntelRst
             || device.Controller.Family == StorageControllerFamily.IntelVroc)
            {
                return true;
            }

            string service = StringUtil.TrimStorageString(device.Controller.Service);
            if (string.IsNullOrWhiteSpace(service))
            {
                return false;
            }

            return service.StartsWith(ControllerServiceNames.IaStorPrefix, StringComparison.OrdinalIgnoreCase)
                || service.Equals(ControllerServiceNames.IaVroc, StringComparison.OrdinalIgnoreCase)
                || StringUtil.EqualsAny(service, ControllerServiceGroups.IntelRstControllerServices)
                || StringUtil.EqualsAny(service, ControllerServiceGroups.IntelRaidProbeServices);
        }

        /// <summary>
        /// Returns whether unit-level StorageDeviceProtocolSpecificProperty (50) NVMe queries are safe for this device.
        /// </summary>
        /// <remarks>
        /// On Intel RST/VROC controllers (e.g. iaStorVD), storport routes PropertyID 50 through
        /// RaUnitStorageQueryDeviceProtocolSpecificPropertyIoctl as a "PROTOCOL" SRB that triggers bugcheck 0x139.
        /// </remarks>
        public static bool ShouldAllowDeviceProtocolQuery(StorageDevice device)
        {
            return !IsIntelRstOrVrocController(device);
        }

        internal static bool ShouldStopAfterSevereIoError(
            StorageDevice device,
            IStorageIoControl ioControl,
            string pathLabel,
            string stage,
            bool stopOnOpenTimeout = false)
        {
            if (ioControl is IStorageIoControlErrorState errorState
             && (errorState.LastIoControlWasSevereDeviceError || (stopOnOpenTimeout && errorState.LastOpenDeviceTimedOut)))
            {
                string timeoutSuffix = stopOnOpenTimeout && errorState.LastOpenDeviceTimedOut
                    ? ", openTimedOut=True"
                    : string.Empty;

                ProbeTraceRecorder.Add(
                    device,
                    $"{pathLabel}: stopping further probing after severe device I/O error during {stage}; ioControl=0x{errorState.LastIoControlCode:X8}, lastError={errorState.LastIoControlError}{timeoutSuffix}.");
                return true;
            }

            return false;
        }

        #endregion

        #region Private

        private static bool ContainsMarker(string[] markers, params string[] values)
        {
            if (markers == null || values == null)
            {
                return false;
            }

            for (int valueIndex = 0; valueIndex < values.Length; ++valueIndex)
            {
                string value = StringUtil.TrimStorageString(values[valueIndex]);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                for (int markerIndex = 0; markerIndex < markers.Length; ++markerIndex)
                {
                    string marker = markers[markerIndex];
                    if (!string.IsNullOrWhiteSpace(marker)
                     && value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion
    }
}
