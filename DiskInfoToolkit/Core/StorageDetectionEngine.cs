/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Logging;
using DiskInfoToolkit.PCI;
using DiskInfoToolkit.Pnp;
using DiskInfoToolkit.Usb;
using DiskInfoToolkit.Utilities;
using DiskInfoToolkit.Vendors;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;

namespace DiskInfoToolkit.Core
{
    public sealed class StorageDetectionEngine
    {
        #region Constructor

        public StorageDetectionEngine(
            IStorageIoControl ioControl,
            ExternalVendorLibraryManager vendorLibraries,
            OptionalVendorBackendSet vendorBackends)
        {
            if (ioControl == null)
                throw new ArgumentNullException(nameof(ioControl));
            if (vendorLibraries == null)
                throw new ArgumentNullException(nameof(vendorLibraries));
            if (vendorBackends == null)
                throw new ArgumentNullException(nameof(vendorBackends));

            _ioControl = ioControl;
            _vendorLibraries = vendorLibraries;
            _vendorBackends = vendorBackends;
        }

        #endregion

        #region Fields

        private readonly IStorageIoControl _ioControl;

        private readonly ExternalVendorLibraryManager _vendorLibraries;

        private readonly OptionalVendorBackendSet _vendorBackends;

        #endregion

        #region Public

        public List<StorageDevice> GetDisks()
        {
            List<StorageDevice> result = new List<StorageDevice>();

#if DEBUG
            var sw = Stopwatch.StartNew();
#endif

            //Enumerate disk interfaces via PnP (SetupAPI) and create initial StorageDevice objects with basic properties
            List<PnpDiskNode> diskNodes = PnpDiskEnumerator.EnumerateDiskInterfaces();

#if DEBUG
            sw.Stop();
            LogSimple.LogTrace($"{nameof(PnpDiskEnumerator.EnumerateDiskInterfaces)} completed in {sw.ElapsedMilliseconds} ms.");
#endif

            foreach (var node in diskNodes)
            {
#if DEBUG
                sw.Restart();
#endif

                //Create base device and map properties from PnP enumeration
                StorageDevice device = CreateBaseDevice(node);

                MapParentControllerProperties(device, node);
                ApplyControllerIdNames(device);
                ClassifyController(device);
                ApplyDeviceFilters(device);

                //Fetch standard storage properties via Storage IOCTLs
                AttachStandardStorageProperties(device);
                SelectProbeStrategy(device);

                if (!device.IsFiltered)
                {
                    //Probe device with appropriate strategy based on controller service and class
                    StorageProbeDispatcher.Probe(device, _ioControl, _vendorBackends);
                }

#if DEBUG
                sw.Stop();
                LogSimple.LogTrace($"Processed device '{device.DisplayName}' in {sw.ElapsedMilliseconds} ms.");
#endif

                result.Add(device);
            }

            return result;
        }

        #endregion

        #region Private

        private static StorageDevice CreateBaseDevice(PnpDiskNode node)
        {
            StorageDevice device = new StorageDevice();
            device.DevicePath            = node.DevicePath ?? string.Empty;
            device.AlternateDevicePath   = node.DevicePath ?? string.Empty;
            device.DeviceInstanceID      = node.DeviceInstanceID ?? string.Empty;
            device.ParentInstanceID      = node.ParentInstanceID ?? string.Empty;
            device.DisplayName           = FirstNonEmpty(node.FriendlyName, node.DeviceDescription, node.DevicePath, StorageTextConstants.UnknownDisk);
            device.DeviceDescription     = node.DeviceDescription ?? string.Empty;
            device.DeviceTypeLabel       = FirstNonEmpty(node.DeviceDescription, StorageTextConstants.DiskDrive);
            device.Controller.Name       = FirstNonEmpty(node.ParentDisplayName, StorageTextConstants.DriveController);
            device.Controller.HardwareID = FirstNonEmpty(node.ParentHardwareID, node.HardwareID, string.Empty);
            device.Controller.Identifier = node.ControllerIdentifier ?? string.Empty;

            VendorIDParser.TryParse(device.Controller.HardwareID, out var vendorId, out var deviceId, out var revision, out var isUsbStyle);
            device.Controller.VendorID             = vendorId;
            device.Controller.DeviceID             = deviceId;
            device.Controller.Revision             = revision;
            device.Controller.IsUsbStyleHardwareID = isUsbStyle;

            return device;
        }

