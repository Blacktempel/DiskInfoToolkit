/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Florian K.
 *
 * Code inspiration, improvements and fixes are from, but not limited to, following projects:
 * CrystalDiskInfo
 */

using BlackSharp.Core.Extensions;
using BlackSharp.Core.Interop.Windows.Utilities;
using DiskInfoToolkit.Internal;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Interop.Structures;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit
{
    internal static class StorageDetector
    {
        #region Fields

        const int BUFFER_SIZE = 1024;

        internal const int DIGCF_PRESENT = 0x00000002;
        internal const int DIGCF_DEVICEINTERFACE = 0x10;

        const uint SPDRP_DEVICEDESC = 0x00000000;
        const uint SPDRP_HARDWAREID = 0x00000001;

        static Guid GUID_DEVCLASS_SCSIADAPTER = new Guid("4D36E97B-E325-11CE-BFC1-08002BE10318");
        static Guid GUID_DEVINTERFACE_DISK = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

        #endregion

        #region Public

        /// <summary>
        /// Detects and returns all available storage devices via SetupAPI.
        /// </summary>
        /// <returns>Returns all available storage devices via SetupAPI.</returns>
        public static List<StorageController> GetStorageDevices()
        {
            //Get storages
            var list = GetStoragesInternal();

            //Map to physical path
            MapStoragesInternal(list);

            return list;
        }

        #endregion

        #region Private

        static void MapStoragesInternal(List<StorageController> storagesInternal)
        {
            //Setup
            var devInfo = SetupAPI.SetupDiGetClassDevs(ref GUID_DEVINTERFACE_DISK, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

            if (devInfo == InteropConstants.InvalidHandle)
            {
                return;
            }

            var interfaceData = new SP_DEVICE_INTERFACE_DATA();
            interfaceData.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

            uint index = 0;
            while (SetupAPI.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref GUID_DEVINTERFACE_DISK, index, ref interfaceData))
            {
                ++index;

                SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

                //Get required buffer size
                SetupAPI.SetupDiGetDeviceInterfaceDetailW(devInfo, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);

                IntPtr detailDataBuffer = Marshal.AllocHGlobal(requiredSize);

                //Marshal.StructureToPtr(detailData, detailDataBuffer, false);
                Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6); //Do not change that

                //Get full device path and devInfo
                if (SetupAPI.SetupDiGetDeviceInterfaceDetailW(devInfo, ref interfaceData, detailDataBuffer, requiredSize, out _, ref devInfoData))
                {
                    var detailData = Marshal.PtrToStructure<SP_DEVICE_INTERFACE_DETAIL_DATA>(detailDataBuffer);
                    string devicePath = Encoding.Unicode.GetString(detailData.DevicePath);
                    devicePath = NormalizeString(devicePath);

                    //Get Device Instance ID
                    StringBuilder instanceId = new StringBuilder(BUFFER_SIZE);
                    if (SetupAPI.SetupDiGetDeviceInstanceId(devInfo, ref devInfoData, instanceId, instanceId.Capacity, out _))
                    {
                        var instanceIDstr = instanceId.ToString();

                        string hwID = GetHardwareID(devInfo, devInfoData);

                        bool found = false;

                        foreach (var si in storagesInternal)
                        {
                            //Find and assign physical path
                            var device = si.StorageDeviceIDs.Find(sdi => string.Compare(sdi.DeviceID, instanceIDstr, StringComparison.OrdinalIgnoreCase) == 0);
                            if (device != null)
                            {
                                device.PhysicalPath = devicePath;
                                device.HardwareID = hwID;
                                device.DriveNumber = GetDriveNumber(devicePath);
                                found = true;
                                break;
                            }
                        }

                        //Likely SATA disk
                        if (!found)
                        {
                            //Get storage controller (parent)
                            if (CfgMgr32.CM_Get_Parent(out var parentDevInst, devInfoData.devInst, 0) != 0)
                            {
                                continue;
                            }

                            //Get Storage Controller name
                            string controllerName = GetDeviceName(devInfo, devInfoData);

                            var storage = new StorageController() { Name = controllerName, HardwareID = hwID };

                            var deviceID = new StringBuilder(BUFFER_SIZE);

                            if (CfgMgr32.CM_Get_Device_ID(devInfoData.devInst, deviceID, deviceID.Capacity, 0) == 0)
                            {
                                //storage.StorageDeviceIDs.Add(new StorageDeviceInternal { DeviceID = deviceID.ToString(), HardwareID = storage.HardwareID, PhysicalPath = devicePath });
                                storage.StorageDeviceIDs.Add(new StorageDevice
                                {
                                    DeviceID = deviceID.ToString(),
                                    HardwareID = hwID,
                                    PhysicalPath = devicePath,
                                    DriveNumber = GetDriveNumber(devicePath),
                                });
                            }

                            //We only want storage controllers with devices
                            if (storage.StorageDeviceIDs.Count > 0)
                            {
                                storagesInternal.Add(storage);
                            }
                        }
                    }
                }

                Marshal.FreeHGlobal(detailDataBuffer);
            }

            //Cleanup
            SetupAPI.SetupDiDestroyDeviceInfoList(devInfo);
        }

        static List<StorageController> GetStoragesInternal()
        {
            var storageDevices = new List<StorageController>();

            //Setup
            var devInfo = SetupAPI.SetupDiGetClassDevs(ref GUID_DEVCLASS_SCSIADAPTER, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);

            if (devInfo == InteropConstants.InvalidHandle)
            {
                return storageDevices;
            }

            var infoData = new SP_DEVINFO_DATA();
            infoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

            uint index = 0;
            while (SetupAPI.SetupDiEnumDeviceInfo(devInfo, index, ref infoData))
            {
                ++index;

                //Get Storage Controller name
                string controllerName = GetDeviceName(devInfo, infoData);

                string hwID = GetHardwareID(devInfo, infoData);

                var storage = new StorageController() { Name = controllerName, HardwareID = hwID };

                //Find and add childs (actual storage devices)
                EnumChildDevices(infoData.devInst, storage);

                //We only want storage controllers with devices
                if (storage.StorageDeviceIDs.Count > 0)
                {
                    storageDevices.Add(storage);
                }
            }

            //Cleanup
            SetupAPI.SetupDiDestroyDeviceInfoList(devInfo);

            return storageDevices;
        }

        static int GetDriveNumber(string physicalPath)
        {
            var handle = SafeFileHandler.OpenHandle(physicalPath);
            if (SafeFileHandler.IsHandleValid(handle))
            {
                var number = Storage.GetDriveNumber(handle);

                SafeFileHandler.CloseHandle(handle);

                return number;
            }

            return -1;
        }

        static string GetDeviceName(IntPtr hDevInfo, SP_DEVINFO_DATA devInfoData)
        {
            var buffer = new char[BUFFER_SIZE];

            if (SetupAPI.SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfoData, SPDRP_DEVICEDESC, out _, buffer, (uint)buffer.Length, out _))
            {
                return NormalizeString(new string(buffer));
            }

            return null;
        }

        static string GetHardwareID(IntPtr hDevInfo, SP_DEVINFO_DATA devInfoData)
        {
            var buffer = new char[BUFFER_SIZE];

            if (SetupAPI.SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfoData, SPDRP_HARDWAREID, out _, buffer, (uint)buffer.Length, out _))
            {
                //Take first hardware-ID, no need for the rest
                return NormalizeString(new string(buffer));
            }

            return null;
        }

        static void EnumChildDevices(uint parentDevInst, StorageController storage)
        {
            if (CfgMgr32.CM_Get_Child(out var childDevInst, parentDevInst, 0) != 0)
            {
                return;
            }

            do
            {
                var deviceID = new StringBuilder(BUFFER_SIZE);

                if (CfgMgr32.CM_Get_Device_ID(childDevInst, deviceID, deviceID.Capacity, 0) == 0)
                {
                    storage.StorageDeviceIDs.Add(new StorageDevice { DeviceID = deviceID.ToString()/*, HardwareID = storage.HardwareID*/ });
                }

                EnumChildDevices(childDevInst, storage);
            }
            while (CfgMgr32.CM_Get_Sibling(out childDevInst, childDevInst, 0) == 0);
        }

        static string NormalizeString(string str)
        {
            var end = str.IndexOf('\0');

            if (end == -1)
            {
                end = 0;
            }

            return str.Substring(0, end);
        }

        #endregion

        #region Internal

        public static StorageController GetStorageDevice(string physicalPath)
        {
            int driveNumber = GetDriveNumber(physicalPath);

            var devices = GetStorageDevices();

            for (int i = 0; i < devices.Count; ++i)
            {
                var device = devices[i];

                for (int j = 0; j < device.StorageDeviceIDs.Count; ++j)
                {
                    var drive = device.StorageDeviceIDs[j];

                    var num = GetDriveNumber(drive.PhysicalPath);

                    if (driveNumber == num)
                    {
                        //This one has updated, remove rest
                        devices.RemoveIf(si => si != device);
                        device.StorageDeviceIDs.RemoveIf(sdi => sdi != drive);

                        return device;
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
