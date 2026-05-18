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
    public static class ScsiSatProbe
    {
        #region Fields

        private const byte SmartEnableCommand = 0xD8;

        private const byte SmartDisableCommand = 0xD9;

        private const byte SmartCommand = 0xB0;

        private const byte SmartReadDataSubcommand = 0xD0;

        private const byte SmartReadThresholdSubcommand = 0xD1;

        private const byte SmartCylinderLow = 0x4F;

        private const byte SmartCylinderHigh = 0xC2;

        private const byte IdentifyDeviceCommand = 0xEC;

        private const int DataLength = 512;

        #endregion

        #region Public

        public static bool TryPopulateIdentifyData(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || string.IsNullOrWhiteSpace(device.DevicePath))
            {
                return false;
            }

            var flavor = GetFlavor(device);

            if (!TryIdentify(ioControl, device.DevicePath, 0xA0, flavor, out var identifyData)
             && !TryIdentify(ioControl, device.DevicePath, 0xB0, flavor, out identifyData))
            {
                //Fallback: try generic SAT
                if (!TryIdentify(ioControl, device.DevicePath, 0xA0, SatPassThroughFlavor.GenericSat, out identifyData)
                 && !TryIdentify(ioControl, device.DevicePath, 0xB0, SatPassThroughFlavor.GenericSat, out identifyData))
                {
                    return false;
                }
            }

            StandardAtaProbe.ApplyAtaIdentify(device, identifyData);
            return true;
        }

        public static bool TryPopulateSmartData(StorageDevice device, IStorageIoControl ioControl)
        {
            return TryPopulateSmartData(device, ioControl, false);
        }

        public static bool TryRefreshSmartData(StorageDevice device, IStorageIoControl ioControl)
        {
            return TryPopulateSmartData(device, ioControl, true);
        }

        private static bool TryPopulateSmartData(StorageDevice device, IStorageIoControl ioControl, bool refreshOnly)
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
                return TryPopulateSmartDataFromHandle(device, ioControl, handle, refreshOnly);
            }
        }

        public static bool TryPopulateIdentifyDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            var flavor = GetFlavor(device);

            if (!TryIdentifyOnHandle(ioControl, handle, 0xA0, flavor, out var identifyData)
             && !TryIdentifyOnHandle(ioControl, handle, 0xB0, flavor, out identifyData))
            {
                //Fallback: try generic SAT
                if (!TryIdentifyOnHandle(ioControl, handle, 0xA0, SatPassThroughFlavor.GenericSat, out identifyData)
                 && !TryIdentifyOnHandle(ioControl, handle, 0xB0, SatPassThroughFlavor.GenericSat, out identifyData))
                {
                    return false;
                }
            }

            StandardAtaProbe.ApplyAtaIdentify(device, identifyData);
            return true;
        }

        public static bool TryPopulateSmartDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle)
        {
            return TryPopulateSmartDataFromHandle(device, ioControl, handle, false);
        }

        private static bool TryPopulateSmartDataFromHandle(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle, bool refreshOnly)
        {
            if (device == null || ioControl == null || handle == null || handle.IsInvalid)
            {
                return false;
            }

            var flavor = GetFlavor(device);

            bool readThresholds = !refreshOnly;

            byte[] smartData = null;
            byte[] smartThresholds = null;

            byte succeededTarget = 0;
            SatPassThroughFlavor succeededFlavor = flavor;

            bool ok = false;

            if (refreshOnly && device.ProbePlan != null && device.ProbePlan.SatSmartFlavor.HasValue && device.ProbePlan.SatSmartTarget.HasValue)
            {
                succeededFlavor = device.ProbePlan.SatSmartFlavor.Value;
                succeededTarget = device.ProbePlan.SatSmartTarget.Value;

                ok = TrySmartReadPagesOnHandle(ioControl, handle, succeededTarget, succeededFlavor, readThresholds, out smartData, out smartThresholds);
            }
            else if (refreshOnly)
            {
                ok = TrySmartReadPagesOnHandle(ioControl, handle, 0xA0, flavor, readThresholds, out smartData, out smartThresholds);

                if (ok)
                {
                    succeededTarget = 0xA0;
                    succeededFlavor = flavor;
                }
                else if (TrySmartReadPagesOnHandle(ioControl, handle, 0xB0, flavor, readThresholds, out smartData, out smartThresholds))
                {
                    ok = true;

                    succeededTarget = 0xB0;
                    succeededFlavor = flavor;
                }
                else if (TrySmartReadPagesOnHandle(ioControl, handle, 0xA0, SatPassThroughFlavor.GenericSat, readThresholds, out smartData, out smartThresholds))
                {
                    ok = true;

                    succeededTarget = 0xA0;
                    succeededFlavor = SatPassThroughFlavor.GenericSat;
                }
                else if (TrySmartReadPagesOnHandle(ioControl, handle, 0xB0, SatPassThroughFlavor.GenericSat, readThresholds, out smartData, out smartThresholds))
                {
                    ok = true;

                    succeededTarget = 0xB0;
                    succeededFlavor = SatPassThroughFlavor.GenericSat;
                }
            }
            else if (!refreshOnly)
            {
                ok = TrySmartReadPagesOnHandle(ioControl, handle, 0xA0, flavor, readThresholds, out smartData, out smartThresholds);

                if (ok)
                {
                    succeededTarget = 0xA0;
                    succeededFlavor = flavor;
                }
                else if (TrySmartReadPagesOnHandle(ioControl, handle, 0xB0, flavor, readThresholds, out smartData, out smartThresholds))
                {
                    ok = true;

                    succeededTarget = 0xB0;
                    succeededFlavor = flavor;
                }
                else if (TrySmartReadPagesOnHandle(ioControl, handle, 0xA0, SatPassThroughFlavor.GenericSat, readThresholds, out smartData, out smartThresholds))
                {
                    ok = true;

                    succeededTarget = 0xA0;
                    succeededFlavor = SatPassThroughFlavor.GenericSat;
                }
                else if (TrySmartReadPagesOnHandle(ioControl, handle, 0xB0, SatPassThroughFlavor.GenericSat, readThresholds, out smartData, out smartThresholds))
                {
                    ok = true;

                    succeededTarget = 0xB0;
                    succeededFlavor = SatPassThroughFlavor.GenericSat;
                }

                if (!ok)
                {
                    ok = TryEnableAndReadSmartPagesOnHandle(ioControl, handle, 0xA0, flavor, readThresholds, out smartData, out smartThresholds);

                    if (ok)
                    {
                        succeededTarget = 0xA0;
                        succeededFlavor = flavor;
                    }
                    else if (TryEnableAndReadSmartPagesOnHandle(ioControl, handle, 0xB0, flavor, readThresholds, out smartData, out smartThresholds))
                    {
                        ok = true;

                        succeededTarget = 0xB0;
                        succeededFlavor = flavor;
                    }
                    else if (TryEnableAndReadSmartPagesOnHandle(ioControl, handle, 0xA0, SatPassThroughFlavor.GenericSat, readThresholds, out smartData, out smartThresholds))
                    {
                        ok = true;

                        succeededTarget = 0xA0;
                        succeededFlavor = SatPassThroughFlavor.GenericSat;
                    }
                    else if (TryEnableAndReadSmartPagesOnHandle(ioControl, handle, 0xB0, SatPassThroughFlavor.GenericSat, readThresholds, out smartData, out smartThresholds))
                    {
                        ok = true;

                        succeededTarget = 0xB0;
                        succeededFlavor = SatPassThroughFlavor.GenericSat;
                    }
                }
            }

            if (!ok)
            {
                return false;
            }

            var cachedThresholds = device.ProbePlan != null ? device.ProbePlan.CachedSmartThresholds : null;

            var attributes = SmartProbe.ParseSmartPages(smartData, smartThresholds, cachedThresholds);
            if (attributes == null || attributes.Count == 0)
            {
                return false;
            }

            device.SupportsSmart = true;
            device.SmartAttributes = attributes;

            if (device.ProbePlan != null)
            {
                device.ProbePlan.SatSmartTarget = succeededTarget;
                device.ProbePlan.SatSmartFlavor = succeededFlavor;

                if (!refreshOnly)
                {
                    device.ProbePlan.RecordSmartThresholds(attributes);
                }
            }

            return true;
        }

        #endregion

        #region Private

        private static bool TrySmartReadPagesOnHandle(IStorageIoControl ioControl, SafeFileHandle handle, byte target, SatPassThroughFlavor flavor, bool readThresholds, out byte[] smartData, out byte[] smartThresholds)
        {
            smartData = null;
            smartThresholds = null;

            return TrySmartReadOnHandle(ioControl, handle, target, flavor, SmartReadDataSubcommand, out smartData)
                && (!readThresholds || TrySmartReadOnHandle(ioControl, handle, target, flavor, SmartReadThresholdSubcommand, out smartThresholds));
        }

        private static bool TryEnableAndReadSmartPagesOnHandle(IStorageIoControl ioControl, SafeFileHandle handle, byte target, SatPassThroughFlavor flavor, bool readThresholds, out byte[] smartData, out byte[] smartThresholds)
        {
            smartData = null;
            smartThresholds = null;

            return TryEnableSmartOnHandle(ioControl, handle, target, flavor)
                && TrySmartReadPagesOnHandle(ioControl, handle, target, flavor, readThresholds, out smartData, out smartThresholds);
        }

        private static bool TryIdentify(IStorageIoControl ioControl, string devicePath, byte target, SatPassThroughFlavor flavor, out byte[] identifyData)
        {
            identifyData = null;

            SafeFileHandle handle = ioControl.OpenDevice(
                devicePath,
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
                var request = SCSI_PASS_THROUGH_WITH_BUFFERS.CreateDefault();
                request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
                request.Spt.SenseInfoLength = 24;
                request.Spt.DataIn = 1;
                request.Spt.DataTransferLength = DataLength;
                request.Spt.TimeOutValue = 2;
                request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.DataBuf)).ToInt32();
                request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.SenseBuf)).ToInt32();
                BuildIdentifyCdb(ref request.Spt, target, flavor);

                var buffer = StructureHelper.GetBytes(request);

                if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
                {
                    return false;
                }

                var response = StructureHelper.FromBytes<SCSI_PASS_THROUGH_WITH_BUFFERS>(buffer);

                identifyData = new byte[DataLength];
                Buffer.BlockCopy(response.DataBuf, 0, identifyData, 0, identifyData.Length);

                return NvmeProbeUtil.HasAnyNonZeroByte(identifyData);
            }
        }

        private static bool TryIdentifyOnHandle(IStorageIoControl ioControl, SafeFileHandle handle, byte target, SatPassThroughFlavor flavor, out byte[] identifyData)
        {
            identifyData = null;

            var request = SCSI_PASS_THROUGH_WITH_BUFFERS.CreateDefault();
            request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            request.Spt.SenseInfoLength = 24;
            request.Spt.DataIn = 1;
            request.Spt.DataTransferLength = DataLength;
            request.Spt.TimeOutValue = 2;
            request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.DataBuf)).ToInt32();
            request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.SenseBuf)).ToInt32();
            BuildIdentifyCdb(ref request.Spt, target, flavor);

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<SCSI_PASS_THROUGH_WITH_BUFFERS>(buffer);

            identifyData = new byte[DataLength];
            Buffer.BlockCopy(response.DataBuf, 0, identifyData, 0, identifyData.Length);

            return NvmeProbeUtil.HasAnyNonZeroByte(identifyData);
        }

        private static bool TrySmartReadOnHandle(IStorageIoControl ioControl, SafeFileHandle handle, byte target, SatPassThroughFlavor flavor, byte feature, out byte[] data)
        {
            data = null;

            var request = SCSI_PASS_THROUGH_WITH_BUFFERS.CreateDefault();
            request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            request.Spt.SenseInfoLength = 24;
            request.Spt.DataIn = 1;
            request.Spt.DataTransferLength = DataLength;
            request.Spt.TimeOutValue = 2;
            request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.DataBuf)).ToInt32();
            request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.SenseBuf)).ToInt32();
            BuildSmartCdb(ref request.Spt, target, flavor, feature);

            var buffer = StructureHelper.GetBytes(request);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            var response = StructureHelper.FromBytes<SCSI_PASS_THROUGH_WITH_BUFFERS>(buffer);

            data = new byte[DataLength];
            Buffer.BlockCopy(response.DataBuf, 0, data, 0, data.Length);

            return NvmeProbeUtil.HasAnyNonZeroByte(data);
        }

        private static bool TryEnableSmartOnHandle(IStorageIoControl ioControl, SafeFileHandle handle, byte target, SatPassThroughFlavor flavor)
        {
            var request = SCSI_PASS_THROUGH_WITH_BUFFERS.CreateDefault();
            request.Spt.Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH>();
            request.Spt.SenseInfoLength = 24;
            request.Spt.DataIn = 0;
            request.Spt.DataTransferLength = 0;
            request.Spt.TimeOutValue = 2;
            request.Spt.DataBufferOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.DataBuf)).ToInt32();
            request.Spt.SenseInfoOffset = (uint)Marshal.OffsetOf<SCSI_PASS_THROUGH_WITH_BUFFERS>(nameof(SCSI_PASS_THROUGH_WITH_BUFFERS.SenseBuf)).ToInt32();
            BuildSmartEnableCdb(ref request.Spt, target, SmartEnableCommand, flavor);

            var buffer = StructureHelper.GetBytes(request);

            return ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned);
        }

        private static void BuildSmartEnableCdb(ref SCSI_PASS_THROUGH spt, byte target, byte command, SatPassThroughFlavor flavor)
        {
            Array.Clear(spt.Cdb, 0, spt.Cdb.Length);

            switch (flavor)
            {
                case SatPassThroughFlavor.Asm1352R:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xA1;
                    spt.Cdb[1] = (0xE << 1);
                    spt.Cdb[2] = ((1 << 3) | (1 << 2) | 2);
                    spt.Cdb[3] = command;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = 0x01;
                    spt.Cdb[6] = SmartCylinderLow;
                    spt.Cdb[7] = SmartCylinderHigh;
                    spt.Cdb[8] = target;
                    spt.Cdb[9] = SmartCommand;
                    break;
                case SatPassThroughFlavor.Realtek9220DP:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xA1;
                    spt.Cdb[1] = (3 << 1);
                    spt.Cdb[2] = ((1 << 3) | (1 << 2) | 2);
                    spt.Cdb[3] = command;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = 0x01;
                    spt.Cdb[6] = SmartCylinderLow;
                    spt.Cdb[7] = SmartCylinderHigh;
                    spt.Cdb[8] = target;
                    spt.Cdb[9] = SmartCommand;
                    break;
                case SatPassThroughFlavor.Sunplus:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xF8;
                    spt.Cdb[1] = 0;
                    spt.Cdb[2] = 0x22;
                    spt.Cdb[3] = 0x10;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = command;
                    spt.Cdb[6] = 0x01;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = SmartCylinderLow;
                    spt.Cdb[9] = SmartCylinderHigh;
                    spt.Cdb[10] = target;
                    spt.Cdb[11] = SmartCommand;
                    break;
                case SatPassThroughFlavor.IoData:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xE3;
                    spt.Cdb[1] = 0;
                    spt.Cdb[2] = command;
                    spt.Cdb[3] = 0;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = SmartCylinderLow;
                    spt.Cdb[6] = SmartCylinderHigh;
                    spt.Cdb[7] = target;
                    spt.Cdb[8] = SmartCommand;
                    spt.Cdb[9] = 0;
                    spt.Cdb[10] = 0;
                    spt.Cdb[11] = 0;
                    break;
                case SatPassThroughFlavor.Logitech:
                    spt.CdbLength = 10;

                    spt.Cdb[0] = 0xE0;
                    spt.Cdb[1] = 0;
                    spt.Cdb[2] = command;
                    spt.Cdb[3] = 0;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = SmartCylinderLow;
                    spt.Cdb[6] = SmartCylinderHigh;
                    spt.Cdb[7] = target;
                    spt.Cdb[8] = SmartCommand;
                    spt.Cdb[9] = 0x4C;
                    break;
                case SatPassThroughFlavor.Prolific:
                    spt.CdbLength = 16;

                    spt.Cdb[0] = 0xD8;
                    spt.Cdb[1] = 0x15;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = command;
                    spt.Cdb[4] = 0x06;
                    spt.Cdb[5] = 0x7B;
                    spt.Cdb[6] = 0;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = 0x02;
                    spt.Cdb[9] = 0;
                    spt.Cdb[10] = 0x01;
                    spt.Cdb[11] = 0;
                    spt.Cdb[12] = SmartCylinderLow;
                    spt.Cdb[13] = SmartCylinderHigh;
                    spt.Cdb[14] = target;
                    spt.Cdb[15] = SmartCommand;
                    break;
                case SatPassThroughFlavor.JMicron:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xDF;
                    spt.Cdb[1] = 0x10;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = 0x02;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = command;
                    spt.Cdb[6] = 0x01;
                    spt.Cdb[7] = 0x01;
                    spt.Cdb[8] = SmartCylinderLow;
                    spt.Cdb[9] = SmartCylinderHigh;
                    spt.Cdb[10] = target;
                    spt.Cdb[11] = SmartCommand;
                    break;
                case SatPassThroughFlavor.Cypress:
                    spt.CdbLength = 16;

                    spt.Cdb[0] = 0x24;
                    spt.Cdb[1] = 0x24;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = 0xBE;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = command;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = 0;
                    spt.Cdb[9] = SmartCylinderLow;
                    spt.Cdb[10] = SmartCylinderHigh;
                    spt.Cdb[11] = target;
                    spt.Cdb[12] = SmartCommand;
                    spt.Cdb[13] = 0;
                    spt.Cdb[14] = 0;
                    spt.Cdb[15] = 0;
                    break;
                case SatPassThroughFlavor.GenericSat:
                default:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xA1;
                    spt.Cdb[1] = (4 << 1);
                    spt.Cdb[2] = ((1 << 3) | (1 << 2) | 2);
                    spt.Cdb[3] = command;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = 1;
                    spt.Cdb[6] = SmartCylinderLow;
                    spt.Cdb[7] = SmartCylinderHigh;
                    spt.Cdb[8] = target;
                    spt.Cdb[9] = SmartCommand;
                    break;
            }
        }

        private static SatPassThroughFlavor GetFlavor(StorageDevice device)
        {
            if (device?.Controller?.VendorID == null)
            {
                return SatPassThroughFlavor.GenericSat;
            }

            switch (device.Controller.VendorID.Value)
            {
                case VendorIDConstants.Asmedia:
                    return SatPassThroughFlavor.Asm1352R;
                case VendorIDConstants.Realtek:
                    return SatPassThroughFlavor.Realtek9220DP;
                case VendorIDConstants.Sunplus:
                    return SatPassThroughFlavor.Sunplus;
                case VendorIDConstants.IoData:
                    return SatPassThroughFlavor.IoData;
                case VendorIDConstants.Logitec:
                    return SatPassThroughFlavor.Logitech;
                case VendorIDConstants.Prolific:
                    return SatPassThroughFlavor.Prolific;
                case VendorIDConstants.JMicron:
                    return SatPassThroughFlavor.JMicron;
                case VendorIDConstants.Cypress:
                    return SatPassThroughFlavor.Cypress;
            }

            return SatPassThroughFlavor.GenericSat;
        }

        private static void BuildIdentifyCdb(ref SCSI_PASS_THROUGH spt, byte target, SatPassThroughFlavor flavor)
        {
            Array.Clear(spt.Cdb, 0, spt.Cdb.Length);

            switch (flavor)
            {
                case SatPassThroughFlavor.Asm1352R:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xA1;
                    spt.Cdb[1] = (0xE << 1) | 0;
                    spt.Cdb[2] = ((1 << 3) | (1 << 2) | 2);
                    spt.Cdb[3] = 0;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = 0;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = target;
                    spt.Cdb[9] = IdentifyDeviceCommand;
                    break;
                case SatPassThroughFlavor.Realtek9220DP:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xA1;
                    spt.Cdb[1] = (4 << 1) | 0;
                    spt.Cdb[2] = ((1 << 3) | (1 << 2) | 2);
                    spt.Cdb[3] = 0;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = 0;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = target;
                    spt.Cdb[9] = IdentifyDeviceCommand;
                    break;
                case SatPassThroughFlavor.Sunplus:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xF8;
                    spt.Cdb[1] = 0;
                    spt.Cdb[2] = 0x22;
                    spt.Cdb[3] = 0x10;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = 0x01;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = 0;
                    spt.Cdb[9] = 0;
                    spt.Cdb[10] = target;
                    spt.Cdb[11] = IdentifyDeviceCommand;
                    break;
                case SatPassThroughFlavor.IoData:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xE3;
                    spt.Cdb[1] = 0;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = 0x01;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = 0;
                    spt.Cdb[7] = target;
                    spt.Cdb[8] = IdentifyDeviceCommand;
                    spt.Cdb[9] = 0;
                    spt.Cdb[10] = 0;
                    spt.Cdb[11] = 0;
                    break;
                case SatPassThroughFlavor.Logitech:
                    spt.CdbLength = 10;

                    spt.Cdb[0] = 0xE0;
                    spt.Cdb[1] = 0;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = 0;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = 0;
                    spt.Cdb[7] = target;
                    spt.Cdb[8] = IdentifyDeviceCommand;
                    spt.Cdb[9] = 0x4C;
                    break;
                case SatPassThroughFlavor.Prolific:
                    spt.CdbLength = 16;

                    spt.Cdb[0] = 0xD8;
                    spt.Cdb[1] = 0x15;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = 0;
                    spt.Cdb[4] = 0x06;
                    spt.Cdb[5] = 0x7B;
                    spt.Cdb[6] = 0;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = 0x02;
                    spt.Cdb[9] = 0;
                    spt.Cdb[10] = 0x01;
                    spt.Cdb[11] = 0;
                    spt.Cdb[12] = 0;
                    spt.Cdb[13] = 0;
                    spt.Cdb[14] = target;
                    spt.Cdb[15] = IdentifyDeviceCommand;
                    break;
                case SatPassThroughFlavor.JMicron:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xDF;
                    spt.Cdb[1] = 0x10;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = 0x02;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = 0x01;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = 0;
                    spt.Cdb[9] = 0;
                    spt.Cdb[10] = target;
                    spt.Cdb[11] = IdentifyDeviceCommand;
                    break;
                case SatPassThroughFlavor.Cypress:
                    spt.CdbLength = 16;

                    spt.Cdb[0] = 0x24;
                    spt.Cdb[1] = 0x24;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = 0xBE;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = 0;
                    spt.Cdb[7] = 0x01;
                    spt.Cdb[8] = 0;
                    spt.Cdb[9] = 0;
                    spt.Cdb[10] = 0;
                    spt.Cdb[11] = target;
                    spt.Cdb[12] = IdentifyDeviceCommand;
                    spt.Cdb[13] = 0;
                    spt.Cdb[14] = 0;
                    spt.Cdb[15] = 0;
                    break;
                case SatPassThroughFlavor.GenericSat:
                default:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xA1;
                    spt.Cdb[1] = (4 << 1);
                    spt.Cdb[2] = ((1 << 3) | (1 << 2) | 2);
                    spt.Cdb[3] = 0;
                    spt.Cdb[4] = 1;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = 0;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = target;
                    spt.Cdb[9] = IdentifyDeviceCommand;
                    break;
            }
        }

        private static void BuildSmartCdb(ref SCSI_PASS_THROUGH spt, byte target, SatPassThroughFlavor flavor, byte feature)
        {
            Array.Clear(spt.Cdb, 0, spt.Cdb.Length);

            switch (flavor)
            {
                case SatPassThroughFlavor.Asm1352R:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xA1;
                    spt.Cdb[1] = (0xE << 1);
                    spt.Cdb[2] = ((1 << 3) | (1 << 2) | 2);
                    spt.Cdb[3] = feature;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = 0x01;
                    spt.Cdb[6] = SmartCylinderLow;
                    spt.Cdb[7] = SmartCylinderHigh;
                    spt.Cdb[8] = target;
                    spt.Cdb[9] = SmartCommand;
                    break;
                case SatPassThroughFlavor.Realtek9220DP:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xA1;
                    spt.Cdb[1] = (4 << 1);
                    spt.Cdb[2] = ((1 << 3) | (1 << 2) | 2);
                    spt.Cdb[3] = feature;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = 0x01;
                    spt.Cdb[6] = SmartCylinderLow;
                    spt.Cdb[7] = SmartCylinderHigh;
                    spt.Cdb[8] = target;
                    spt.Cdb[9] = SmartCommand;
                    break;
                case SatPassThroughFlavor.Sunplus:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xF8;
                    spt.Cdb[1] = 0;
                    spt.Cdb[2] = 0x22;
                    spt.Cdb[3] = 0x10;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = feature;
                    spt.Cdb[6] = 0x01;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = SmartCylinderLow;
                    spt.Cdb[9] = SmartCylinderHigh;
                    spt.Cdb[10] = target;
                    spt.Cdb[11] = SmartCommand;
                    break;
                case SatPassThroughFlavor.IoData:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xE3;
                    spt.Cdb[1] = 0;
                    spt.Cdb[2] = feature;
                    spt.Cdb[3] = 0;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = SmartCylinderLow;
                    spt.Cdb[6] = SmartCylinderHigh;
                    spt.Cdb[7] = target;
                    spt.Cdb[8] = SmartCommand;
                    spt.Cdb[9] = 0;
                    spt.Cdb[10] = 0;
                    spt.Cdb[11] = 0;
                    break;
                case SatPassThroughFlavor.Logitech:
                    spt.CdbLength = 10;

                    spt.Cdb[0] = 0xE0;
                    spt.Cdb[1] = 0;
                    spt.Cdb[2] = feature;
                    spt.Cdb[3] = 0;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = SmartCylinderLow;
                    spt.Cdb[6] = SmartCylinderHigh;
                    spt.Cdb[7] = target;
                    spt.Cdb[8] = SmartCommand;
                    spt.Cdb[9] = 0x4C;
                    break;
                case SatPassThroughFlavor.Prolific:
                    spt.CdbLength = 16;

                    spt.Cdb[0] = 0xD8;
                    spt.Cdb[1] = 0x15;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = feature;
                    spt.Cdb[4] = 0x06;
                    spt.Cdb[5] = 0x7B;
                    spt.Cdb[6] = 0;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = 0x02;
                    spt.Cdb[9] = 0;
                    spt.Cdb[10] = 0x01;
                    spt.Cdb[11] = 0;
                    spt.Cdb[12] = SmartCylinderLow;
                    spt.Cdb[13] = SmartCylinderHigh;
                    spt.Cdb[14] = target;
                    spt.Cdb[15] = SmartCommand;
                    break;
                case SatPassThroughFlavor.JMicron:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xDF;
                    spt.Cdb[1] = 0x10;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = 0x02;
                    spt.Cdb[4] = 0;
                    spt.Cdb[5] = feature;
                    spt.Cdb[6] = 0x01;
                    spt.Cdb[7] = 0x01;
                    spt.Cdb[8] = SmartCylinderLow;
                    spt.Cdb[9] = SmartCylinderHigh;
                    spt.Cdb[10] = target;
                    spt.Cdb[11] = SmartCommand;
                    break;
                case SatPassThroughFlavor.Cypress:
                    spt.CdbLength = 16;

                    spt.Cdb[0] = 0x24;
                    spt.Cdb[1] = 0x24;
                    spt.Cdb[2] = 0;
                    spt.Cdb[3] = 0xBE;
                    spt.Cdb[4] = 0x01;
                    spt.Cdb[5] = 0;
                    spt.Cdb[6] = feature;
                    spt.Cdb[7] = 0;
                    spt.Cdb[8] = 0;
                    spt.Cdb[9] = SmartCylinderLow;
                    spt.Cdb[10] = SmartCylinderHigh;
                    spt.Cdb[11] = target;
                    spt.Cdb[12] = SmartCommand;
                    spt.Cdb[13] = 0;
                    spt.Cdb[14] = 0;
                    spt.Cdb[15] = 0;
                    break;
                case SatPassThroughFlavor.GenericSat:
                default:
                    spt.CdbLength = 12;

                    spt.Cdb[0] = 0xA1;
                    spt.Cdb[1] = (4 << 1);
                    spt.Cdb[2] = ((1 << 3) | (1 << 2) | 2);
                    spt.Cdb[3] = feature;
                    spt.Cdb[4] = 1;
                    spt.Cdb[5] = 1;
                    spt.Cdb[6] = SmartCylinderLow;
                    spt.Cdb[7] = SmartCylinderHigh;
                    spt.Cdb[8] = target;
                    spt.Cdb[9] = SmartCommand;
                    break;
            }
        }

        #endregion
    }
}