        private static void MapParentControllerProperties(StorageDevice device, PnpDiskNode node)
        {
            device.Controller.Class   = node.ParentClass ?? string.Empty;
            device.Controller.Service = node.ParentService ?? string.Empty;
            device.Controller.Name    = FirstNonEmpty(node.ParentDisplayName, device.Controller.Name, StorageTextConstants.DriveController);

            if (string.IsNullOrWhiteSpace(device.Controller.HardwareID))
            {
                device.Controller.HardwareID = node.ParentHardwareID ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(device.Controller.Identifier))
            {
                device.Controller.Identifier = node.ControllerIdentifier ?? string.Empty;
            }
        }

        private static void ApplyControllerIdNames(StorageDevice device)
        {
            if (device == null || device.Controller == null || !device.Controller.VendorID.HasValue)
            {
                return;
            }

            if (device.Controller.IsUsbStyleHardwareID)
            {
                if (USBIDReader.TryGetVendorAndDeviceName(
                    device.Controller.VendorID.Value,
                    device.Controller.DeviceID.GetValueOrDefault(),
                    out var vendorName,
                    out var deviceName))
                {
                    if (string.IsNullOrWhiteSpace(device.Controller.VendorName) && !string.IsNullOrWhiteSpace(vendorName))
                    {
                        device.Controller.VendorName = vendorName;
                    }

                    if (string.IsNullOrWhiteSpace(device.Controller.DeviceName) && !string.IsNullOrWhiteSpace(deviceName))
                    {
                        device.Controller.DeviceName = deviceName;
                    }
                }

                return;
            }

            if (PCIIDReader.TryGetVendorAndDeviceName(
                device.Controller.VendorID.Value,
                device.Controller.DeviceID.GetValueOrDefault(),
                out var pciVendorName,
                out var pciDeviceName))
            {
                if (string.IsNullOrWhiteSpace(device.Controller.VendorName) && !string.IsNullOrWhiteSpace(pciVendorName))
                {
                    device.Controller.VendorName = pciVendorName;
                }

                if (string.IsNullOrWhiteSpace(device.Controller.DeviceName) && !string.IsNullOrWhiteSpace(pciDeviceName))
                {
                    device.Controller.DeviceName = pciDeviceName;
                }
            }
        }

