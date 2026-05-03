/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Utilities;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using OS = BlackSharp.Core.Platform.OperatingSystem;

namespace DiskInfoToolkit.Pnp
{
    public static class PnpDiskEnumerator
    {
        #region Fields

        private static Guid DiskInterfaceGuid = new Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

        #endregion

        #region Public

        public static List<PnpDiskNode> EnumerateDiskInterfaces()
        {
            if (OS.IsLinux())
            {
                return LinuxSysfsDiskEnumerator.EnumerateDiskInterfaces();
            }

            if (!OS.IsWindows())
            {
                return new List<PnpDiskNode>();
            }

            var result = new List<PnpDiskNode>();

            IntPtr infoSet = SetupAPINative.SetupDiGetClassDevs(ref DiskInterfaceGuid, null, IntPtr.Zero,
                SetupDiGetClassDevsFlags.DIGCF_PRESENT | SetupDiGetClassDevsFlags.DIGCF_DEVICEINTERFACE);

            if (infoSet == SetupAPINative.InvalidHandleValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"{nameof(SetupAPINative.SetupDiGetClassDevs)} failed.");
            }

            try
            {
                uint index = 0;
                while (true)
                {
                    var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                    interfaceData.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

                    bool ok = SetupAPINative.SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref DiskInterfaceGuid, index, ref interfaceData);
                    if (!ok)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == SetupAPINative.ERROR_NO_MORE_ITEMS)
                        {
                            break;
                        }

                        throw new Win32Exception(error, $"{nameof(SetupAPINative.SetupDiEnumDeviceInterfaces)} failed.");
                    }

