/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Models;
using DiskInfoToolkit.Probes;
using Microsoft.Win32.SafeHandles;
using System.Text;

namespace DiskInfoToolkit.Tests.Probes
{
    [TestClass]
    public class UsbNvmeBridgeProbeTest
    {
        #region Public

        [TestMethod]
        public void OwcExpress1M2IsClassifiedAsAsmediaNvmeBridge()
        {
            StorageDevice device = CreateOwcExpress1M2();

            UsbBridgeClassifier.Apply(device);
            UsbNvmeSetupModeDetector.Apply(device);

            Assert.AreEqual(UsbBridgeFamilyNames.Asmedia, device.Usb.BridgeFamily);
            Assert.AreEqual(UsbMassStorageProtocolNames.Uasp, device.Usb.MassStorageProtocolName);
            Assert.AreEqual(UsbNvmeSetupModeNames.ASMediaPassThrough, device.Usb.NvmeSetupMode);
            Assert.IsTrue(UsbNvmeSetupModeDetector.IsUsbNvmeCandidate(device));
        }

        [TestMethod]
        public void OwcExpress1M2UsesAsmediaPassThroughForNvmeSmart()
        {
            StorageDevice device = CreateOwcExpress1M2();
            var ioControl = new AsmediaNvmeIoControl();

            UsbBridgeClassifier.Apply(device);
            UsbNvmeSetupModeDetector.Apply(device);

            bool success = UsbNvmeBridgeProbe.TryPopulateData(device, ioControl);

            Assert.IsTrue(success);
            Assert.AreEqual(2, ioControl.ScsiPassThroughCount);
            Assert.IsTrue(device.SupportsSmart);
            Assert.AreEqual(StorageTransportKind.Nvme, device.TransportKind);
            Assert.AreEqual(StorageBusType.Usb, device.BusType);
            Assert.AreEqual("Samsung SSD 990 PRO 2TB", device.ProductName);
            Assert.AreEqual("Samsung SSD 990 PRO 2TB", device.DisplayName);
            Assert.AreEqual("S7DNNABCDEZYX", device.SerialNumber);
            Assert.AreEqual("4B2QJXD7", device.ProductRevision);
            Assert.AreEqual(29, device.SmartAttributes.Count);
            Assert.AreEqual((ulong)309, device.SmartAttributes[1].RawValue);
            Assert.IsTrue(device.ProbeTrace.Any(entry => entry.Contains("ASMediaPassThrough SMART succeeded", StringComparison.Ordinal)));
        }

        #endregion

        #region Private

        private static StorageDevice CreateOwcExpress1M2()
        {
            var device = new StorageDevice();
            device.DevicePath = @"\\?\scsi#disk&ven_owc&prod_express_1m2#test";
            device.DisplayName = "OWC Express 1M2 SCSI Disk Device";
            device.VendorName = "OWC";
            device.ProductName = "Express 1M2";
            device.Controller.Service = ControllerServiceNames.UaspStor;
            device.Controller.Class = ControllerClassNames.ScsiAdapter;
            device.Controller.VendorID = VendorIDConstants.OtherWorldComputing;
            device.Controller.DeviceID = UsbNvmeBridgeProductIDConstants.OtherWorldComputingExpress1M2;
            device.Controller.HardwareID = @"USB\VID_1E91&PID_DE79&REV_0100";

            return device;
        }

        private sealed class AsmediaNvmeIoControl : IStorageIoControl
        {
            public int ScsiPassThroughCount { get; private set; }

            public SafeFileHandle OpenDevice(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes)
            {
                return new SafeFileHandle(new IntPtr(1), false);
            }

            public bool SendRawIoControl(SafeFileHandle handle, uint ioControlCode, byte[] inBuffer, byte[] outBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TryGetStorageDeviceDescriptor(SafeFileHandle handle, out StorageDeviceDescriptorInfo descriptor)
            {
                descriptor = null;
                return false;
            }

            public bool TryGetStorageAdapterDescriptor(SafeFileHandle handle, out StorageAdapterDescriptorInfo descriptor)
            {
                descriptor = null;
                return false;
            }

            public bool TryGetDriveLayout(SafeFileHandle handle, out byte[] rawLayout)
            {
                rawLayout = null;
                return false;
            }

            public bool TryGetScsiAddress(SafeFileHandle handle, out ScsiAddressInfo scsiAddress)
            {
                scsiAddress = null;
                return false;
            }

            public bool TryGetStorageDeviceNumber(SafeFileHandle handle, out StorageDeviceNumberInfo info)
            {
                info = null;
                return false;
            }

            public bool TryGetDriveGeometryEx(SafeFileHandle handle, out DiskGeometryInfo info)
            {
                info = null;
                return false;
            }

            public bool TryGetPredictFailure(SafeFileHandle handle, out PredictFailureInfo info)
            {
                info = null;
                return false;
            }

            public bool TryGetSffDiskDeviceProtocol(SafeFileHandle handle, out StorageProtocolType protocolType)
            {
                protocolType = default;
                return false;
            }

            public bool TryGetSmartVersion(SafeFileHandle handle, out SmartVersionInfo info)
            {
                info = null;
                return false;
            }

            public bool TryScsiPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                ++ScsiPassThroughCount;
                bytesReturned = responseBuffer.Length;

                Assert.AreEqual((byte)0xE6, requestBuffer[36]);

                if (requestBuffer[37] == 0x06 && requestBuffer[39] == 0x01)
                {
                    WriteAscii(responseBuffer, 92 + 4, 20, "S7DNNABCDEZYX");
                    WriteAscii(responseBuffer, 92 + 24, 40, "Samsung SSD 990 PRO 2TB");
                    WriteAscii(responseBuffer, 92 + 64, 8, "4B2QJXD7");
                    return true;
                }

                if (requestBuffer[37] == 0x02 && requestBuffer[39] == 0x02)
                {
                    Assert.AreEqual((uint)512, BitConverter.ToUInt32(requestBuffer, 12));

                    int dataOffset = 92;
                    WriteUInt16(responseBuffer, dataOffset + 1, 309);
                    responseBuffer[dataOffset + 3] = 100;
                    responseBuffer[dataOffset + 4] = 10;
                    WriteUInt64(responseBuffer, dataOffset + 32, 1000);
                    WriteUInt64(responseBuffer, dataOffset + 48, 2000);
                    WriteUInt64(responseBuffer, dataOffset + 112, 482);
                    WriteUInt64(responseBuffer, dataOffset + 128, 1195);
                    return true;
                }

                return false;
            }

            public bool TryScsiMiniport(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TryAtaPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TryIdePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TrySmartReceiveDriveData(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TrySmartSendDriveCommand(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            public bool TryIntelNvmePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned)
            {
                bytesReturned = 0;
                return false;
            }

            private static void WriteAscii(byte[] buffer, int offset, int length, string value)
            {
                byte[] encoded = Encoding.ASCII.GetBytes(value.PadRight(length));
                Buffer.BlockCopy(encoded, 0, buffer, offset, length);
            }

            private static void WriteUInt16(byte[] buffer, int offset, ushort value)
            {
                byte[] encoded = BitConverter.GetBytes(value);
                Buffer.BlockCopy(encoded, 0, buffer, offset, encoded.Length);
            }

            private static void WriteUInt64(byte[] buffer, int offset, ulong value)
            {
                byte[] encoded = BitConverter.GetBytes(value);
                Buffer.BlockCopy(encoded, 0, buffer, offset, encoded.Length);
            }
        }

        #endregion
    }
}
