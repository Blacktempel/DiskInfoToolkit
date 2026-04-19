/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Probes;
using DiskInfoToolkit.Vendors;

namespace DiskInfoToolkit.Core
{
    public static class StorageProbeDispatcher
    {
        #region Public

        public static void Probe(StorageDevice device, IStorageIoControl ioControl, OptionalVendorBackendSet vendorBackends)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            ProbeTraceRecorder.Add(device, $"Probe start: strategy={device.ProbeStrategy}, service={device.Controller.Service}, class={device.Controller.Class}");

            //The main probe dispatcher method
            //This routes to different probe sequences based on the device's ProbeStrategy,
            //which is determined by initial heuristics in the controller/device classification phase
            switch (device.ProbeStrategy)
            {
                case ProbeStrategy.PciNvmeProbe:
                    ProbeNvmeLikeDevice(device, ioControl);
                    break;
                case ProbeStrategy.UsbProbe:
                    ProbeUsbLikeDevice(device, ioControl);
                    break;
                case ProbeStrategy.SdMmcProbe:
                    ProbeSdMmcLikeDevice(device, ioControl);
                    break;
                case ProbeStrategy.RaidProbe:
                    ProbeRaidLikeDevice(device, ioControl, vendorBackends);
                    break;
                default:
                    ProbeGenericDevice(device, ioControl);
                    break;
            }

            //After probing, apply "glue" logic to finalize the device state based on the data collected and the probe paths taken
            ProbeGlueLogic.FinalizeDevice(device);
        }

        #endregion

        #region Private

        private static void ProbeGenericDevice(StorageDevice device, IStorageIoControl ioControl)
        {
            ProbeTraceRecorder.Add(device, "Generic path: ATA identify / SMART -> SAT identify / SMART -> SCSI inquiry / capacity.");

            //Try standard ATA identify and SMART as these can provide the most detailed information if they work
            bool ataIdentifyOk = StandardAtaProbe.TryPopulateIdentifyData(device, ioControl);
            if (ataIdentifyOk)
            {
                ProbeTraceRecorder.Add(device, "Generic path: ATA identify succeeded.");
            }

            //Try SMART probing even if the device doesn't report SMART support,
            //as some devices have non-standard implementations that can still be accessed with the right commands
            bool ataSmartOk = SmartProbe.TryPopulateSmartData(device, ioControl);
            if (ataSmartOk)
            {
                ProbeTraceRecorder.Add(device, "Generic path: ATA SMART succeeded.");
            }

            //Exit if we got sufficient data from the ATA phase
            if (HasSufficientGenericData(device))
            {
                ProbeTraceRecorder.Add(device, "Generic path: ATA phase produced sufficient data.");
                return;
            }

            //Try SAT over SCSI identify and SMART which can work on some devices that don't support
            //standard ATA commands but do support the SAT command set over SCSI
            bool satIdentifyOk = ScsiSatProbe.TryPopulateIdentifyData(device, ioControl);
            if (satIdentifyOk)
            {
                ProbeTraceRecorder.Add(device, "Generic path: SAT identify succeeded.");
            }

            //Try SAT SMART if the device doesn't support SMART but might support it through SAT
            bool satSmartOk = ScsiSatProbe.TryPopulateSmartData(device, ioControl);
            if (satSmartOk)
            {
                ProbeTraceRecorder.Add(device, "Generic path: SAT SMART succeeded.");
            }

            //Exit if we got sufficient data from the SAT phase
            if (HasSufficientGenericData(device))
            {
                ProbeTraceRecorder.Add(device, "Generic path: SAT phase produced sufficient data.");
                return;
            }

            //Try generic SCSI inquiry and capacity, as some devices might support basic SCSI commands to get
            //at least some identity and capacity data even if they don't support ATA or SAT command sets
            bool inquiryOk = ScsiInquiryProbe.TryPopulateData(device, ioControl);
            if (inquiryOk)
            {
                ProbeTraceRecorder.Add(device, "Generic path: SCSI inquiry fallback succeeded.");
            }

            //Try populating capacity data through SCSI commands
            bool capacityOk = ScsiCapacityProbe.TryPopulateData(device, ioControl);
            if (capacityOk)
            {
                ProbeTraceRecorder.Add(device, "Generic path: SCSI capacity fallback succeeded.");
            }

            //Check if we got sufficient data from the SCSI phase
            if (HasSufficientGenericData(device))
            {
                ProbeTraceRecorder.Add(device, "Generic path: SCSI fallback phase produced sufficient data.");
            }
            else
            {
                ProbeTraceRecorder.Add(device, "Generic path: all implemented generic probe attempts finished without sufficient data.");
            }
        }