        private static void ClassifyController(StorageDevice device)
        {
            string service = device.Controller.Service ?? string.Empty;
            string controllerClass = device.Controller.Class ?? string.Empty;

            if (StringUtil.EqualsAny(service, ControllerServiceGroups.NvmeControllerServices))
            {
                device.TransportKind     = StorageTransportKind.Nvme;
                device.Controller.Family = StorageControllerFamily.StorNvme;
                device.Controller.Kind   = ControllerKindNames.NvmePci;

                if (string.IsNullOrWhiteSpace(device.Controller.Class))
                {
                    device.Controller.Class = ControllerClassNames.ScsiAdapter;
                }

                return;
            }

            if (StringUtil.EqualsAny(service, ControllerServiceGroups.IntelRstControllerServices))
            {
                device.TransportKind     = StorageTransportKind.Raid;
                device.Controller.Family = StorageControllerFamily.IntelRst;
                device.Controller.Kind   = ControllerKindNames.Raid;
                return;
            }

            if (StringUtil.EqualsAny(service, ControllerServiceNames.IaVroc))
            {
                device.TransportKind     = StorageTransportKind.Raid;
                device.Controller.Family = StorageControllerFamily.IntelVroc;
                device.Controller.Kind   = ControllerKindNames.Raid;
                return;
            }

            if (StringUtil.EqualsAny(service, ControllerServiceGroups.UsbMassStorageServices))
            {
                device.TransportKind = StorageTransportKind.Usb;

                device.Controller.Family = service.Equals(ControllerServiceNames.UaspStor, StringComparison.OrdinalIgnoreCase)
                    ? StorageControllerFamily.UaspStor
                    : (service.Equals(ControllerServiceNames.AsusStpt, StringComparison.OrdinalIgnoreCase)
                        ? StorageControllerFamily.AsusBridge
                        : StorageControllerFamily.UsbStor);

                if (string.IsNullOrWhiteSpace(device.Controller.Class))
                {
                    device.Controller.Class = ControllerClassNames.Usb;
                }

                return;
            }

            if (StringUtil.EqualsAny(service, ControllerServiceGroups.SdControllerServices))
            {
                device.TransportKind     = StorageTransportKind.Sd;
                device.Controller.Family = StorageControllerFamily.RealtekSd;
                return;
            }

            if (StringUtil.StartsWithAny(service, ControllerServiceGroups.SasServicePrefixes) || StringUtil.EqualsAny(service, ControllerServiceNames.MegaSas))
            {
                device.TransportKind = StorageTransportKind.Raid;

                device.Controller.Family = service.Equals(ControllerServiceNames.MegaSas, StringComparison.OrdinalIgnoreCase)
                    ? StorageControllerFamily.MegaRaid
                    : StorageControllerFamily.LsiSas;

                device.Controller.Kind = ControllerKindNames.Raid;
                return;
            }

            if (StringUtil.EqualsAny(service, ControllerServiceGroups.AhciControllerServices))
            {
                device.TransportKind     = StorageTransportKind.Ahci;
                device.Controller.Family = StorageControllerFamily.Ahci;
                return;
            }

            if (StringUtil.EqualsAny(service, ControllerServiceGroups.AmdSataControllerServices))
            {
                device.TransportKind     = StorageTransportKind.Ahci;
                device.Controller.Family = StorageControllerFamily.AmdSata;
                return;
            }

            if (controllerClass.Equals(ControllerClassNames.Usb, StringComparison.OrdinalIgnoreCase))
            {
                device.TransportKind     = StorageTransportKind.Usb;
                device.Controller.Family = StorageControllerFamily.Generic;
                return;
            }

            if (controllerClass.Equals(ControllerClassNames.Sas, StringComparison.OrdinalIgnoreCase))
            {
                device.TransportKind     = StorageTransportKind.Sas;
                device.Controller.Family = StorageControllerFamily.LsiSas;
                return;
            }

            if (controllerClass.Equals(ControllerClassNames.Raid, StringComparison.OrdinalIgnoreCase))
            {
                device.TransportKind     = StorageTransportKind.Raid;
                device.Controller.Family = StorageControllerFamily.Generic;
                device.Controller.Kind   = ControllerKindNames.Raid;
                return;
            }

            if (controllerClass.Equals(ControllerClassNames.ScsiAdapter, StringComparison.OrdinalIgnoreCase))
            {
                device.TransportKind     = StorageTransportKind.Scsi;
                device.Controller.Family = StorageControllerFamily.Generic;
            }
        }

        private static void ApplyDeviceFilters(StorageDevice device)
        {
            string display = device.DisplayName ?? string.Empty;
            if (display.StartsWith(StorageDetectionFilter.Drobo5D, StringComparison.OrdinalIgnoreCase))
            {
                device.IsFiltered = true;
                device.FilterReason = "Known SMART-incompatible USB bridge.";
                return;
            }

            if (display.StartsWith(StorageDetectionFilter.VirtualDisk, StringComparison.OrdinalIgnoreCase))
            {
                device.IsFiltered = true;
                device.FilterReason = "Virtual disk filtered.";
                device.Controller.Family = StorageControllerFamily.VirtualDisk;
                device.TransportKind = StorageTransportKind.Virtual;
            }
        }

