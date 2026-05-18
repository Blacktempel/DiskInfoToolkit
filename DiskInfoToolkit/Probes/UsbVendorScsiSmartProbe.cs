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
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Probes
{
    public static class UsbVendorScsiSmartProbe
    {
        #region Fields

        private const byte SmartCommand = 0xB0;

        private const byte SmartReadDataSubcommand = 0xD0;

        private const byte SmartReadThresholdSubcommand = 0xD1;

        private const byte SmartCylinderLow = 0x4F;

        private const byte SmartCylinderHigh = 0xC2;

        private const int SmartSectorBytes = 512;

        #endregion

        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
        {
            return TryPopulateData(device, ioControl, false);
        }

        public static bool TryRefreshSmartData(StorageDevice device, IStorageIoControl ioControl)
        {
            return TryPopulateData(device, ioControl, true);
        }

        #endregion

        #region Private

        private static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl, bool refreshOnly)
        {
            if (device == null || ioControl == null || string.IsNullOrWhiteSpace(device.DevicePath))
            {
                return false;
            }

            var flavor = GetFlavor(device);
            if (flavor == UsbSmartFlavor.None)
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
                byte[] smartData = null;
                byte[] smartThresholds = null;

                bool ok = TryReadPage(ioControl, handle, flavor, SmartReadDataSubcommand, out smartData)
                       && (refreshOnly || TryReadPage(ioControl, handle, flavor, SmartReadThresholdSubcommand, out smartThresholds));

                if (!ok)
                {
                    return false;
                }

                var cachedThresholds = device.ProbePlan != null ? device.ProbePlan.CachedSmartThresholds : null;

                var attributes = SmartProbe.ParseSmartPages(smartData, smartThresholds, cachedThresholds);
                if (attributes.Count == 0)
                {
                    return false;
                }

                device.SupportsSmart = true;
                device.SmartAttributes = attributes;

                if (!refreshOnly && device.ProbePlan != null)
                {
                    device.ProbePlan.RecordSmartThresholds(attributes);
                }

                ProbeTraceRecorder.Add(device, "USB bridge SMART: vendor-specific SCSI SMART succeeded for " + (device.Usb.BridgeFamily ?? "unknown") + ".");

                return true;
            }
        }

        private static UsbSmartFlavor GetFlavor(StorageDevice device)
        {
            var family = StringUtil.TrimStorageString(device.Usb.BridgeFamily);
            if (family.Equals(UsbBridgeFamilyNames.Sunplus, StringComparison.OrdinalIgnoreCase))
            {
                return UsbSmartFlavor.Sunplus;
            }

            if (family.Equals(UsbBridgeFamilyNames.IoData, StringComparison.OrdinalIgnoreCase))
            {
                return UsbSmartFlavor.IoData;
            }

            if (family.Equals(UsbBridgeFamilyNames.Logitec, StringComparison.OrdinalIgnoreCase))
            {
                return UsbSmartFlavor.Logitec;
            }

            if (family.Equals(UsbBridgeFamilyNames.Prolific, StringComparison.OrdinalIgnoreCase))
            {
                return UsbSmartFlavor.Prolific;
            }

            if (family.Equals(UsbBridgeFamilyNames.JMicron, StringComparison.OrdinalIgnoreCase))
            {
                return UsbSmartFlavor.JMicron;
            }

            if (family.Equals(UsbBridgeFamilyNames.Cypress, StringComparison.OrdinalIgnoreCase))
            {
                return UsbSmartFlavor.Cypress;
            }

            if (family.Equals(UsbBridgeFamilyNames.Asmedia, StringComparison.OrdinalIgnoreCase))
            {
                return UsbSmartFlavor.AsmediaSat;
            }

            return UsbSmartFlavor.None;
        }

        private static bool TryReadPage(IStorageIoControl ioControl, SafeFileHandle handle, UsbSmartFlavor flavor, byte subcommand, out byte[] page)
        {
            return TryReadPage(ioControl, handle, flavor, subcommand, 0xA0, out page)
                || TryReadPage(ioControl, handle, flavor, subcommand, 0xB0, out page);
        }

        private static bool TryReadPage(IStorageIoControl ioControl, SafeFileHandle handle, UsbSmartFlavor flavor, byte subcommand, byte target, out byte[] page)
        {
            page = null;

            var request = SCSI_PASS_THROUGH_WITH_BUFFERS.CreateDefault();

            request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            request.Spt.CdbLength = (byte)GetCdbLength(flavor);
            request.Spt.SenseInfoLength = 24;
            request.Spt.DataIn = 1;
            request.Spt.DataTransferLength = SmartSectorBytes;
            request.Spt.TimeOutValue = 2;
            request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.DataBuf)).ToInt32();
            request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.SenseBuf)).ToInt32();
            request.Spt.Cdb = new byte[16];

            BuildCdb(request.Spt.Cdb, flavor, subcommand, target);

            var buffer = StructureHelper.GetBytes(request);
            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<SCSI_PASS_THROUGH_WITH_BUFFERS>(buffer);

            page = new byte[SmartSectorBytes];
            Buffer.BlockCopy(response.DataBuf, 0, page, 0, page.Length);

            return HasAnyNonZero(page);
        }

        private static int GetCdbLength(UsbSmartFlavor flavor)
        {
            switch (flavor)
            {
                case UsbSmartFlavor.Logitec:
                    return 10;
                case UsbSmartFlavor.Prolific:
                case UsbSmartFlavor.Cypress:
                    return 16;
                default:
                    return 12;
            }
        }

        private static void BuildCdb(byte[] cdb, UsbSmartFlavor flavor, byte subcommand, byte target)
        {
            switch (flavor)
            {
                case UsbSmartFlavor.Sunplus:
                    cdb[0] = 0xF8;
                    cdb[1] = 0x00;
                    cdb[2] = 0x22;
                    cdb[3] = 0x10;
                    cdb[4] = 0x01;
                    cdb[5] = subcommand;
                    cdb[6] = 0x01;
                    cdb[7] = 0x00;
                    cdb[8] = SmartCylinderLow;
                    cdb[9] = SmartCylinderHigh;
                    cdb[10] = target;
                    cdb[11] = SmartCommand;
                    break;
                case UsbSmartFlavor.IoData:
                    cdb[0] = 0xE3;
                    cdb[1] = 0x00;
                    cdb[2] = subcommand;
                    cdb[3] = 0x00;
                    cdb[4] = 0x00;
                    cdb[5] = SmartCylinderLow;
                    cdb[6] = SmartCylinderHigh;
                    cdb[7] = target;
                    cdb[8] = SmartCommand;
                    cdb[9] = 0x00;
                    cdb[10] = 0x00;
                    cdb[11] = 0x00;
                    break;
                case UsbSmartFlavor.Logitec:
                    cdb[0] = 0xE0;
                    cdb[1] = 0x00;
                    cdb[2] = subcommand;
                    cdb[3] = 0x00;
                    cdb[4] = 0x00;
                    cdb[5] = SmartCylinderLow;
                    cdb[6] = SmartCylinderHigh;
                    cdb[7] = target;
                    cdb[8] = SmartCommand;
                    cdb[9] = 0x4C;
                    break;
                case UsbSmartFlavor.Prolific:
                    cdb[0] = 0xD8;
                    cdb[1] = 0x15;
                    cdb[2] = 0x00;
                    cdb[3] = subcommand;
                    cdb[4] = 0x06;
                    cdb[5] = 0x7B;
                    cdb[6] = 0x00;
                    cdb[7] = 0x00;
                    cdb[8] = 0x02;
                    cdb[9] = 0x00;
                    cdb[10] = 0x01;
                    cdb[11] = 0x00;
                    cdb[12] = SmartCylinderLow;
                    cdb[13] = SmartCylinderHigh;
                    cdb[14] = target;
                    cdb[15] = SmartCommand;
                    break;
                case UsbSmartFlavor.JMicron:
                    cdb[0] = 0xDF;
                    cdb[1] = 0x10;
                    cdb[2] = 0x00;
                    cdb[3] = 0x02;
                    cdb[4] = 0x00;
                    cdb[5] = subcommand;
                    cdb[6] = 0x01;
                    cdb[7] = 0x01;
                    cdb[8] = SmartCylinderLow;
                    cdb[9] = SmartCylinderHigh;
                    cdb[10] = target;
                    cdb[11] = SmartCommand;
                    break;
                case UsbSmartFlavor.Cypress:
                    cdb[0] = 0x24;
                    cdb[1] = 0x24;
                    cdb[2] = 0x00;
                    cdb[3] = 0xBE;
                    cdb[4] = 0x01;
                    cdb[5] = 0x00;
                    cdb[6] = subcommand;
                    cdb[7] = 0x00;
                    cdb[8] = 0x00;
                    cdb[9] = SmartCylinderLow;
                    cdb[10] = SmartCylinderHigh;
                    cdb[11] = target;
                    cdb[12] = SmartCommand;
                    cdb[13] = 0x00;
                    cdb[14] = 0x00;
                    cdb[15] = 0x00;
                    break;
                case UsbSmartFlavor.AsmediaSat:
                    cdb[0] = 0xA1;
                    cdb[1] = (byte)((0x0E << 1) | 0x00);
                    cdb[2] = (byte)((1 << 3) | (1 << 2) | 2);
                    cdb[3] = subcommand;
                    cdb[4] = 1;
                    cdb[5] = 1;
                    cdb[6] = SmartCylinderLow;
                    cdb[7] = SmartCylinderHigh;
                    cdb[8] = target;
                    cdb[9] = SmartCommand;
                    break;
            }
        }

        private static bool HasAnyNonZero(byte[] buffer)
        {
            if (buffer == null)
            {
                return false;
            }

            for (int i = 0; i < buffer.Length; ++i)
            {
                if (buffer[i] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private enum UsbSmartFlavor
        {
            None,
            Sunplus,
            IoData,
            Logitec,
            Prolific,
            JMicron,
            Cypress,
            AsmediaSat
        }

        #endregion
    }
}
