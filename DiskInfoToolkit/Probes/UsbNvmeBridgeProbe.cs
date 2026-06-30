/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using BlackSharp.Core.Interop.Windows.Mutexes;
using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Globals;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;

namespace DiskInfoToolkit.Probes
{
    public static class UsbNvmeBridgeProbe
    {
        #region Fields

        private const int NvmeIdentifyBytes = 512;

        private const int NvmeSmartLogBytes = 512;

        #endregion

        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
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
                bool any = false;

                string service = StringUtil.TrimStorageString(device.Controller.Service);
                if (!service.Equals(ControllerServiceNames.SecNvme, StringComparison.OrdinalIgnoreCase))
                {
                    if (NvmeProbe.TryPopulateStandardNvmeData(device, ioControl))
                    {
                        ProbeTraceRecorder.Add(device, "USB NVMe: standard storage query succeeded.");
                        any = true;
                    }
                }

                if (!any)
                {
                    byte[] identifyData = null;
                    if (TryReadIdentify(device, ioControl, handle, out identifyData))
                    {
                        device.Nvme.IdentifyControllerData = identifyData;
                        ApplyUsbNvmeIdentifyControllerStrings(device, identifyData);

                        ProbeTraceRecorder.Add(device, "USB NVMe: " + device.Usb.NvmeSetupMode + " identify succeeded.");
                        any = true;
                    }
                }

                if (!any && LooksLikeSamsungUsbNvme(device))
                {
                    if (SamsungNvmeScsiProbe.TryPopulateData(device, ioControl))
                    {
                        ProbeTraceRecorder.Add(device, "USB NVMe: Samsung vendor SCSI fallback succeeded.");
                        any = true;
                    }
                }

                if (!any)
                {
                    return false;
                }

                if (device.Nvme.SmartLogData == null || device.Nvme.SmartLogData.Length == 0)
                {
                    byte[] smartLogData = null;
                    if (TryReadSmartLog(device, ioControl, handle, out smartLogData))
                    {
                        device.Nvme.SmartLogData = smartLogData;
                        device.SupportsSmart = true;
                        NvmeSmartLogParser.Apply(device, smartLogData);

                        ProbeTraceRecorder.Add(device, "USB NVMe: " + device.Usb.NvmeSetupMode + " SMART succeeded.");
                    }
                }

                if ((device.Nvme.SmartLogData == null || device.Nvme.SmartLogData.Length == 0) && LooksLikeSamsungUsbNvme(device))
                {
                    if (SamsungNvmeScsiProbe.TryPopulateData(device, ioControl))
                    {
                        ProbeTraceRecorder.Add(device, "USB NVMe: Samsung SMART fallback succeeded.");
                    }
                }

