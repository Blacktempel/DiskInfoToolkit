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
using DiskInfoToolkit.Disk;
using DiskInfoToolkit.Enums;
using DiskInfoToolkit.Enums.Interop;
using DiskInfoToolkit.Globals;
using DiskInfoToolkit.HardDrive;
using DiskInfoToolkit.Identifiers;
using DiskInfoToolkit.Internal;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Interop.Enums;
using DiskInfoToolkit.Interop.Structures;
using DiskInfoToolkit.Logging;
using DiskInfoToolkit.Usb;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit
{
    /// <summary>
    /// Represents a storage medium.
    /// </summary>
    public sealed class Storage : IDisposable
    {
        #region Constructor

        internal Storage(string storageController, StorageDevice sdi)
        {
#if DEBUG
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
#endif

            if (sdi == null)
            {
                throw new ArgumentNullException(nameof(sdi));
            }

            StorageController      = storageController;
            _StorageDeviceInternal = sdi;

            var handle = SafeFileHandler.OpenHandle(_StorageDeviceInternal.PhysicalPath);

            if (!SafeFileHandler.IsHandleValid(handle))
            {
                LogSimple.LogTrace($"Handle for {nameof(Storage)} is invalid.");

                IsValid = false;
                return;
            }
            else
            {
                LogSimple.LogTrace($"Handle for {nameof(Storage)} is open.");
                LogSimple.LogTrace($"{nameof(sdi.PhysicalPath)} = '{sdi.PhysicalPath}'.");
            }

            IsValid = IdentifyStorageController();

            if (!IsValid)
            {
                return;
            }

            GetDiskGeometry(handle);

            IsValid = GetDiskInformation(handle);

            if (!IsValid)
            {
                return;
            }

            UpdatePartitions(handle);

            IsValid = IdentifyDisk(handle);

            SafeFileHandler.CloseHandle(handle);

#if DEBUG
            sw.Stop();

            LogSimple.LogDebug($"Initialization of {nameof(Storage)} took {sw.Elapsed}.");
#endif
        }

        ~Storage()
        {
            Dispose();
        }

        #endregion

        #region Fields

        bool _Disposed;

        StorageDevice _StorageDeviceInternal;

        #endregion

        #region Properties

        #region Internal

        internal bool IsValid { get; private set; }

        internal static bool HasNVMeStorageQuery => Environment.OSVersion.Version.Major >= 10;

        internal static bool IsARM => RuntimeInformation.ProcessArchitecture == Architecture.Arm
                                   || RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        internal HostReadsWritesUnit HostReadsWritesUnit { get; set; }

        internal NandWritesUnit NandWritesUnit { get; set; } = NandWritesUnit.NandWritesGB;

        internal COMMAND_TYPE Command { get; set; }

        internal byte Target { get; set; }

        internal int SiliconImageType { get; private set; }

        internal float TemperatureMultiplier { get; set; } = 1.0f;

        #endregion

        #region Fixed

        /// <summary>
        /// Smart key which identifies storage medium.
        /// </summary>
        public SmartKey SmartKey { get; set; }

        /// <summary>
        /// Indicates if this is a NVMe disk.
        /// </summary>
        public bool IsNVMe { get; internal set; }

        /// <summary>
        /// Indicates if this is a SSD disk.
        /// </summary>
        public bool IsSSD { get; internal set; }

        /// <summary>
        /// Name of storage controller of <see cref="Storage"/>.
        /// </summary>
        public string StorageController { get; private set; }

        /// <summary>
        /// Type of storage controller.
        /// </summary>
        public StorageControllerType StorageControllerType { get; private set; }
            = StorageControllerType.Unknown;

        /// <summary>
        /// Vendor ID of disk.
        /// </summary>
        public ushort? VendorID { get; internal set; }

        /// <summary>
        /// Vendor as string, if available.
        /// </summary>
        public string Vendor { get; internal set; }

        /// <summary>
        /// Product ID of disk.
        /// </summary>
        public ushort? ProductID { get; internal set; }

        /// <summary>
        /// Number of drive.
        /// </summary>
        public int DriveNumber => _StorageDeviceInternal.DriveNumber;

        /// <summary>
        /// Device path with which a handle can be opened.
        /// </summary>
        public string PhysicalPath => _StorageDeviceInternal.PhysicalPath;

        /// <summary>
        /// ID of device.
        /// </summary>
        public string DeviceID => _StorageDeviceInternal.DeviceID;

        /// <summary>
        /// Bus type of this instance.
        /// </summary>
        public StorageBusType BusType { get; private set; }

        /// <summary>
        /// Gets the total size of storage space on a drive, in bytes.
        /// </summary>
        public ulong TotalSize { get; internal set; }

        /// <summary>
        /// Model name of device.
        /// </summary>
        public string Model { get; internal set; }

        /// <summary>
        /// Firmware of device.
        /// </summary>
        public string Firmware { get; internal set; }

        /// <summary>
        /// Firmware revision of device.
        /// </summary>
        public string FirmwareRev { get; internal set; }

        /// <summary>
        /// Serial number of device.
        /// </summary>
        public string SerialNumber { get; internal set; }

        /// <summary>
        /// Detected time unit type.
        /// </summary>
        public TimeUnitType DetectedTimeUnitType { get; internal set; }

        /// <summary>
        /// Measured time unit type.
        /// </summary>
        public TimeUnitType MeasuredTimeUnitType { get; internal set; }

        /// <summary>
        /// Contains ATA information.
        /// </summary>
        public ATAInfo ATAInfo { get; internal set; }

        #endregion

        #region Volatile

        /// <summary>
        /// Identifies if this device is a dynamic disk (Windows).
        /// </summary>
        public bool IsDynamicDisk => Partitions.Any(p => p.IsDynamicDiskPartition);

        /// <summary>
        /// Gets the total free size of storage space on a drive, in bytes.
        /// </summary>
        /// <remarks>Calculation (all <see cref="Partitions"/>): <see cref="TotalSize"/> - <see cref="Partition.PartitionLength"/> + <see cref="Partition.AvailableFreeSpace"/>.<br/>
        /// This will not return reliable data if this device is a dynamic disk (check <see cref="IsDynamicDisk"/>).</remarks>
        public ulong TotalFreeSize => GetTotalFreeSize();

        /// <summary>
        /// Smart information of drive.
        /// </summary>
        public SmartInfo Smart { get; internal set; } = new();

        /// <summary>
        /// List of all partitions on drive.
        /// </summary>
        /// <remarks>Partitions do not reflect partitions on a dynamic disk (Windows) - check <see cref="IsDynamicDisk"/>.</remarks>
        public List<Partition> Partitions { get; private set; } = new();

        #endregion

        #region Features

        public bool IsTrimSupported { get; internal set; }

        public bool IsVolatileWriteCachePresent { get; internal set; }

        #endregion

        #endregion

        #region Public

        /// <summary>
        /// Updates all volatile data.
        /// </summary>
        public void Update()
        {
            var handle = SafeFileHandler.OpenHandle(_StorageDeviceInternal.PhysicalPath);

            DiskHandler.UpdateSmartInfo(this, handle);

            UpdatePartitions(handle);

            SafeFileHandler.CloseHandle(handle);
        }

        public void Dispose()
        {
            if (!_Disposed)
            {
                _Disposed = true;
            }
        }

        #endregion

        #region Internal

        internal static int GetDriveNumber(IntPtr handle)
        {
            if (Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, out var sdn, Marshal.SizeOf<STORAGE_DEVICE_NUMBER>(), out _, IntPtr.Zero))
            {
                return sdn.DeviceNumber;
            }

            return -1;
        }

        internal static bool ModelContains(Storage storage, string text)
        {
            return storage.Model.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ModelStartsWith(Storage storage, string text)
        {
            return storage.Model.StartsWith(text, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Private

        void UpdatePartitions(IntPtr handle)
        {
            List<Partition> partitions;
            Partition.GetPartitions(handle, DriveNumber, out partitions);

            Partitions.Clear();
            Partitions.AddRange(partitions);
        }

        ulong GetTotalFreeSize()
        {
            //Sum of free space on partitions
            var partitionsFree = Partitions.Sum(p => (long?)p.AvailableFreeSpace);

            //Sum of partition sizes
            var partitionSizes = Partitions.Sum(p => p.PartitionLength);

            //Calculate total free size
            var totalFree = TotalSize - (ulong)partitionSizes + (ulong)partitionsFree.GetValueOrDefault();

            return totalFree;
        }

        void UsbNVMeCheck()
        {
            //USB device
            if (BusType == StorageBusType.BusTypeUsb
             || ModelContains(this, "USB Device")
             || StorageControllerType == StorageControllerType.UASP)
            {
                if (ModelContains(this, "NVME")
                 || ModelContains(this, "Optane")
                 || PhysicalPath.Contains("NVME", StringComparison.OrdinalIgnoreCase)
                 || PhysicalPath.Contains("Optane", StringComparison.OrdinalIgnoreCase))
                {
                    IsNVMe = true;
                }
                else //Check for USB ID mapping
                {
                    var venLoc = _StorageDeviceInternal.HardwareID.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
                    var devLoc = _StorageDeviceInternal.HardwareID.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);

                    if (venLoc != -1 && devLoc != -1)
                    {
                        var venStr = _StorageDeviceInternal.HardwareID.Substring(venLoc + 4, 4);
                        var devStr = _StorageDeviceInternal.HardwareID.Substring(devLoc + 4, 4);

                        int venID = Convert.ToInt32(venStr, 16);
                        int devID = Convert.ToInt32(devStr, 16);

                        VendorID  = (ushort)venID;
                        ProductID = (ushort)devID;

                        var vendor = USBIDReader.UsbVendors.FirstOrDefault(v => v.ID == venID);
                        if (vendor != null)
                        {
                            Vendor = vendor.Name;

                            var device = vendor.Devices.FirstOrDefault(d => d.ID == devID);

                            if (device != null)
                            {
                                if (vendor.Name.Contains("NVME"  , StringComparison.OrdinalIgnoreCase)
                                 || vendor.Name.Contains("Optane", StringComparison.OrdinalIgnoreCase)
                                 || device.Name.Contains("NVME"  , StringComparison.OrdinalIgnoreCase)
                                 || device.Name.Contains("Optane", StringComparison.OrdinalIgnoreCase))
                                {
                                    IsNVMe = true;
                                }
                            }
                        }
                    }
                }
            }
            else //No USB device, read vendor normally
            {
                var venLoc = _StorageDeviceInternal.HardwareID.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
                var devLoc = _StorageDeviceInternal.HardwareID.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);

                if (venLoc != -1 && devLoc != -1)
                {
                    var venStr = _StorageDeviceInternal.HardwareID.Substring(venLoc + 4, 4);
                    var devStr = _StorageDeviceInternal.HardwareID.Substring(devLoc + 4, 4);

                    int venID = Convert.ToInt32(venStr, 16);
                    int devID = Convert.ToInt32(devStr, 16);

                    VendorID  = (ushort)venID;
                    ProductID = (ushort)devID;
                }
            }
        }

        bool IdentifyStorageController()
        {
            if (StorageController.Contains("USB") || StorageController.Contains("UAS"))
            {
                StorageControllerType = StorageControllerType.UASP;
            }

            if (StorageController.Contains("VIA VT6410")
             || StorageController.Contains("ITE IT8212"))
            {
                StorageControllerType = StorageControllerType.BlackList;
            }

            if (StorageController.Contains("NVIDIA"))
            {
                StorageControllerType = StorageControllerType.Nvidia;
            }

            if (StorageController.Contains("Marvell"))
            {
                StorageControllerType = StorageControllerType.Marvell;
            }

            if (StorageController.Contains("DVDFab Virtual Drive"))
            {
                StorageControllerType = StorageControllerType.DVDFabVirtualDrive;
            }

            if (StorageController.Contains("Silicon Image SiI "))
            {
                StorageControllerType = StorageControllerType.SiliconImage;

                var number = StorageController.Replace("Silicon Image SiI ", string.Empty);

                if (!int.TryParse(number, out var imgType))
                {
                    return false;
                }

                SiliconImageType = imgType;
            }
            else if (StorageController.Contains("BUFFALO IFC-PCI2ES"))
            {
                StorageControllerType = StorageControllerType.SiliconImage;

                SiliconImageType = 3112;
            }
            else if (StorageController.Contains("BUFFALO IFC-PCIE2SA"))
            {
                StorageControllerType = StorageControllerType.SiliconImage;

                SiliconImageType = 3132;
            }

            return true;
        }

        bool IdentifyDisk(IntPtr handle)
        {
            // [2010/12/05] Workaround for SAMSUNG HD204UI
            // http://sourceforge.net/apps/trac/smartmontools/wiki/SamsungF4EGBadBlocks
            if (ModelContains(this, "SAMSUNG HD155UI")
             || ModelContains(this, "SAMSUNG HD204UI")
             && Firmware.Contains("1AQ10003", StringComparison.OrdinalIgnoreCase)
               )
            {
                return false;
            }

            // [2018/10/24] Workaround for FuzeDrive (AMDStoreMi)
            // http://sourceforge.net/apps/trac/smartmontools/wiki/SamsungF4EGBadBlocks
            if (ModelContains(this, "FuzeDrive")
             || ModelContains(this, "StoreMI")
               )
            {
                return false;
            }

            UsbNVMeCheck();

            if (!DeviceIdentifier.IdentifyDisk(this, handle))
            {
                LogSimple.LogWarn($"{nameof(DeviceIdentifier.IdentifyDisk)} unsuccessful.");
                return false;
            }

            return true;
        }

        void GetDiskGeometry(IntPtr handle)
        {
            var size = Marshal.SizeOf<DISK_GEOMETRY_EX>();

            var buffer = Marshal.AllocHGlobal(size);

            if (Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, IntPtr.Zero, 0, buffer, size, out _, IntPtr.Zero))
            {
                var geometry = Marshal.PtrToStructure<DISK_GEOMETRY_EX>(buffer);

                TotalSize = (ulong)(geometry.DiskSize);
            }

            Marshal.FreeHGlobal(buffer);
        }

        bool GetDiskInformation(IntPtr handle)
        {
            var query = new STORAGE_PROPERTY_QUERY()
            {
                PropertyId = STORAGE_PROPERTY_ID.StorageDeviceProperty,
                QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery,
            };

            var querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
            var queryPtr = Marshal.AllocHGlobal(querySize);
            Marshal.StructureToPtr(query, queryPtr, false);

            var outBuffer = Marshal.AllocHGlobal(SharedConstants.BUFFER_SIZE);

            if (Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_STORAGE_QUERY_PROPERTY, queryPtr, querySize, outBuffer, SharedConstants.BUFFER_SIZE, out _, IntPtr.Zero))
            {
                var descriptor = Marshal.PtrToStructure<STORAGE_DEVICE_DESCRIPTOR>(outBuffer);

                Model        = Marshal.PtrToStringAnsi(outBuffer + descriptor.ProductIdOffset      );
                SerialNumber = Marshal.PtrToStringAnsi(outBuffer + descriptor.SerialNumberOffset   );
                Firmware     = Marshal.PtrToStringAnsi(outBuffer + descriptor.ProductRevisionOffset);
                BusType      = descriptor.BusType;

                //Is removable media ?
                if (BusType == StorageBusType.BusTypeUsb
                 && descriptor.RemovableMedia == 1)
                {
                    //Is possibly a SD Card reader ?
                    if (ModelContains(this, "SD Card"))
                    {
                        Marshal.FreeHGlobal(outBuffer);
                        Marshal.FreeHGlobal(queryPtr);

                        //Skip that
                        return false;
                    }
                }
            }

            Marshal.FreeHGlobal(outBuffer);
            Marshal.FreeHGlobal(queryPtr);

            return true;
        }

        #endregion
    }
}
