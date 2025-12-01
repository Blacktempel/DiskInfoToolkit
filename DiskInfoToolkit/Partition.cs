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

using BlackSharp.Core.Interop.Windows.Utilities;
using DiskInfoToolkit.Enums.Interop;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Interop.Structures;
using DiskInfoToolkit.Structures.Interop;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit
{
    /// <summary>
    /// This represents a partition on a disk.
    /// </summary>
    public class Partition
    {
        #region Fields

        const int PARTITION_LDM = 0x42;

        static Guid GUID_DEVINTERFACE_VOLUME = new Guid("53f5630d-b6bf-11d0-94f2-00a0c91efb8b");

        static readonly Guid PARTITION_LDM_METADATA_GUID = new Guid("5808c8aa-7e8f-42e0-85d2-e1e90434cfb3");
        static readonly Guid PARTITION_LDM_DATA_GUID = new Guid("af9b60a0-1431-4f62-bc68-3311714a69ad");

        #endregion

        #region Properties

        /// <summary>
        /// Style of partition.
        /// </summary>
        public PartitionStyle PartitionStyle { get; internal set; }

        /// <summary>
        /// Partition information.
        /// </summary>
        public PartitionInformationUnion PartitionInformation { get; internal set; }

        /// <summary>
        /// Starting offset of this partition.
        /// </summary>
        public long StartingOffset { get; internal set; }

        /// <summary>
        /// Length of this partition.
        /// </summary>
        public long PartitionLength { get; internal set; }

        /// <summary>
        /// Number of this partition.
        /// </summary>
        public uint PartitionNumber { get; internal set; }

        /// <summary>
        /// Drive letter of this partition, if set.
        /// </summary>
        /// <remarks>Example: 'C' or <see langword="null"/> if not mapped to drive letter.</remarks>
        public char? DriveLetter { get; internal set; }

        /// <summary>
        /// Available free space on partition.
        /// </summary>
        public ulong? AvailableFreeSpace { get; internal set; }

        /// <summary>
        /// Identifies if this partition is on a dynamic disk (Windows).
        /// </summary>
        public bool IsDynamicDiskPartition { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the partition contains an operating system other than the current one.
        /// </summary>
        public bool IsOtherOperatingSystemPartition => CheckIsOtherOperatingSystemPartition();

        /// <summary>
        /// Volume path which is being used for <see cref="Kernel32.GetDiskFreeSpaceEx"/>.
        /// </summary>
        internal string VolumePath { get; set; }

        #endregion

        #region Internal

        internal static bool GetPartitions(IntPtr handle, int driveNumber, out List<Partition> partitions)
        {
            return UpdatePartitions(handle, driveNumber, out partitions);
        }

        #endregion

        #region Private

        static bool UpdatePartitions(IntPtr handle, int driveNumber, out List<Partition> partitions)
        {
            if (!ReadPartitions(handle, out partitions))
            {
                return false;
            }

            UpdateAllPartitionsVolumes(driveNumber, partitions);

            UpdateDriveLetters(driveNumber, partitions);

            return true;
        }

        static bool ReadPartitions(IntPtr handle, out List<Partition> partitions)
        {
            partitions = new List<Partition>();

            var driveLayoutEx = new DRIVE_LAYOUT_INFORMATION_EX();

            var partitionEntrySize = Marshal.SizeOf<PARTITION_INFORMATION_EX>() * 128;

            var size = Marshal.SizeOf<DRIVE_LAYOUT_INFORMATION_EX>() + partitionEntrySize;
            var ptr = Marshal.AllocHGlobal(size);

            Marshal.StructureToPtr(driveLayoutEx, ptr, false);

            if (!Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_DISK_GET_DRIVE_LAYOUT_EX, IntPtr.Zero, 0, ptr, size, out _, IntPtr.Zero))
            {
                Marshal.FreeHGlobal(ptr);

                return false;
            }

            driveLayoutEx = Marshal.PtrToStructure<DRIVE_LAYOUT_INFORMATION_EX>(ptr);

            var partitionEntriesOffset = Marshal.OffsetOf<DRIVE_LAYOUT_INFORMATION_EX>(nameof(driveLayoutEx.PartitionInformation));

            for (int i = 0; i < driveLayoutEx.PartitionCount; ++i)
            {
                var singlePartitionEntrySize = Marshal.SizeOf<PARTITION_INFORMATION_EX>();

                var where = singlePartitionEntrySize * i;

                var partitionPtr = new IntPtr(ptr.ToInt64() + partitionEntriesOffset.ToInt64() + where);

                var partitionInformation = Marshal.PtrToStructure<PARTITION_INFORMATION_EX>(partitionPtr);

                var partition = new Partition()
                {
                    PartitionStyle       = (PartitionStyle)partitionInformation.PartitionStyle,
                    PartitionInformation = partitionInformation.Layout,
                    StartingOffset       = partitionInformation.StartingOffset,
                    PartitionLength      = partitionInformation.PartitionLength,
                    PartitionNumber      = partitionInformation.PartitionNumber,
                };

                switch (partition.PartitionStyle)
                {
                    case PartitionStyle.PartitionStyleMBR:
                        partition.IsDynamicDiskPartition = partitionInformation.Layout.Mbr.PartitionType == PARTITION_LDM;
                        break;
                    case PartitionStyle.PartitionStyleGPT:
                        partition.IsDynamicDiskPartition =
                            partitionInformation.Layout.Gpt.PartitionType == PARTITION_LDM_METADATA_GUID
                         || partitionInformation.Layout.Gpt.PartitionType == PARTITION_LDM_DATA_GUID;
                        break;
                }

                partitions.Add(partition);
            }

            Marshal.FreeHGlobal(ptr);

            return true;
        }

        static void UpdateAllPartitionsVolumes(int driveNumber, List<Partition> partitions)
        {
            foreach (var partition in partitions)
            {
                if (partition.VolumePath == null)
                {
                    TryMatchPartitionForVolume(driveNumber, partition);
                }

                if (partition.VolumePath != null)
                {
                    //Get free space
                    if (Kernel32.GetDiskFreeSpaceEx(partition.VolumePath, out var freeBytes, out _, out _))
                    {
                        partition.AvailableFreeSpace = freeBytes;
                    }
                }
            }
        }

        static void TryMatchPartitionForVolume(int driveNumber, Partition partition)
        {
            // Setup Device Interface for Volumes
            IntPtr devInfo = SetupAPI.SetupDiGetClassDevs(ref GUID_DEVINTERFACE_VOLUME, IntPtr.Zero, IntPtr.Zero, StorageDetector.DIGCF_PRESENT | StorageDetector.DIGCF_DEVICEINTERFACE);

            if (!SafeFileHandler.IsHandleValid(devInfo))
            {
                return;
            }

            var interfaceData = new SP_DEVICE_INTERFACE_DATA();
            interfaceData.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

            uint index = 0;
            try
            {
                while (SetupAPI.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref GUID_DEVINTERFACE_VOLUME, index, ref interfaceData))
                {
                    ++index;

                    //Get required buffer size
                    SetupAPI.SetupDiGetDeviceInterfaceDetailW(devInfo, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);

                    var detailDataBuffer = Marshal.AllocHGlobal(requiredSize);

                    try
                    {
                        Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6); //Do not change that

                        //Get full device path and devInfo
                        if (SetupAPI.SetupDiGetDeviceInterfaceDetailW(devInfo, ref interfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                        {
                            var offset = Marshal.OffsetOf<SP_DEVICE_INTERFACE_DETAIL_DATA>(nameof(SP_DEVICE_INTERFACE_DETAIL_DATA.DevicePath)).ToInt64();

                            var pDevicePath = new IntPtr(detailDataBuffer.ToInt64() + offset);
                            string volumePath = Marshal.PtrToStringAnsi(pDevicePath);

                            var handle = SafeFileHandler.OpenHandle(volumePath);
                            if (!SafeFileHandler.IsHandleValid(handle))
                            {
                                continue;
                            }

                            //Get Disk Extents for this volume
                            var size = Marshal.SizeOf<VOLUME_DISK_EXTENTS>() + 64 * Marshal.SizeOf<DISK_EXTENT>();
                            var ptr = Marshal.AllocHGlobal(size);

                            try
                            {
                                if (!Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0, ptr, size, out _, IntPtr.Zero))
                                {
                                    continue;
                                }

                                var numberOfExtents = Marshal.ReadInt32(ptr);
                                offset = Marshal.OffsetOf<VOLUME_DISK_EXTENTS>(nameof(VOLUME_DISK_EXTENTS.Extents)).ToInt64();

                                for (int i = 0; i < numberOfExtents; ++i)
                                {
                                    var extentPtr = new IntPtr(ptr.ToInt64() + offset + i * Marshal.SizeOf<DISK_EXTENT>());
                                    var extent = Marshal.PtrToStructure<DISK_EXTENT>(extentPtr);

                                    //Check if partition belongs to this extent
                                    if (extent.DiskNumber == driveNumber
                                     && extent.StartingOffset == partition.StartingOffset)
                                    {
                                        const string Backslash = @"\";

                                        if (!volumePath.EndsWith(Backslash))
                                        {
                                            volumePath += Backslash;
                                        }

                                        //Set volume path
                                        partition.VolumePath = volumePath;

                                        return;
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(ptr);
                                SafeFileHandler.CloseHandle(handle);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            finally
            {
                SetupAPI.SetupDiDestroyDeviceInfoList(devInfo);
            }
        }

        static void UpdateDriveLetters(int driveNumber, List<Partition> partitions)
        {
            for (char c = 'A'; c < 'Z'; ++c)
            {
                var handleABC = SafeFileHandler.OpenHandle($@"\\.\{c}:");

                if (!SafeFileHandler.IsHandleValid(handleABC))
                {
                    continue;
                }

                try
                {
                    var volumeDiskExtents = new VOLUME_DISK_EXTENTS();

                    var diskExtentsSize = Marshal.SizeOf<DISK_EXTENT>() * 128;

                    var size = Marshal.SizeOf<VOLUME_DISK_EXTENTS>() + diskExtentsSize;
                    var ptr = Marshal.AllocHGlobal(size);

                    try
                    {
                        Marshal.StructureToPtr(volumeDiskExtents, ptr, false);

                        if (!Kernel32.DeviceIoControl(handleABC, Kernel32.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0, ptr, size, out _, IntPtr.Zero))
                        {
                            return;
                        }

                        volumeDiskExtents = Marshal.PtrToStructure<VOLUME_DISK_EXTENTS>(ptr);

                        var diskExtentsOffset = Marshal.OffsetOf<VOLUME_DISK_EXTENTS>(nameof(volumeDiskExtents.Extents));

                        for (int i = 0; i < volumeDiskExtents.NumberOfDiskExtents; ++i)
                        {
                            var singleDiskExtentSize = Marshal.SizeOf<DISK_EXTENT>();

                            var where = singleDiskExtentSize * i;

                            var diskExtentPtr = new IntPtr(ptr.ToInt64() + diskExtentsOffset.ToInt64() + where);

                            var diskExtent = Marshal.PtrToStructure<DISK_EXTENT>(diskExtentPtr);

                            var found = partitions.Find(p => driveNumber == diskExtent.DiskNumber
                                                          && p.StartingOffset == diskExtent.StartingOffset);

                            if (found != null)
                            {
                                //Assign drive letter
                                found.DriveLetter = c;
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                finally
                {
                    SafeFileHandler.CloseHandle(handleABC);
                }
            }
        }

        bool CheckIsOtherOperatingSystemPartition()
        {
            switch (PartitionStyle)
            {
                case PartitionStyle.PartitionStyleMBR:
                    //Linux filesystems types in MBR
                    var linuxTypes = new byte[]
                    {
                        0x82, //Linux swap
                        0x83, //Linux
                        0x8E, //Linux LVM
                        0xA5, //FreeBSD
                        0xA6, //OpenBSD
                        0xA8, //Mac OS X
                        0xAB, //Mac OS X Boot
                        0xAF, //Mac OS X HFS+
                    };

                    if (linuxTypes.Contains(PartitionInformation.Mbr.PartitionType))
                    {
                        return true;
                    }
                    break;
                case PartitionStyle.PartitionStyleGPT:
                    //Linux filesystem types in GPT
                    var linuxGuids = new Guid[]
                    {
                        new Guid("0FC63DAF-8483-4772-8E79-3D69D8477DE4"), //Linux filesystem
                        new Guid("0657FD6D-A4AB-43C4-84E5-0933C84B4F4F"), //Linux swap
                        new Guid("E6D6D379-F507-44C2-A23C-238F2A3DF928"), //Linux LVM
                    };

                    if (linuxGuids.Contains(PartitionInformation.Gpt.PartitionType))
                    {
                        return true;
                    }
                    break;
            }

            return false;
        }

        #endregion
    }
}