        private static void SelectProbeStrategy(StorageDevice device)
        {
            string service = device.Controller.Service ?? string.Empty;

            //Intel RST hybrid controllers (iaStorAC, iaStorAVC, iaStorVD) manage both NVMe and SATA devices.
            //SATA devices behind these controllers need the RAID probe path (CSMI/ATA), not NVMe pass-through.
            if (StringUtil.EqualsAny(service,
                    ControllerServiceNames.IaStorAC,
                    ControllerServiceNames.IaStorAVC,
                    ControllerServiceNames.IaStorVD))
            {
                device.ProbeStrategy = device.BusType == StorageBusType.Sata
                    ? ProbeStrategy.RaidProbe
                    : ProbeStrategy.PciNvmeProbe;
                return;
            }

            if (StringUtil.EqualsAny(service,
                    ControllerServiceNames.StorNvme,
                    ControllerServiceNames.Nvme,
                    ControllerServiceNames.Nvme2K,
                    ControllerServiceNames.IaNvme,
                    ControllerServiceNames.IaVroc,
                    ControllerServiceNames.MtInvme,
                    ControllerServiceNames.SecNvme))
            {
                device.ProbeStrategy = ProbeStrategy.PciNvmeProbe;
                return;
            }

            if (StringUtil.EqualsAny(service, ControllerServiceGroups.UsbMassStorageServices) || string.Equals(device.Controller.Class, ControllerClassNames.Usb, StringComparison.OrdinalIgnoreCase))
            {
                device.ProbeStrategy = ProbeStrategy.UsbProbe;
                return;
            }

            if (StringUtil.EqualsAny(service, ControllerServiceGroups.SdControllerServices))
            {
                device.ProbeStrategy = ProbeStrategy.SdMmcProbe;
                return;
            }

            if (StringUtil.StartsWithAny(service, ControllerServiceGroups.SasServicePrefixes)
             || StringUtil.EqualsAny(service,
                    ControllerServiceNames.MegaSas,
                    ControllerServiceNames.IaStorA,
                    ControllerServiceNames.IaStorAV,
                    ControllerServiceNames.IaVroc))
            {
                device.ProbeStrategy = ProbeStrategy.RaidProbe;
                return;
            }

            device.ProbeStrategy = ProbeStrategy.GenericStorageProbe;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return StringUtil.FirstNonEmpty(values);
        }

        private void AttachStandardStorageProperties(StorageDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.DevicePath))
            {
                return;
            }

            SafeFileHandle handle = _ioControl.OpenDevice(
                device.DevicePath,
                IoAccess.GenericRead,
                IoShare.ReadWrite,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            if (handle == null || handle.IsInvalid)
            {
                return;
            }

            using (handle)
            {
                if (_ioControl.TryGetStorageDeviceDescriptor(handle, out var descriptor))
                {
                    device.VendorName      = FirstNonEmpty(descriptor.VendorID, device.VendorName, string.Empty);
                    device.ProductName     = FirstNonEmpty(descriptor.ProductID, device.ProductName, string.Empty);
                    device.ProductRevision = FirstNonEmpty(descriptor.ProductRevision, device.ProductRevision, string.Empty);
                    device.SerialNumber    = FirstNonEmpty(descriptor.SerialNumber, device.SerialNumber, string.Empty);
                    device.BusType         = descriptor.BusType;
                    device.IsRemovable     = descriptor.RemovableMedia;
                }

                if (_ioControl.TryGetStorageAdapterDescriptor(handle, out var adapterDescriptor) && device.BusType == StorageBusType.Unknown)
                {
                    device.BusType = adapterDescriptor.BusType;
                }

                if (_ioControl.TryGetScsiAddress(handle, out var scsiAddress))
                {
                    device.Scsi.PortNumber = scsiAddress.PortNumber;
                    device.Scsi.PathID     = scsiAddress.PathID;
                    device.Scsi.TargetID   = scsiAddress.TargetID;
                    device.Scsi.Lun        = scsiAddress.Lun;
                }

                if (_ioControl.TryGetSmartVersion(handle, out var smartVersionInfo))
                {
                    device.SupportsSmart   = true;
                    device.SmartVersionRaw = smartVersionInfo.Capabilities;
                }

                if (_ioControl.TryGetStorageDeviceNumber(handle, out var deviceNumberInfo))
                {
                    device.StorageDeviceNumber = deviceNumberInfo.DeviceNumber;
                }

                if (_ioControl.TryGetDriveGeometryEx(handle, out var geometryInfo))
                {
                    device.DiskSizeBytes = geometryInfo.DiskSize;
                }

                if (_ioControl.TryGetPredictFailure(handle, out var predictFailureInfo))
                {
                    device.PredictsFailure          = predictFailureInfo.PredictsFailure;
                    device.PredictFailureVendorData = predictFailureInfo.VendorSpecificData ?? [];
                }

                if (_ioControl.TryGetSffDiskDeviceProtocol(handle, out var protocolType))
                {
                    device.SdProtocolType = protocolType;

                    if (protocolType == StorageProtocolType.MultiMediaCard)
                    {
                        device.SdProtocolName = StorageTextConstants.Mmc;

                        if (device.TransportKind == StorageTransportKind.Sd)
                        {
                            device.TransportKind = StorageTransportKind.Mmc;
                        }
                    }
                    else if (protocolType == StorageProtocolType.SecureDigital)
                    {
                        device.SdProtocolName = StorageTextConstants.Sd;
                    }
                }
            }
        }

        #endregion
    }
}