                    result.Add(ReadInterface(infoSet, ref interfaceData));
                    ++index;
                }
            }
            finally
            {
                SetupAPINative.SetupDiDestroyDeviceInfoList(infoSet);
            }

            return result;
        }

        #endregion

        #region Private

        private static PnpDiskNode ReadInterface(IntPtr infoSet, ref SP_DEVICE_INTERFACE_DATA interfaceData)
        {
            SetupAPINative.SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
            IntPtr detailBuffer = Marshal.AllocHGlobal(requiredSize);

            try
            {
                Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                var deviceInfoData = new SP_DEVINFO_DATA();
                deviceInfoData.cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>();

                if (!SetupAPINative.SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detailBuffer, requiredSize, out requiredSize, ref deviceInfoData))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"{nameof(SetupAPINative.SetupDiGetDeviceInterfaceDetail)} failed.");
                }

                var node = new PnpDiskNode();
                node.DevicePath = Marshal.PtrToStringUni(new IntPtr(detailBuffer.ToInt64() + 4)) ?? string.Empty;
                node.DeviceInstanceID = GetSetupDiInstanceId(infoSet, ref deviceInfoData);
                node.DeviceDescription = GetSetupDiPropertyString(infoSet, ref deviceInfoData, SetupDiRegistryProperty.SPDRP_DEVICEDESC);
                node.FriendlyName = GetSetupDiPropertyString(infoSet, ref deviceInfoData, SetupDiRegistryProperty.SPDRP_FRIENDLYNAME);
                node.HardwareID = GetSetupDiPropertyString(infoSet, ref deviceInfoData, SetupDiRegistryProperty.SPDRP_HARDWAREID);

                if (CfgMgr32Native.CM_Get_Parent(out var parentDevInst, deviceInfoData.DevInst, 0) == 0)
                {
                    node.ParentInstanceID = GetDeviceId(parentDevInst);
                    node.ParentHardwareID = GetDevNodeRegistryPropertyString(parentDevInst, CmDeviceRegistryProperty.HardwareId);
                    node.ParentClass = GetDevNodeRegistryPropertyString(parentDevInst, CmDeviceRegistryProperty.Class);
                    node.ParentService = GetDevNodeRegistryPropertyString(parentDevInst, CmDeviceRegistryProperty.Service);
                    node.ParentDisplayName = GetParentDisplayName(parentDevInst);
                    node.ControllerIdentifier = BuildControllerIdentifier(deviceInfoData.DevInst, node.ParentService);
                }
                else
                {
                    node.ControllerIdentifier = BuildControllerIdentifier(deviceInfoData.DevInst, string.Empty);
                }

                return node;
            }
            finally
            {
                Marshal.FreeHGlobal(detailBuffer);
            }
        }

        private static string GetSetupDiInstanceId(IntPtr infoSet, ref SP_DEVINFO_DATA deviceInfoData)
        {
            var buffer = new char[512];

            if (!SetupAPINative.SetupDiGetDeviceInstanceId(infoSet, ref deviceInfoData, buffer, buffer.Length, out var requiredSize))
            {
                return string.Empty;
            }

            return StringUtil.TrimStorageString(new string(buffer));
        }

        private static string GetSetupDiPropertyString(IntPtr infoSet, ref SP_DEVINFO_DATA deviceInfoData, SetupDiRegistryProperty property)
        {
            SetupAPINative.SetupDiGetDeviceRegistryProperty(infoSet, ref deviceInfoData, property, out var propertyType, null, 0, out var requiredSize);
            if (requiredSize <= 2)
            {
                return string.Empty;
            }

            var buffer = new byte[requiredSize];
            if (!SetupAPINative.SetupDiGetDeviceRegistryProperty(infoSet, ref deviceInfoData, property, out propertyType, buffer, buffer.Length, out requiredSize))
            {
                return string.Empty;
            }

            return StringUtil.TrimStorageString(Encoding.Unicode.GetString(buffer, 0, requiredSize));
        }

        private static string GetDeviceId(uint devInst)
        {
            var buffer = new char[512];
            if (CfgMgr32Native.CM_Get_Device_ID(devInst, buffer, buffer.Length, 0) != 0)
            {
                return string.Empty;
            }

            return StringUtil.TrimStorageString(new string(buffer));
        }

        private static string GetDevNodeRegistryPropertyString(uint devInst, CmDeviceRegistryProperty property)
        {
            int requiredSize = 1024;
            var buffer = new byte[requiredSize];

            if (CfgMgr32Native.CM_Get_DevNode_Registry_Property(devInst, (uint)property, out var propertyType, buffer, ref requiredSize, 0) != 0)
            {
                return string.Empty;
            }

            return StringUtil.TrimStorageString(Encoding.Unicode.GetString(buffer, 0, requiredSize));
        }

        private static string GetRawDevNodeRegistryPropertyString(uint devInst, uint propertyId)
        {
            int requiredSize = 1024;
            var buffer = new byte[requiredSize];

            if (CfgMgr32Native.CM_Get_DevNode_Registry_Property(devInst, propertyId, out var propertyType, buffer, ref requiredSize, 0) != 0)
            {
                return string.Empty;
            }

            return StringUtil.TrimStorageString(Encoding.Unicode.GetString(buffer, 0, requiredSize));
        }

        private static string GetParentDisplayName(uint parentDevInst)
        {
            var display = GetRawDevNodeRegistryPropertyString(parentDevInst, CmDeviceRegistryPropertyRaw.FriendlyName);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return StringUtil.TrimStorageString(display);
            }

            display = GetRawDevNodeRegistryPropertyString(parentDevInst, CmDeviceRegistryPropertyRaw.DeviceDescription);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return StringUtil.TrimStorageString(display);
            }

            display = GetRawDevNodeRegistryPropertyString(parentDevInst, CmDeviceRegistryPropertyRaw.Driver);
            if (!string.IsNullOrWhiteSpace(display))
            {
                return StringUtil.TrimStorageString(display);
            }

            return string.Empty;
        }

        private static string BuildControllerIdentifier(uint diskDevInst, string controllerService)
        {
            var physicalDeviceObjectName = GetRawDevNodeRegistryPropertyString(diskDevInst, CmDeviceRegistryPropertyRaw.PhysicalDeviceObjectName);
            if (string.IsNullOrWhiteSpace(physicalDeviceObjectName))
            {
                return string.Empty;
            }

            if (!physicalDeviceObjectName.StartsWith("\\Device\\0000", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string left = string.IsNullOrWhiteSpace(controllerService)
                ? StorageTextConstants.GenericControllerIdentifierPrefix
                : controllerService.Substring(0, Math.Min(3, controllerService.Length)).ToUpperInvariant();

            string right = physicalDeviceObjectName.Length >= 16
                ? physicalDeviceObjectName.Substring(12, Math.Min(4, physicalDeviceObjectName.Length - 12)).ToUpperInvariant()
                : string.Empty;

            return left + StorageTextConstants.ControllerIdentifierSeparator + right;
        }

        #endregion
    }
}
