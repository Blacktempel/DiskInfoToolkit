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

using DiskInfoToolkit.Interop.Structures;
using System.Runtime.InteropServices;
using System.Text;

namespace DiskInfoToolkit.Interop
{
    internal static class SetupAPI
    {
        const string DLLNAME = "setupapi.dll";

        [DllImport(DLLNAME, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            IntPtr Enumerator,
            IntPtr hwndParent,
            uint Flags);

        [DllImport(DLLNAME, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(
            IntPtr DeviceInfoSet,
            uint MemberIndex,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport(DLLNAME, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property,
            out uint PropertyRegDataType,
            char[] PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize);

        [DllImport(DLLNAME, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            ref uint propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize
            );

        [DllImport(DLLNAME, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet,
            IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid,
            uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport(DLLNAME, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            int DeviceInterfaceDetailDataSize,
            out int RequiredSize,
            IntPtr DeviceInfoData);

        [DllImport(DLLNAME, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            int DeviceInterfaceDetailDataSize,
            out int RequiredSize,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport(DLLNAME, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            ref SP_DEVICE_INTERFACE_DETAIL_DATA DeviceInterfaceDetailData,
            int DeviceInterfaceDetailDataSize,
            out int RequiredSize,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport(DLLNAME, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInstanceId(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            StringBuilder DeviceInstanceId,
            int DeviceInstanceIdSize,
            out int RequiredSize);

        [DllImport(DLLNAME, SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
    }
}