                device.TransportKind = StorageTransportKind.Nvme;
                device.BusType = StorageBusType.Usb;
                device.Controller.Kind = string.IsNullOrWhiteSpace(device.Controller.Kind) ? ControllerKindNames.NvmeUsb : device.Controller.Kind;
                return true;
            }
        }

        #endregion

        #region Private

        private static bool LooksLikeSamsungUsbNvme(StorageDevice device)
        {
            if (device == null)
            {
                return false;
            }

            if (device.Controller.VendorID.GetValueOrDefault() == VendorIDConstants.Samsung)
            {
                return true;
            }

            string service = StringUtil.TrimStorageString(device.Controller.Service);
            if (service.Equals(ControllerServiceNames.SecNvme, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string text = ((device.Controller.HardwareID ?? string.Empty) + " " + (device.DisplayName ?? string.Empty) + " " + (device.ProductName ?? string.Empty));
            return text.IndexOf(UsbBridgeFamilyNames.Samsung, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReadIdentify(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle, out byte[] identifyData)
        {
            identifyData = null;
            if (UsesBridgePassThrough(device, UsbNvmeSetupModeNames.ASMediaPassThrough, UsbBridgeFamilyNames.Asmedia, VendorIDConstants.Asmedia))
            {
                return TryAsmediaIdentify(ioControl, handle, out identifyData);
            }

            if (UsesBridgePassThrough(device, UsbNvmeSetupModeNames.RealtekPassThrough, UsbBridgeFamilyNames.Realtek, VendorIDConstants.Realtek))
            {
                return TryRealtekIdentify(ioControl, handle, out identifyData);
            }

            if (UsesBridgePassThrough(device, UsbNvmeSetupModeNames.JMicronPassThrough, UsbBridgeFamilyNames.JMicron, VendorIDConstants.JMicron))
            {
                return TryJmicronIdentify(ioControl, handle, out identifyData);
            }

            return false;
        }

        private static bool TryReadSmartLog(StorageDevice device, IStorageIoControl ioControl, SafeFileHandle handle, out byte[] smartLogData)
        {
            smartLogData = null;
            if (UsesBridgePassThrough(device, UsbNvmeSetupModeNames.ASMediaPassThrough, UsbBridgeFamilyNames.Asmedia, VendorIDConstants.Asmedia))
            {
                return TryAsmediaSmartLog(ioControl, handle, out smartLogData);
            }

            if (UsesBridgePassThrough(device, UsbNvmeSetupModeNames.RealtekPassThrough, UsbBridgeFamilyNames.Realtek, VendorIDConstants.Realtek))
            {
                return TryRealtekSmartLog(ioControl, handle, out smartLogData);
            }

            if (UsesBridgePassThrough(device, UsbNvmeSetupModeNames.JMicronPassThrough, UsbBridgeFamilyNames.JMicron, VendorIDConstants.JMicron))
            {
                return TryJmicronSmartLog(ioControl, handle, out smartLogData);
            }

            return false;
        }

        private static void ApplyUsbNvmeIdentifyControllerStrings(StorageDevice device, byte[] identifyData)
        {
            NvmeProbeUtil.ApplyIdentifyControllerStrings(device, identifyData);

            string model = NvmeProbeUtil.ReadAscii(identifyData, 24, 40);
            if (!IsPlausibleIdentifyText(model, 40))
            {
                return;
            }

            device.ProductName = model;
            device.DisplayName = model;
        }

        private static bool IsPlausibleIdentifyText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            {
                return false;
            }

            bool hasAlphaNumeric = false;
            for (int i = 0; i < value.Length; ++i)
            {
                char c = value[i];
                if (c < 0x20 || c > 0x7E)
                {
                    return false;
                }

                if (char.IsLetterOrDigit(c))
                {
                    hasAlphaNumeric = true;
                }
            }

            return hasAlphaNumeric;
        }

        private static bool UsesBridgePassThrough(StorageDevice device, string setupMode, string bridgeFamily, ushort nativeVendorID)
        {
            if (device == null)
            {
                return false;
            }

            if (StringUtil.TrimStorageString(device.Usb.NvmeSetupMode).Equals(setupMode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (StringUtil.TrimStorageString(device.Usb.BridgeFamily).Equals(bridgeFamily, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return device.Controller.VendorID.GetValueOrDefault() == nativeVendorID;
        }

        private static bool TryAsmediaIdentify(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] identifyData)
        {
            identifyData = null;

            var buffer = new byte[92 + BufferSizeConstants.Size4K];

            BuildScsiPassThroughHeader(buffer, 16, 24, true, BufferSizeConstants.Size4K, 2, 92, 60);
            buffer[36] = 0xE6;
            WriteUInt16(buffer, 37, 6);
            WriteUInt32(buffer, 39, 1);
            WriteUInt32(buffer, 43, 0);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            if (buffer[2] != 0)
            {
                return false;
            }

            identifyData = new byte[NvmeIdentifyBytes];
            Buffer.BlockCopy(buffer, 92, identifyData, 0, identifyData.Length);
            return NvmeProbeUtil.HasAnyNonZeroByte(identifyData);
        }

        private static bool TryRealtekIdentify(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] identifyData)
        {
            identifyData = null;

            var buffer = new byte[0x105C];

            BuildScsiPassThroughHeader(buffer, 16, 32, true, BufferSizeConstants.Size4K, 2, 92, 60);
            buffer[36] = 0xE4;
            buffer[37] = buffer[12];
            buffer[38] = 16;
            buffer[39] = 6;
            WriteUInt32(buffer, 40, 1);
            WriteUInt32(buffer, 44, 0);
            WriteUInt32(buffer, 48, 0);

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            if (buffer[2] != 0)
            {
                return false;
            }

            identifyData = new byte[NvmeIdentifyBytes];
            Buffer.BlockCopy(buffer, 92, identifyData, 0, identifyData.Length);
            return NvmeProbeUtil.HasAnyNonZeroByte(identifyData);
        }

        private static bool TryJmicronIdentify(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] identifyData)
        {
            identifyData = null;

            using var guard = new WorldMutexGuard(WorldMutexManager.WorldJMicronMutex);

            var buffer = new byte[0x250];

            BuildScsiPassThroughHeader(buffer, 12, 24, false, 512, 2, 80, 56);
            buffer[36] = 0xA1;
            buffer[37] = 0x80;
            buffer[40] = 2;
            buffer[80] = (byte)'N';
            buffer[81] = (byte)'V';
            buffer[82] = (byte)'M';
            buffer[83] = (byte)'E';

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            if (buffer[2] != 0)
            {
                return false;
            }

            Array.Clear(buffer, 36, 16);

            BuildScsiPassThroughHeader(buffer, 12, 24, true, 512, 2, 80, 56);
            buffer[36] = 0xA1;
            buffer[37] = 0x82;
            buffer[40] = 2;

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out bytesReturned))
            {
                return false;
            }

            if (buffer[2] != 0)
            {
                return false;
            }

            identifyData = new byte[NvmeIdentifyBytes];
            Buffer.BlockCopy(buffer, 80, identifyData, 0, identifyData.Length);
            return NvmeProbeUtil.HasAnyNonZeroByte(identifyData);
        }

        private static bool TryAsmediaSmartLog(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] smartLogData)
        {
            smartLogData = null;

            var buffer = new byte[92 + NvmeSmartLogBytes];

            BuildScsiPassThroughHeader(buffer, 16, 24, true, NvmeSmartLogBytes, 2, 92, 60);
            buffer[36] = 0xE6;
            buffer[37] = 0x02;
            buffer[39] = 0x02;
            buffer[43] = 0x7F;

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            if (buffer[2] != 0)
            {
                return false;
            }

            smartLogData = new byte[NvmeSmartLogBytes];
            Buffer.BlockCopy(buffer, 92, smartLogData, 0, smartLogData.Length);
            return NvmeProbeUtil.HasAnyNonZeroByte(smartLogData);
        }

        private static bool TryRealtekSmartLog(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] smartLogData)
        {
            smartLogData = null;

            var buffer = new byte[0x105C];

            BuildScsiPassThroughHeader(buffer, 16, 32, true, BufferSizeConstants.Size4K, 2, 92, 60);
            buffer[36] = 0xE4;
            buffer[37] = 0;
            buffer[38] = 2;
            buffer[39] = 0x02;

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            if (buffer[2] != 0)
            {
                return false;
            }

            smartLogData = new byte[512];
            Buffer.BlockCopy(buffer, 92, smartLogData, 0, smartLogData.Length);
            return NvmeProbeUtil.HasAnyNonZeroByte(smartLogData);
        }

        private static bool TryJmicronSmartLog(IStorageIoControl ioControl, SafeFileHandle handle, out byte[] smartLogData)
        {
            smartLogData = null;

            using var guard = new WorldMutexGuard(WorldMutexManager.WorldJMicronMutex);

            var buffer = new byte[0x250];

            BuildScsiPassThroughHeader(buffer, 12, 24, false, 512, 2, 80, 56);
            buffer[36] = 0xA1;
            buffer[37] = 0x80;
            buffer[40] = 0x02;
            buffer[80] = (byte)'N';
            buffer[81] = (byte)'V';
            buffer[82] = (byte)'M';
            buffer[83] = (byte)'E';
            buffer[88] = 0x02;
            buffer[90] = 0x56;
            buffer[92] = 0xFF;
            buffer[93] = 0xFF;
            buffer[94] = 0xFF;
            buffer[95] = 0xFF;
            buffer[0xA1] = 0x40;
            buffer[0xA2] = 0x7A;
            buffer[0xB0] = 0x02;
            buffer[0xB2] = 0x7F;

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            if (buffer[2] != 0)
            {
                return false;
            }

            Array.Clear(buffer, 36, 16);

            BuildScsiPassThroughHeader(buffer, 12, 24, true, 512, 2, 80, 56);
            buffer[36] = 0xA1;
            buffer[37] = 0x82;
            buffer[40] = 0x02;

            if (!ioControl.TryScsiPassThrough(handle, buffer, buffer, out bytesReturned))
            {
                return false;
            }

            if (buffer[2] != 0)
            {
                return false;
            }

            smartLogData = new byte[512];
            Buffer.BlockCopy(buffer, 80, smartLogData, 0, smartLogData.Length);
            return NvmeProbeUtil.HasAnyNonZeroByte(smartLogData);
        }

        private static void BuildScsiPassThroughHeader(byte[] buffer, byte cdbLength, byte senseLength, bool dataIn, int transferLength, uint timeoutSeconds, int dataOffset, int senseOffset)
        {
            Array.Clear(buffer, 0, Math.Min(buffer.Length, dataOffset));

            WriteUInt16(buffer, 0, 56);
            buffer[6] = cdbLength;
            buffer[7] = senseLength;
            buffer[8] = dataIn ? (byte)1 : (byte)0;
            WriteUInt32(buffer, 12, (uint)transferLength);
            WriteUInt32(buffer, 16, timeoutSeconds);
            WriteUInt64(buffer, 24, (ulong)dataOffset);
            WriteUInt32(buffer, 32, (uint)senseOffset);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            var tmp = BitConverter.GetBytes(value);
            Buffer.BlockCopy(tmp, 0, buffer, offset, 2);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            var tmp = BitConverter.GetBytes(value);
            Buffer.BlockCopy(tmp, 0, buffer, offset, 4);
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            var tmp = BitConverter.GetBytes(value);
            Buffer.BlockCopy(tmp, 0, buffer, offset, 8);
        }

        #endregion
    }
}
