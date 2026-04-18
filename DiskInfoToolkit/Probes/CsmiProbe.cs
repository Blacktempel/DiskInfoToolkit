/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.Probes
{
    public static class CsmiProbe
    {
        #region Fields

        private static readonly byte[] CsmiAllSignature = Encoding.ASCII.GetBytes("CSMIALL");

        private static readonly byte[] CsmiSasSignature = Encoding.ASCII.GetBytes("CSMISAS");

        private const uint CsmiGetDriverInfo = 1;

        private const uint CsmiGetPhyInfo = 20;

        private const uint CsmiStpPassThrough = 25;

        private const uint CsmiTimeoutSeconds = 60;

        private const byte SmartCommand = 0xB0;

        private const byte SmartReadDataSubcommand = 0xD0;

        private const byte SmartReadThresholdSubcommand = 0xD1;

        private const byte SmartCylinderLow = 0x4F;

        private const byte SmartCylinderHigh = 0xC2;

        private const byte IdentifyDeviceCommand = 0xEC;

        private const uint CsmiLinkRateNegotiated = 0x00;

        private const uint CsmiStpRead = 0x00000001;

        private const uint CsmiStpUnspecified = 0x00000004;

        private const uint CsmiStpPio = 0x00000010;

        private const int SectorBytes = 512;

        private const byte CsmiProtocolSata = 0x01;

        private const byte CsmiProtocolStp = 0x04;

        #endregion

        #region Public

        public static bool TryPopulateDriverInfo(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || string.IsNullOrWhiteSpace(device.DevicePath))
            {
                return false;
            }

            SafeFileHandle handle = ioControl.OpenDevice(
                device.DevicePath,
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
                return TryPopulateDriverInfoFromHandle(device, ioControl, handle);
            }
        }

        public static bool TryPopulateDriverInfoFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            var buffer = CSMI_SAS_DRIVER_INFO_BUFFER.CreateDefault();
            if (!SendCsmiMiniport(handle, ioControl, CsmiGetDriverInfo, CsmiAllSignature, ref buffer))
            {
                return false;
            }

            var name        = StringUtil.CleanAscii(buffer.Information.szName);
            var description = StringUtil.CleanAscii(buffer.Information.szDescription);

            var revision = new Version(buffer.Information.usMajorRevision, buffer.Information.usMinorRevision).ToString();

            if (!string.IsNullOrWhiteSpace(name))
            {
                if (string.IsNullOrWhiteSpace(device.Controller.Service))
                {
                    device.Controller.Service = name;
                }

                if (string.IsNullOrWhiteSpace(device.Controller.Name))
                {
                    device.Controller.Name = description;
                }

                if (string.IsNullOrWhiteSpace(device.Controller.Kind))
                {
                    device.Controller.Kind = ControllerKindNames.Csmi;
                }

                if (string.IsNullOrWhiteSpace(device.ProductRevision))
                {
                    device.ProductRevision = revision;
                }
            }

            return !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(description);
        }

        public static bool TryPopulateTopologyData(StorageDevice device, IStorageIoControl ioControl)
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
                return TryPopulateTopologyDataFromHandle(device, ioControl, handle);
            }
        }

        public static bool TryPopulateTopologyDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            if (!TryGetPhyInfoFromHandle(ioControl, handle, out var phyInfoBuffer))
            {
                return false;
            }

            int count = phyInfoBuffer.Information.bNumberOfPhys;
            device.Csmi.PhyCount = count;

            if (count <= 0)
            {
                return false;
            }

            int selectedIndex = -1;
            bool hasRequestedPhy = HasRequestedPhy(device);
            for (int i = 0; i < count && i < phyInfoBuffer.Information.Phy.Length; ++i)
            {
                var candidate = phyInfoBuffer.Information.Phy[i];
                if (!MatchesRequestedPhy(device, candidate))
                {
                    continue;
                }

                if (IsAtaCapablePhy(candidate))
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex < 0)
            {
                if (hasRequestedPhy)
                {
                    return false;
                }

                selectedIndex = 0;
            }

            var phy = phyInfoBuffer.Information.Phy[selectedIndex];
            ApplyPhyInfo(device, phy);

            if ((phy.Attached.bTargetPortProtocol & CsmiProtocolStp) != 0)
            {
                device.TransportKind = StorageTransportKind.Ata;
            }
            else if ((phy.Attached.bTargetPortProtocol & CsmiProtocolSata) != 0)
            {
                device.TransportKind = StorageTransportKind.Ata;
            }
            else
            {
                device.TransportKind = StorageTransportKind.Sas;
            }

            if (string.IsNullOrWhiteSpace(device.Controller.Kind))
            {
                device.Controller.Kind = ControllerKindNames.Csmi;
            }

            return true;
        }

        public static bool TryPopulateAtaData(StorageDevice device, IStorageIoControl ioControl)
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
                return TryPopulateAtaDataFromHandle(device, ioControl, handle);
            }
        }

        public static bool TryPopulateAtaDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            if (!TryGetPhyInfoFromHandle(ioControl, handle, out var phyInfoBuffer))
            {
                return false;
            }

            int count = phyInfoBuffer.Information.bNumberOfPhys;
            if (count <= 0)
            {
                return false;
            }

            bool hasRequestedPhy = HasRequestedPhy(device);
            for (int i = 0; i < count && i < phyInfoBuffer.Information.Phy.Length; ++i)
            {
                var phy = phyInfoBuffer.Information.Phy[i];
                if (!MatchesRequestedPhy(device, phy))
                {
                    continue;
                }

                if (!IsAtaCapablePhy(phy))
                {
                    if (!hasRequestedPhy)
                    {
                        continue;
                    }

                    return false;
                }

                if (SendAtaCommand(handle, ioControl, phy, IdentifyDeviceCommand, 0x00, 0x00, SectorBytes, out var identifyData))
                {
                    ApplyAtaIdentify(device, identifyData);

                    byte[] smartData = null;
                    byte[] smartThresholds = null;

                    bool smartOk = SendAtaCommand(handle, ioControl, phy, SmartCommand, SmartReadDataSubcommand, 0x00, SectorBytes, out smartData)
                        && SendAtaCommand(handle, ioControl, phy, SmartCommand, SmartReadThresholdSubcommand, 0x00, SectorBytes, out smartThresholds);

                    if (!smartOk)
                    {
                        if (SendAtaCommand(handle, ioControl, phy, SmartCommand, 0xD8, 0x00, 0, out smartData))
                        {
                            smartOk = SendAtaCommand(handle, ioControl, phy, SmartCommand, SmartReadDataSubcommand, 0x00, SectorBytes, out smartData)
                                && SendAtaCommand(handle, ioControl, phy, SmartCommand, SmartReadThresholdSubcommand, 0x00, SectorBytes, out smartThresholds);
                        }
                    }

                    if (smartOk)
                    {
                        device.SupportsSmart = true;
                        device.SmartAttributes = ParseSmartPages(smartData, smartThresholds);
                    }

                    device.Controller.Kind = string.IsNullOrWhiteSpace(device.Controller.Kind) ? "CSMI" : device.Controller.Kind;
                    return true;
                }
            }

            return false;
        }

        public static void ApplyAtaIdentify(StorageDevice device, byte[] identifyData)
        {
            if (identifyData == null || identifyData.Length < 512)
            {
                return;
            }

            var serial   = AtaStringDecoder.ReadWordSwappedString(identifyData, 10, 10);
            var firmware = AtaStringDecoder.ReadWordSwappedString(identifyData, 23,  4);
            var model    = AtaStringDecoder.ReadWordSwappedString(identifyData, 27, 20);

            if (!string.IsNullOrWhiteSpace(model))
            {
                device.ProductName = model;
                if (string.IsNullOrWhiteSpace(device.DisplayName) || device.DisplayName.Equals(StorageTextConstants.UnknownDisk, StringComparison.OrdinalIgnoreCase))
                {
                    device.DisplayName = model;
                }
            }

            if (!string.IsNullOrWhiteSpace(serial))
            {
                device.SerialNumber = serial;
            }

            if (!string.IsNullOrWhiteSpace(firmware))
            {
                device.ProductRevision = firmware;
            }
        }

        public static List<SmartAttributeEntry> ParseSmartPages(byte[] dataPage, byte[] thresholdPage)
        {
            var result = new List<SmartAttributeEntry>();
            var thresholds = new Dictionary<byte, byte>();

            if (thresholdPage != null && thresholdPage.Length >= 362)
            {
                for (int offset = 2; offset + 12 <= 362; offset += 12)
                {
                    byte id = thresholdPage[offset];
                    if (id != 0)
                    {
                        thresholds[id] = thresholdPage[offset + 1];
                    }
                }
            }

            if (dataPage == null || dataPage.Length < 362)
            {
                return result;
            }

            for (int offset = 2; offset + 12 <= 362; offset += 12)
            {
                byte id = dataPage[offset];
                if (id == 0)
                {
                    continue;
                }

                var entry = new SmartAttributeEntry();
                entry.ID = id;
                entry.StatusFlags = BitConverter.ToUInt16(dataPage, offset + 1);
                entry.CurrentValue = dataPage[offset + 3];
                entry.WorstValue = dataPage[offset + 4];
                entry.RawValue = ReadUInt48(dataPage, offset + 5);

                byte threshold;
                if (thresholds.TryGetValue(id, out threshold))
                {
                    entry.ThresholdValue = threshold;
                }

                result.Add(entry);
            }

            return result;
        }

        #endregion

        #region Internal

        internal static bool TryGetPhyInfoFromHandle(IStorageIoControl ioControl, SafeFileHandle handle, out CSMI_SAS_PHY_INFO_BUFFER phyInfoBuffer)
        {
            phyInfoBuffer = CSMI_SAS_PHY_INFO_BUFFER.CreateDefault();
            if (ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            return SendCsmiMiniport(handle, ioControl, CsmiGetPhyInfo, CsmiSasSignature, ref phyInfoBuffer);
        }

        internal static bool TryReadAtaIdentifyFromHandle(IStorageIoControl ioControl, SafeFileHandle handle, CSMI_SAS_PHY_ENTITY phy, out byte[] identifyData)
        {
            identifyData = null;
            if (ioControl == null || handle == null || handle.IsInvalid || !IsAtaCapablePhy(phy))
            {
                return false;
            }

            return SendAtaCommand(handle, ioControl, phy, IdentifyDeviceCommand, 0x00, 0x00, SectorBytes, out identifyData);
        }

        internal static bool IsAtaCapablePhy(CSMI_SAS_PHY_ENTITY phy)
        {
            return (phy.Attached.bTargetPortProtocol & CsmiProtocolSata) != 0
                || (phy.Attached.bTargetPortProtocol & CsmiProtocolStp) != 0;
        }

        internal static void ApplyPhyInfo(StorageDevice device, CSMI_SAS_PHY_ENTITY phy)
        {
            if (device == null)
            {
                return;
            }

            device.Csmi.PortIdentifier = phy.bPortIdentifier;
            device.Csmi.AttachedPhyIdentifier = phy.Attached.bPhyIdentifier;
            device.Csmi.NegotiatedLinkRate = phy.bNegotiatedLinkRate;
            device.Csmi.NegotiatedLinkRateName = GetLinkRateName(phy.bNegotiatedLinkRate);
            device.Csmi.AttachedSasAddress = FormatSasAddress(phy.Attached.bSASAddress);
            device.Csmi.TargetProtocol = phy.Attached.bTargetPortProtocol;
        }

        #endregion

        #region Private

        private static bool HasRequestedPhy(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            return device.Csmi.PortIdentifier.HasValue
                || device.Csmi.AttachedPhyIdentifier.HasValue
                || !string.IsNullOrWhiteSpace(device.Csmi.AttachedSasAddress)
                || device.Scsi.TargetID.HasValue;
        }

        private static bool MatchesRequestedPhy(StorageDevice device, CSMI_SAS_PHY_ENTITY phy)
        {
            if (device == null)
            {
                return true;
            }

            if (device.Csmi.PortIdentifier.HasValue && phy.bPortIdentifier != device.Csmi.PortIdentifier.Value)
            {
                return false;
            }

            if (device.Csmi.AttachedPhyIdentifier.HasValue && phy.Attached.bPhyIdentifier != device.Csmi.AttachedPhyIdentifier.Value)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(device.Csmi.AttachedSasAddress))
            {
                var candidateAddress = FormatSasAddress(phy.Attached.bSASAddress);
                if (!string.Equals(candidateAddress, device.Csmi.AttachedSasAddress, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!device.Csmi.PortIdentifier.HasValue && device.Scsi.TargetID.HasValue && phy.bPortIdentifier != device.Scsi.TargetID.Value)
            {
                return false;
            }

            return true;
        }

        private static string FormatSasAddress(byte[] sasAddress)
        {
            if (sasAddress == null || sasAddress.Length == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(sasAddress.Length * 2);
            for (int i = 0; i < sasAddress.Length; ++i)
            {
                sb.Append(sasAddress[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static string GetLinkRateName(byte linkRate)
        {
            switch (linkRate)
            {
                case 0x08:
                    return "1.5 Gbps";
                case 0x09:
                    return "3.0 Gbps";
                case 0x0A:
                    return "6.0 Gbps";
                case 0x0B:
                    return "12.0 Gbps";
                default:
                    return string.Empty;
            }
        }

        private static bool SendAtaCommand(SafeFileHandle handle, IStorageIoControl ioControl, CSMI_SAS_PHY_ENTITY phy, byte main, byte sub, byte param, int dataLength, out byte[] data)
        {
            data = null;

            int totalSize = Marshal.SizeOf<CSMI_SAS_STP_PASSTHRU_BUFFER>() + Math.Max(0, dataLength) - 1;
            if (totalSize < Marshal.SizeOf<CSMI_SAS_STP_PASSTHRU_BUFFER>())
            {
                totalSize = Marshal.SizeOf<CSMI_SAS_STP_PASSTHRU_BUFFER>();
            }

            var buffer = new byte[totalSize];

            var request = CSMI_SAS_STP_PASSTHRU_BUFFER.CreateDefault();
            request.Parameters.bPhyIdentifier = phy.Attached.bPhyIdentifier;
            request.Parameters.bPortIdentifier = phy.bPortIdentifier;

            Array.Copy(phy.Attached.bSASAddress, request.Parameters.bDestinationSASAddress, Math.Min(phy.Attached.bSASAddress.Length, request.Parameters.bDestinationSASAddress.Length));

            request.Parameters.bConnectionRate = (byte)CsmiLinkRateNegotiated;
            request.Parameters.uFlags = main == 0xEF ? CsmiStpUnspecified : (CsmiStpPio | CsmiStpRead);
            request.Parameters.uDataLength = (uint)Math.Max(0, dataLength);

            request.Parameters.bCommandFIS[0] = 0x27;
            request.Parameters.bCommandFIS[1] = 0x80;
            request.Parameters.bCommandFIS[2] = main;
            request.Parameters.bCommandFIS[3] = sub;
            request.Parameters.bCommandFIS[4] = 0;
            request.Parameters.bCommandFIS[5] = main == SmartCommand ? SmartCylinderLow : (byte)0;
            request.Parameters.bCommandFIS[6] = main == SmartCommand ? SmartCylinderHigh : (byte)0;
            request.Parameters.bCommandFIS[7] = 0xA0;
            request.Parameters.bCommandFIS[12] = param;

            var requestBytes = StructureHelper.GetBytes(request);
            Buffer.BlockCopy(requestBytes, 0, buffer, 0, requestBytes.Length);

            var headerOnly = StructureHelper.FromBytes<CSMI_SAS_STP_PASSTHRU_BUFFER>(buffer);
            if (!SendCsmiMiniport(handle, ioControl, CsmiStpPassThrough, CsmiSasSignature, ref headerOnly, buffer))
            {
                return false;
            }

            if (dataLength <= 0)
            {
                data = [];
                return true;
            }

            int dataOffset = Marshal.OffsetOf<CSMI_SAS_STP_PASSTHRU_BUFFER>(nameof(CSMI_SAS_STP_PASSTHRU_BUFFER.bDataBuffer)).ToInt32();
            if (dataOffset + dataLength > buffer.Length)
            {
                return false;
            }

            data = new byte[dataLength];
            Buffer.BlockCopy(buffer, dataOffset, data, 0, data.Length);

            return true;
        }

        private static ulong ReadUInt48(byte[] data, int offset)
        {
            ulong value = 0;

            for (int i = 0; i < 6; ++i)
            {
                value |= ((ulong)data[offset + i]) << (8 * i);
            }

            return value;
        }

        private static bool SendCsmiMiniport<T>(SafeFileHandle handle, IStorageIoControl ioControl, uint controlCode, byte[] signature, ref T request)
            where T : struct
        {
            var buffer = StructureHelper.GetBytes(request);

            bool ok = SendCsmiMiniport(handle, ioControl, controlCode, signature, ref request, buffer);
            if (ok)
            {
                request = StructureHelper.FromBytes<T>(buffer);
            }

            return ok;
        }

        private static bool SendCsmiMiniport<T>(SafeFileHandle handle, IStorageIoControl ioControl, uint controlCode, byte[] signature, ref T request, byte[] buffer)
            where T : struct
        {
            int totalSize = buffer.Length;

            var header = new SRB_IO_CONTROL();
            header.HeaderLength = (uint)Marshal.SizeOf<SRB_IO_CONTROL>();
            header.Signature = new byte[8];

            Array.Copy(signature, header.Signature, Math.Min(signature.Length, header.Signature.Length));

            header.Timeout = CsmiTimeoutSeconds;
            header.ControlCode = controlCode;
            header.ReturnCode = 0;
            header.Length = (uint)(totalSize - Marshal.SizeOf<SRB_IO_CONTROL>());

            var headerBytes = StructureHelper.GetBytes(header);
            Buffer.BlockCopy(headerBytes, 0, buffer, 0, headerBytes.Length);

            if (!ioControl.TryScsiMiniport(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            request = StructureHelper.FromBytes<T>(buffer);

            SRB_IO_CONTROL resultHeader = StructureHelper.FromBytes<SRB_IO_CONTROL>(buffer);
            return resultHeader.ReturnCode == 0;
        }

        #endregion
    }
}