        private static void ProbeNvmeLikeDevice(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device.BusType == StorageBusType.Unknown)
            {
                device.BusType = StorageBusType.Nvme;
            }

            ProbeTraceRecorder.Add(device, "NVMe path: VROC pass-through -> standard query -> Intel pass-through -> Intel RAID miniport -> Intel signature sweep -> Samsung SCSI fallback -> generic SCSI fallback.");

            //We have to probe VROC pass-through first
            if (device.Controller.Family == StorageControllerFamily.IntelVroc)
            {
                if (VrocNvmePassThroughProbe.TryPopulateData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "NVMe path: Intel VROC pass-through succeeded.");
                    return;
                }
            }

            //Probe for Intel RAID/VROC miniport data, which can work on some RAID configurations where NVMe pass-through does not work
            if (device.Controller.Family == StorageControllerFamily.IntelRst || device.Controller.Family == StorageControllerFamily.IntelVroc)
            {
                //First try the normal miniport probing method
                if (IntelRaidMiniportProbe.TryPopulateData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "NVMe path: Intel RAID/VROC miniport succeeded.");
                    return;
                }

                //If that fails, try the more aggressive signature sweep method, which tries multiple signatures to account for different RAID configurations and driver versions
                //This can succeed in cases where the normal method fails due to signature mismatches
                if (IntelRaidMiniportProbe.TryPopulateDataWithSignatureSweep(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "NVMe path: Intel RAID/VROC multi-signature sweep succeeded.");
                    return;
                }
            }

            //Probe for standard NVMe data, as this is the most likely to be supported on NVMe devices and can provide data if it works
            if (NvmeProbe.TryPopulateStandardNvmeData(device, ioControl))
            {
                ProbeTraceRecorder.Add(device, "NVMe path: standard NVMe query succeeded.");
                return;
            }

            //Probe for Intel NVMe pass-through data
            if (IntelNvmeProbe.TryPopulateIntelNvmeData(device, ioControl))
            {
                ProbeTraceRecorder.Add(device, "NVMe path: Intel NVMe pass-through succeeded.");
                return;
            }

            //Try samsung NVMe SCSI fallback
            if (SamsungNvmeScsiProbe.TryPopulateData(device, ioControl))
            {
                ProbeTraceRecorder.Add(device, "NVMe path: Samsung SCSI fallback succeeded.");
                return;
            }

            bool inquiryOk = ScsiInquiryProbe.TryPopulateData(device, ioControl);
            bool capacityOk = ScsiCapacityProbe.TryPopulateData(device, ioControl);

            if (inquiryOk)
            {
                ProbeTraceRecorder.Add(device, "NVMe path: generic SCSI inquiry fallback succeeded.");
            }
            if (capacityOk)
            {
                ProbeTraceRecorder.Add(device, "NVMe path: generic SCSI capacity fallback succeeded.");
            }

            //Check if we got any useful data from anything
            if (!HasUsefulIdentity(device) && !HasSufficientGenericData(device))
            {
                ProbeTraceRecorder.Add(device, "NVMe path: all implemented NVMe probe attempts failed.");
            }
        }

        private static void ProbeUsbLikeDevice(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device.BusType == StorageBusType.Unknown)
            {
                device.BusType = StorageBusType.Usb;
            }

            ProbeTraceRecorder.Add(device, "USB path: classify bridge -> bridge-specific probing -> generic mass-storage fallbacks.");

            UsbBridgeClassifier.Apply(device);

            if (ControllerServiceProbeRules.ShouldFilterNoSmartSupport(device))
            {
                ProbeTraceRecorder.Add(device, "USB path: known no-SMART profile matched.");
                if (string.IsNullOrWhiteSpace(device.FilterReason))
                {
                    device.FilterReason = "Known no-SMART USB storage profile.";
                }
                return;
            }

            if (UsbBridgeProbe.TryPopulateData(device, ioControl))
            {
                ProbeTraceRecorder.Add(device, "USB path: bridge-specific probing succeeded.");
                return;
            }

            bool any = false;

            //Verify that the device is a USB mass storage device before attempting mass-storage specific methods
            if (ControllerServiceProbeRules.IsUsbMassStorageService(device.Controller.Service)
                || string.Equals(device.Controller.Class, ControllerClassNames.Usb, StringComparison.OrdinalIgnoreCase)
                || device.Usb.IsMassStorageLike)
            {
                //Try a couple of USB specific identify/SMART methods
                if (UsbMassStorageProbe.TryPopulateData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "USB path: generic USB mass-storage fallback succeeded.");
                    any = true;
                }

                //Now try some vendor-specific USB SMART methods for devices that don't support standard SMART
                //as some USB bridges require vendor-specific commands to access SMART data
                if (!device.SupportsSmart && UsbVendorScsiSmartProbe.TryPopulateData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "USB path: vendor-specific USB SMART fallback succeeded.");
                    any = true;
                }

                //Try some SAT over SCSI methods for USB mass storage devices that don't support SMART or have very limited identity data
                if (!HasSufficientUsbData(device) && ScsiSatProbe.TryPopulateIdentifyData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "USB path: SAT identify fallback succeeded.");
                    any = true;
                }

                //Try SAT SMART if the device doesn't support SMART but might support it through SAT
                if (!device.SupportsSmart && ScsiSatProbe.TryPopulateSmartData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "USB path: SAT SMART fallback succeeded.");
                    any = true;
                }

                //Try various ATA identify/SMART methods for USB mass storage devices that don't have sufficient data
                if (!HasUsefulIdentity(device) && StandardAtaProbe.TryPopulateIdentifyData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "USB path: ATA identify fallback succeeded.");
                    any = true;
                }

                //Try SMART probing for devices that don't report SMART support but might have it accessible through USB-specific commands
                if (!device.SupportsSmart && SmartProbe.TryPopulateSmartData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "USB path: ATA SMART fallback succeeded.");
                    any = true;
                }
            }

            //Try generic SCSI inquiry/capacity for USB devices that don't have sufficient data,
            //as some USB bridges might allow basic SCSI commands to get at least some identity and capacity data even if SMART is not supported
            if (!HasUsefulIdentity(device) && ScsiInquiryProbe.TryPopulateData(device, ioControl))
            {
                ProbeTraceRecorder.Add(device, "USB path: final SCSI inquiry fallback succeeded.");
                any = true;
            }

            //Only attempt the SCSI capacity fallback if we don't already have size data,
            //as some USB bridges might return incorrect capacity data that could be worse than having no data at all
            if ((device.DiskSizeBytes.GetValueOrDefault() == 0 && device.Scsi.LastLogicalBlockAddress == 0) && ScsiCapacityProbe.TryPopulateData(device, ioControl))
            {
                ProbeTraceRecorder.Add(device, "USB path: final SCSI capacity fallback succeeded.");
                any = true;
            }

            if (any)
            {
                //After all probing is done, apply any heuristics we can based on the data we got to try to classify the USB bridge family and get more info out of it
                UsbBridgeClassifier.ApplyInquiryHeuristics(device);
                ProbeTraceRecorder.Add(device, "USB path: generic USB/UASP fallback sequence completed with usable data.");
            }
            else
            {
                ProbeTraceRecorder.Add(device, "USB path: all implemented USB bridge probe attempts finished without sufficient data.");
            }
        }

        private static void ProbeSdMmcLikeDevice(StorageDevice device, IStorageIoControl ioControl)
        {
            ProbeTraceRecorder.Add(device, "SD/MMC path: protocol query already performed in standard properties.");

            if (device.SdProtocolType.HasValue)
            {
                if (device.SdProtocolType.Value == StorageProtocolType.MultiMediaCard)
                {
                    device.TransportKind = StorageTransportKind.Mmc;
                    device.SdProtocolName = StorageTextConstants.Mmc;
                    ProbeTraceRecorder.Add(device, "SD/MMC path: protocol resolved to MMC.");
                }
                else if (device.SdProtocolType.Value == StorageProtocolType.SecureDigital)
                {
                    device.TransportKind = StorageTransportKind.Sd;
                    device.SdProtocolName = StorageTextConstants.Sd;
                    ProbeTraceRecorder.Add(device, "SD/MMC path: protocol resolved to SD.");
                }
            }
        }

        private static void ProbeRaidLikeDevice(StorageDevice device, IStorageIoControl ioControl, OptionalVendorBackendSet vendorBackends)
        {
            ProbeTraceRecorder.Add(device, "RAID path: vendor backends -> Intel/VROC -> CSMI -> composite port probing -> controller-specific ATA/SAT/SCSI fallbacks -> generic SCSI fallback.");

            //First try Mega Raid
            if (device.Controller.Family == StorageControllerFamily.MegaRaid && vendorBackends.MegaRaidBackend.IsAvailable)
            {
                if (vendorBackends.MegaRaidBackend.TryProbe(device))
                {
                    ProbeTraceRecorder.Add(device, "RAID path: external MegaRAID backend succeeded.");
                    return;
                }
            }

            //Next try HighPoint RocketRaid
            if (device.Controller.Family == StorageControllerFamily.RocketRaid && vendorBackends.HighPointBackend.IsAvailable)
            {
                if (vendorBackends.HighPointBackend.TryProbe(device))
                {
                    ProbeTraceRecorder.Add(device, "RAID path: external HighPoint backend succeeded.");
                    return;
                }
            }

            bool preferCsmiAtaPath = ShouldPreferCsmiAtaRaidPath(device);

            //For synthetic CSMI SATA members we must prefer the CSMI ATA/STP path before any Intel NVMe-style RAID probing
            if (preferCsmiAtaPath)
            {
                if (TryCsmiRaidControllerPaths(device, ioControl))
                {
                    return;
                }

                if (TryIntelRaidControllerPaths(device, ioControl))
                {
                    return;
                }
            }
            else
            {
                //Try various RAID-specific paths for Intel RAID/VROC controllers
                if (TryIntelRaidControllerPaths(device, ioControl))
                {
                    return;
                }

                //Try CSMI paths, which can work on a variety of RAID controllers that support the CSMI interface
                if (TryCsmiRaidControllerPaths(device, ioControl))
                {
                    return;
                }
            }

            //Try composite SCSI port paths, which can work on some RAID controllers that expose RAID metadata through SCSI port queries
            if (TryCompositeRaidPortPaths(device, ioControl))
            {
                return;
            }

            //Try any controller-specific fallbacks based on the controller's characteristics
            if (TryRaidControllerSpecificFallbacks(device, ioControl))
            {
                return;
            }

            //Try the Samsung NVMe SCSI fallback, which can work on some RAID configurations that present as NVMe devices but support a special SCSI command set for identity and SMART data
            if (SamsungNvmeScsiProbe.TryPopulateData(device, ioControl))
            {
                ProbeTraceRecorder.Add(device, "RAID path: Samsung NVMe SCSI fallback succeeded.");
                return;
            }

            //Try generic SCSI inquiry and capacity fallback for RAID controllers
            bool inquiryOk = ScsiInquiryProbe.TryPopulateData(device, ioControl);
            if (inquiryOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: SCSI inquiry fallback succeeded.");
            }

            bool capacityOk = ScsiCapacityProbe.TryPopulateData(device, ioControl);
            if (capacityOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: SCSI capacity fallback succeeded.");
            }

            //For RAID controllers we consider the fallback successful if we get any useful identity data
            if (ControllerServiceProbeRules.IsScsiRaidController(device) && HasUsefulIdentity(device))
            {
                ProbeTraceRecorder.Add(device, "RAID path: SCSI RAID controller identity is sufficient after inquiry/capacity fallback.");
                return;
            }

            bool vendorFallbackOk = false;

            //If we don't have sufficient RAID data at this point, we can try vendor-specific fallbacks for certain controllers
            if (!HasSufficientRaidData(device) && ControllerServiceProbeRules.IsScsiRaidController(device))
            {
                if (vendorBackends.HighPointBackend.IsAvailable && vendorBackends.HighPointBackend.TryProbe(device))
                {
                    ProbeTraceRecorder.Add(device, "RAID path: opportunistic HighPoint backend fallback succeeded.");
                    vendorFallbackOk = true;
                }
                else if (vendorBackends.MegaRaidBackend.IsAvailable && vendorBackends.MegaRaidBackend.TryProbe(device))
                {
                    ProbeTraceRecorder.Add(device, "RAID path: opportunistic MegaRAID backend fallback succeeded.");
                    vendorFallbackOk = true;
                }
            }

            if (vendorFallbackOk || HasSufficientRaidData(device))
            {
                ProbeTraceRecorder.Add(device, "RAID path: final RAID fallback sequence produced sufficient data.");
                return;
            }

            ProbeTraceRecorder.Add(device, "RAID path: all implemented RAID probe attempts finished without a definitive controller-specific success.");
        }

        private static bool ShouldPreferCsmiAtaRaidPath(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            bool isIntelController = device.Controller.Family == StorageControllerFamily.IntelRst
                                  || device.Controller.Family == StorageControllerFamily.IntelVroc;

            if (!isIntelController)
            {
                return false;
            }

            bool hasConcreteCsmiMemberIdentity = device.Csmi.PortIdentifier.HasValue
                                              && device.Csmi.AttachedPhyIdentifier.HasValue;

            if (!hasConcreteCsmiMemberIdentity)
            {
                return false;
            }

            bool isAtaTarget = (device.Csmi.TargetProtocol.GetValueOrDefault() & CsmiProbe.CsmiProtocolSata) != 0
                            || (device.Csmi.TargetProtocol.GetValueOrDefault() & CsmiProbe.CsmiProtocolStp) != 0
                            || device.BusType == StorageBusType.Sata
                            || device.BusType == StorageBusType.Ata
                            || device.TransportKind == StorageTransportKind.Ata;

            return isAtaTarget;
        }

        private static bool TryIntelRaidControllerPaths(StorageDevice device, IStorageIoControl ioControl)
        {
            //If the controller is not an Intel RST or VROC family we skip these paths entirely
            //to save time and avoid unnecessary attempts that are unlikely to succeed
            if (device.Controller.Family != StorageControllerFamily.IntelRst && device.Controller.Family != StorageControllerFamily.IntelVroc)
            {
                return false;
            }

            //Try Intel VROC pass-through on VROC controllers
            if (device.Controller.Family == StorageControllerFamily.IntelVroc)
            {
                bool vrocPassThroughOk = VrocNvmePassThroughProbe.TryPopulateData(device, ioControl);
                if (vrocPassThroughOk)
                {
                    ProbeTraceRecorder.Add(device, "RAID path: Intel VROC pass-through succeeded.");
                    return true;
                }
            }

            //Try Intel RAID/VROC miniport probing, which can work on some RAID configurations where NVMe pass-through does not work
            bool intelMiniportOk = IntelRaidMiniportProbe.TryPopulateData(device, ioControl);
            if (intelMiniportOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: Intel RAID/VROC miniport succeeded.");
                return true;
            }

            //If the normal miniport probing fails, try the more aggressive signature sweep method
            //which tries multiple signatures to account for different RAID configurations and driver versions
            bool intelMiniportSweepOk = IntelRaidMiniportProbe.TryPopulateDataWithSignatureSweep(device, ioControl);
            if (intelMiniportSweepOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: Intel RAID/VROC multi-signature miniport sweep succeeded.");
                return true;
            }

            //Try Intel NVMe pass-through, which can work on some RAID configurations that don't support the
            //full miniport interface but still allow some NVMe commands to get identity and SMART data
            bool intelNvmeOk = IntelNvmeProbe.TryPopulateIntelNvmeData(device, ioControl);
            if (intelNvmeOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: Intel NVMe pass-through succeeded.");
                return true;
            }

            //Try standard NVMe protocol queries, which can work on some RAID configurations
            bool standardNvmeOk = NvmeProbe.TryPopulateStandardNvmeData(device, ioControl);
            if (standardNvmeOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: standard NVMe storage query succeeded.");
                return true;
            }

            return false;
        }

        private static bool TryCsmiRaidControllerPaths(StorageDevice device, IStorageIoControl ioControl)
        {
            //Try CSMI driver info, which can give us details about the RAID controller and its capabilities if it works
            bool driverInfoOk = CsmiProbe.TryPopulateDriverInfo(device, ioControl);
            if (driverInfoOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: CSMI driver info succeeded.");
            }

            //Try CSMI topology data, which can give us information about the physical layout of the RAID array and the devices connected to it
            bool topologyOk = CsmiProbe.TryPopulateTopologyData(device, ioControl);
            if (topologyOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: CSMI topology info succeeded.");
            }

            //Try CSMI ATA pass-through, which can work on some RAID controllers that support passing ATA commands
            //through the CSMI interface to get identity and SMART data from the drives in the array
            bool ataOk = CsmiProbe.TryPopulateAtaData(device, ioControl);
            if (ataOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: CSMI ATA/STP probing succeeded.");
                if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                {
                    device.Controller.Kind = ControllerKindNames.Csmi;
                }
                return true;
            }

            //Try CSMI composite port probing, which can work on some RAID controllers that expose RAID metadata through SCSI port queries on the CSMI interface
            bool compositeOk = CsmiPortCompositeProbe.TryPopulateData(device, ioControl);
            if (compositeOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: CSMI composite SCSI port probing succeeded.");
                return true;
            }

            if (driverInfoOk && string.IsNullOrWhiteSpace(device.Controller.Kind))
            {
                device.Controller.Kind = ControllerKindNames.Csmi;
            }

            return HasUsefulIdentity(device) || device.SupportsSmart;
        }

        private static bool TryCompositeRaidPortPaths(StorageDevice device, IStorageIoControl ioControl)
        {
            //Try master port composite probing, which can work on some RAID controllers that expose RAID metadata
            //through a special "master" SCSI port with composite data for all drives in the array
            bool masterPortCompositeOk = RaidMasterPortCompositeProbe.TryPopulateData(device, ioControl);
            if (masterPortCompositeOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: master composite SCSI port probing succeeded.");
                if (HasSufficientRaidData(device))
                {
                    return true;
                }
            }

            //Try regular composite port probing, which can work on some RAID controllers that expose RAID metadata
            //through a special SCSI port with composite data for all drives in the array
            bool compositePortOk = RaidPortCompositeProbe.TryPopulateData(device, ioControl);
            if (compositePortOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: composite SCSI port probing succeeded.");
                if (HasSufficientRaidData(device))
                {
                    return true;
                }
            }

            //Try SCSI miniport port probing, which can work on some RAID controllers that expose RAID metadata
            //through SCSI port queries on the miniport interface rather than the regular storage port interface
            bool miniportPortOk = ScsiMiniportPortProbe.TryPopulateData(device, ioControl);
            if (miniportPortOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: SCSI miniport port probing succeeded.");
                return true;
            }

            //Try regular SCSI port probing, which can work on some RAID controllers that allow SCSI commands
            //to be sent to the RAID port to get identity and SMART data for the drives in the array
            bool raidPortScsiOk = RaidScsiPortProbe.TryPopulateData(device, ioControl);
            if (raidPortScsiOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: SCSI port inquiry/capacity probing succeeded.");
                if (HasSufficientRaidData(device))
                {
                    return true;
                }
            }

            //Try SAT over SCSI port probing, which can work on some RAID controllers that support the SAT command
            //set over their RAID SCSI port to get identity and SMART data from the drives in the array
            bool raidSatPortOk = RaidSatPortProbe.TryPopulateData(device, ioControl);
            if (raidSatPortOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: SAT over SCSI port probing succeeded.");
                return true;
            }

            //Try regular SCSI port probing, which can work on some RAID controllers that allow SCSI commands
            //to be sent to the RAID port to get identity and SMART data for the drives in the array
            bool raidControllerPortOk = RaidControllerPortProbe.TryPopulateData(device, ioControl);
            if (raidControllerPortOk)
            {
                ProbeTraceRecorder.Add(device, "RAID path: controller port descriptor probing succeeded.");
                if (HasSufficientRaidData(device))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryRaidControllerSpecificFallbacks(StorageDevice device, IStorageIoControl ioControl)
        {
            //Checks if the controller matches known characteristics for certain RAID controllers
            bool ataLikeController = ControllerServiceProbeRules.IsAtaLikeScsiController(device);

            //Checks if the controller matches known characteristics for SCSI RAID controllers
            bool scsiRaidController = ControllerServiceProbeRules.IsScsiRaidController(device);

            if (ataLikeController)
            {
                ProbeTraceRecorder.Add(device, "RAID path: controller matches ATA-like SCSI fallback rules.");

                //Try SAT over SCSI identify and SMART, which can work on some RAID controllers that
                //don't support standard ATA commands but do support the SAT command set over SCSI
                if (ScsiSatProbe.TryPopulateIdentifyData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "RAID path: SAT identify succeeded.");
                }

                //Try SAT SMART if the device doesn't support SMART but might support it through SAT,
                //which can work on some RAID controllers that present as ATA-like SCSI devices
                if (ScsiSatProbe.TryPopulateSmartData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "RAID path: SAT SMART succeeded.");
                }

                //Try standard ATA identify and SMART as some RAID controllers that present as
                //ATA-like SCSI devices might still support these commands through the right pass-through method
                if (StandardAtaProbe.TryPopulateIdentifyData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "RAID path: ATA identify succeeded.");
                }

                //Try SMART probing for devices that don't report SMART support but might have it accessible
                //through ATA commands, which can work on some RAID controllers that present as ATA-like SCSI devices
                if (SmartProbe.TryPopulateSmartData(device, ioControl))
                {
                    ProbeTraceRecorder.Add(device, "RAID path: ATA SMART succeeded.");
                }

                return HasSufficientRaidData(device);
            }

            if (scsiRaidController)
            {
                ProbeTraceRecorder.Add(device, "RAID path: controller matches SCSI RAID fallback rules.");

                bool inquiryOk = ScsiInquiryProbe.TryPopulateData(device, ioControl);
                if (inquiryOk)
                {
                    ProbeTraceRecorder.Add(device, "RAID path: controller-specific SCSI inquiry succeeded.");
                }

                bool capacityOk = ScsiCapacityProbe.TryPopulateData(device, ioControl);
                if (capacityOk)
                {
                    ProbeTraceRecorder.Add(device, "RAID path: controller-specific SCSI capacity succeeded.");
                }

                return HasUsefulIdentity(device);
            }

            return false;
        }

        private static bool HasSufficientGenericData(StorageDevice device)
        {
            if (HasUsefulIdentity(device))
            {
                return true;
            }

            if (device.SupportsSmart)
            {
                return true;
            }

            if (device.DiskSizeBytes.GetValueOrDefault() > 0 || device.Scsi.LastLogicalBlockAddress > 0)
            {
                return true;
            }

            return false;
        }

        private static bool HasSufficientUsbData(StorageDevice device)
        {
            if (HasSufficientGenericData(device))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(device.Scsi.DeviceIdentifier))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(device.Usb.BridgeFamily))
            {
                return true;
            }

            return false;
        }

        private static bool HasUsefulIdentity(StorageDevice device)
        {
            return !string.IsNullOrWhiteSpace(device.ProductName)
                || !string.IsNullOrWhiteSpace(device.SerialNumber)
                || !string.IsNullOrWhiteSpace(device.VendorName)
                || (device.Nvme.IdentifyControllerData != null && device.Nvme.IdentifyControllerData.Length > 0)
                || device.SmartAttributes.Count > 0;
        }

        private static bool HasSufficientRaidData(StorageDevice device)
        {
            if (HasUsefulIdentity(device))
            {
                return true;
            }

            if (device.SupportsSmart)
            {
                return true;
            }

            if (device.Scsi.LastLogicalBlockAddress > 0 || device.DiskSizeBytes.GetValueOrDefault() > 0)
            {
                return true;
            }

            return false;
        }

        #endregion
    }
}
