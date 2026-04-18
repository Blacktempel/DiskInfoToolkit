/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Models;
using DiskInfoToolkit.Probes;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;

namespace DiskInfoToolkit.Core
{
    internal static class IntelRstSataMemberEnumerator
    {
        #region Public

        public static void Enumerate(List<StorageDevice> devices, IStorageIoControl ioControl, out List<IntelRstSataMember> detectedDevices)
        {
            detectedDevices = new List<IntelRstSataMember>();
            if (devices == null || ioControl == null || devices.Count == 0)
            {
                return;
            }

            var existingMemberKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            //Build a set of existing member keys to avoid duplicates
            foreach (var device in devices)
            {
                if (!ShouldReserveExistingMemberKey(device))
                {
                    continue;
                }

                var existingMemberKey = BuildMemberKey(device.Scsi.PortNumber, device.Scsi.TargetID);
                if (!string.IsNullOrWhiteSpace(existingMemberKey))
                {
                    existingMemberKeys.Add(existingMemberKey);
                }
            }

            //Group devices by controller and SCSI port number so the CSMI topology is queried only once per controller path
            var controllerGroups = devices
                .Where(IsControllerCandidate)
                .GroupBy(BuildControllerGroupKey)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key));

            foreach (var controllerGroup in controllerGroups)
            {
                var controllerDevices = controllerGroup.ToList();
                var representative = controllerDevices.FirstOrDefault();

                if (representative == null || !representative.Scsi.PortNumber.HasValue)
                {
                    continue;
                }

                string scsiPortPath = StoragePathBuilder.BuildScsiPortPath(representative.Scsi.PortNumber.Value);
                SafeFileHandle handle = ioControl.OpenDevice(
                    scsiPortPath,
                    IoAccess.GenericRead | IoAccess.GenericWrite,
                    IoShare.ReadWrite,
                    IoCreation.OpenExisting,
                    IoFlags.Normal);

                if (handle == null || handle.IsInvalid)
                {
                    continue;
                }

                using (handle)
                {
                    //Attempt to retrieve CSMI phy information for the controller
                    //If this fails, we won't be able to synthesize member devices, so we skip it
                    if (!CsmiProbe.TryGetPhyInfoFromHandle(ioControl, handle, out var phyInfoBuffer))
                    {
                        continue;
                    }

                    var members = new List<StorageDevice>();
                    int count = phyInfoBuffer.Information.bNumberOfPhys;

                    for (int i = 0; i < count && i < phyInfoBuffer.Information.Phy.Length; ++i)
                    {
                        var phy = phyInfoBuffer.Information.Phy[i];
                        if (!CsmiProbe.IsAtaCapablePhy(phy))
                        {
                            continue;
                        }

                        var memberKey = BuildMemberKey(representative.Scsi.PortNumber, phy.bPortIdentifier);
                        if (string.IsNullOrWhiteSpace(memberKey) || existingMemberKeys.Contains(memberKey))
                        {
                            continue;
                        }

                        if (!CsmiProbe.TryReadAtaIdentifyFromHandle(ioControl, handle, phy, out var identifyData))
                        {
                            ProbeTraceRecorder.Add(representative, $"Skipping synthetic Intel RST SATA member for CSMI phy {phy.bPortIdentifier} on port {representative.Scsi.PortNumber.Value} because ATA IDENTIFY did not succeed.");
                            continue;
                        }

                        var memberDevice = CreateMemberDevice(representative, phy.bPortIdentifier, scsiPortPath);
                        CsmiProbe.ApplyPhyInfo(memberDevice, phy);
                        CsmiProbe.ApplyAtaIdentify(memberDevice, identifyData);

                        ProbeTraceRecorder.Add(memberDevice, $"Synthetic Intel RST SATA member created from CSMI phy {phy.bPortIdentifier} on port {representative.Scsi.PortNumber.Value} after ATA IDENTIFY validation.");

                        members.Add(memberDevice);
                        existingMemberKeys.Add(memberKey);
                    }

                    if (members.Count <= 0)
                    {
                        continue;
                    }

                    var aggregateVolume = SelectAggregateVolumeCandidate(controllerDevices, members);
                    if (aggregateVolume == null)
                    {
                        ProbeTraceRecorder.Add(representative, $"Intel RST SATA member synthesis found {members.Count} hidden ATA member(s), but no unique aggregate RAID volume candidate could be selected on controller port {representative.Scsi.PortNumber.Value}.");
                    }
                    else
                    {
                        ProbeTraceRecorder.Add(aggregateVolume, $"Intel RST SATA member synthesis associated {members.Count} hidden ATA member(s) with aggregate candidate '{aggregateVolume.DisplayName}'.");
                    }

                    detectedDevices.Add(new IntelRstSataMember
                    {
                        AggregateVolume = aggregateVolume,
                        Volumes = members
                    });
                }
            }
        }

        public static void RemoveAggregateVolumes(List<StorageDevice> devices, List<IntelRstSataMember> detectedRstSataMembers)
        {
            if (devices == null || detectedRstSataMembers?.Count == 0)
            {
                return;
            }

            var devicesToRemove = new HashSet<StorageDevice>();
            foreach (var members in detectedRstSataMembers)
            {
                if (members == null || members.AggregateVolume == null || members.Volumes?.Count <= 1)
                {
                    continue;
                }

                if (!LooksLikeAggregateRaidVolume(members.AggregateVolume, members.Volumes))
                {
                    continue;
                }

                ProbeTraceRecorder.Add(members.AggregateVolume, $"Logical Intel RAID volume removed from result list because {members.Volumes.Count} SATA member disks were synthesized from the CSMI topology.");
                devicesToRemove.Add(members.AggregateVolume);
            }

            devices.RemoveAll(devicesToRemove.Contains);
        }

        #endregion

        #region Private

        private static StorageDevice CreateMemberDevice(StorageDevice representative, byte targetId, string scsiPortPath)
        {
            var device = new StorageDevice();
            device.DeviceDescription = StorageTextConstants.DiskDrive;
            device.DeviceTypeLabel = StorageTextConstants.DiskDrive;
            device.DisplayName = StorageTextConstants.UnknownDisk;
            device.AlternateDevicePath = scsiPortPath;
            device.BusType = StorageBusType.Sata;
            device.TransportKind = StorageTransportKind.Raid;
            device.ProbeStrategy = ProbeStrategy.RaidProbe;
            device.Controller.Service = representative.Controller.Service;
            device.Controller.Class = representative.Controller.Class;
            device.Controller.Kind = representative.Controller.Kind;
            device.Controller.Family = representative.Controller.Family;
            device.Controller.Name = representative.Controller.Name;
            device.Controller.HardwareID = representative.Controller.HardwareID;
            device.Controller.Identifier = representative.Controller.Identifier;
            device.Controller.VendorID = representative.Controller.VendorID;
            device.Controller.DeviceID = representative.Controller.DeviceID;
            device.Controller.Revision = representative.Controller.Revision;
            device.Controller.VendorName = representative.Controller.VendorName;
            device.Controller.DeviceName = representative.Controller.DeviceName;
            device.Controller.IsUsbStyleHardwareID = representative.Controller.IsUsbStyleHardwareID;
            device.Scsi.PortNumber = representative.Scsi.PortNumber;
            device.Scsi.PathID = representative.Scsi.PathID ?? 0;
            device.Scsi.TargetID = targetId;
            device.Scsi.Lun = representative.Scsi.Lun ?? 0;
            device.ParentInstanceID = representative.ParentInstanceID;
            return device;
        }

        private static bool IsControllerCandidate(StorageDevice device)
        {
            if (device == null || !device.Scsi.PortNumber.HasValue)
            {
                return false;
            }

            if (device.Controller.Family != StorageControllerFamily.IntelRst && device.Controller.Family != StorageControllerFamily.IntelVroc)
            {
                return false;
            }

            return device.ProbeStrategy == ProbeStrategy.RaidProbe
                || StringUtil.EqualsAny(
                    device.Controller.Service ?? string.Empty,
                    ControllerServiceGroups.IntelRstControllerServices)
                || StringUtil.EqualsAny(
                    device.Controller.Service ?? string.Empty,
                    ControllerServiceGroups.IntelRaidProbeServices);
        }

        private static bool ShouldReserveExistingMemberKey(StorageDevice device)
        {
            if (device == null || !device.Scsi.PortNumber.HasValue || !device.Scsi.TargetID.HasValue)
            {
                return false;
            }

            if (device.Controller.Family == StorageControllerFamily.IntelRst || device.Controller.Family == StorageControllerFamily.IntelVroc)
            {
                if (UsesRAIDPath(device))
                {
                    return false;
                }

                return !LooksLikeExistingAggregateRaidVolume(device);
            }

            return true;
        }

        private static bool LooksLikeExistingAggregateRaidVolume(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            var combinedName = BuildCombinedDeviceName(device);

            bool hasVolumeHint = HasVolumeHint(combinedName);
            bool hasRaidHint = HasRAIDHint(combinedName);

            return UsesRAIDPath(device) && (hasVolumeHint || hasRaidHint);
        }

        private static bool LooksLikeAggregateRaidVolume(StorageDevice device, List<StorageDevice> members)
        {
            if (device == null || members == null || members.Count <= 1)
            {
                return false;
            }

            return ScoreAggregateRaidVolumeCandidate(device, null) > 0;
        }

        private static StorageDevice SelectAggregateVolumeCandidate(List<StorageDevice> controllerDevices, List<StorageDevice> members)
        {
            if (controllerDevices == null || members == null || members.Count <= 1)
            {
                return null;
            }

            var memberTargets = new HashSet<byte>(members
                .Where(member => member?.Scsi.TargetID.HasValue == true)
                .Select(member => member.Scsi.TargetID.Value));

            var candidates = controllerDevices
                .Select(device => new
                {
                    Device = device,
                    Score = ScoreAggregateRaidVolumeCandidate(device, memberTargets)
                })
                .Where(entry => entry.Score > 0)
                .OrderByDescending(entry => entry.Score)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count > 1 && candidates[0].Score == candidates[1].Score)
            {
                return null;
            }

            return candidates[0].Device;
        }

        private static int ScoreAggregateRaidVolumeCandidate(StorageDevice device, HashSet<byte> memberTargets)
        {
            if (device == null)
            {
                return 0;
            }

            var combinedName = BuildCombinedDeviceName(device);

            bool hasVolumeHint = HasVolumeHint(combinedName);
            bool hasRaidHint = HasRAIDHint(combinedName);

            if (!hasVolumeHint && !hasRaidHint)
            {
                return 0;
            }

            int score = 0;

            if (hasVolumeHint)
            {
                score += 10;
            }

            if (hasRaidHint)
            {
                score += 4;
            }

            if (device.ProbeStrategy == ProbeStrategy.RaidProbe)
            {
                score += 3;
            }

            if (device.TransportKind == StorageTransportKind.Raid)
            {
                score += 2;
            }

            if (device.BusType == StorageBusType.RAID || device.BusType == StorageBusType.Scsi)
            {
                score += 1;
            }

            if (device.BusType == StorageBusType.Nvme || device.TransportKind == StorageTransportKind.Nvme)
            {
                score -= 4;
            }

            return score;
        }

        private static bool HasVolumeHint(string combinedName)
        {
            return combinedName.IndexOf("volume"      , StringComparison.OrdinalIgnoreCase) >= 0
                || combinedName.IndexOf("array"       , StringComparison.OrdinalIgnoreCase) >= 0
                || combinedName.IndexOf("virtual disk", StringComparison.OrdinalIgnoreCase) >= 0
                || combinedName.IndexOf("logical disk", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasRAIDHint(string combinedName)
        {
            return combinedName.IndexOf("raid", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool UsesRAIDPath(StorageDevice device)
        {
            return device.ProbeStrategy == ProbeStrategy.RaidProbe
                || device.TransportKind == StorageTransportKind.Raid
                || device.BusType == StorageBusType.RAID
                || device.BusType == StorageBusType.Scsi;
        }

        private static string BuildCombinedDeviceName(StorageDevice device)
        {
            if (device == null)
            {
                return string.Empty;
            }

            return $"{device.DisplayName} {device.ProductName} {device.DeviceDescription} {device.DeviceTypeLabel}";
        }

        private static string BuildControllerGroupKey(StorageDevice device)
        {
            if (device == null || !device.Scsi.PortNumber.HasValue)
            {
                return string.Empty;
            }

            return StoragePathBuilder.BuildScsiPortPath(device.Scsi.PortNumber.Value);
        }

        private static string BuildMemberKey(byte? portNumber, byte? targetId)
        {
            if (!portNumber.HasValue || !targetId.HasValue)
            {
                return string.Empty;
            }

            return $"{portNumber.Value}|{targetId.Value}";
        }

        #endregion
    }
}
