/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using Avalonia.Threading;
using BlackSharp.Core.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskInfoToolkit;
using DiskInfoViewer.ViewModels;
using System.Collections.ObjectModel;

namespace DiskInfoViewer.ModelAbstraction
{
    public partial class StorageVM : ViewModelBase
    {
        #region Constructor

        public StorageVM(StorageDevice sd)
        {
            _Storage = sd;

            StorageController = _Storage.Controller.Name;
            DriveNumber       = (int)_Storage.StorageDeviceNumber;
            BusType           = _Storage.BusType;
            ControllerVendorName = _Storage.Controller.VendorName;
            ControllerDeviceName = _Storage.Controller.DeviceName;
            TotalSize         = _Storage.DiskSizeBytes.GetValueOrDefault();
            Model             = _Storage.ProductName;
            Firmware          = _Storage.ProductRevision;
            FirmwareRev       = _Storage.ProductRevision;
            SerialNumber      = _Storage.SerialNumber;
            IsDynamicDisk     = _Storage.IsDynamicDisk;
            TotalFreeSize     = _Storage.TotalPartitionFreeSpaceBytes;
        }

        #endregion

        #region Fields

        bool _Initialized = false;

        StorageDevice _Storage;

        #region Fixed

        public string         StorageController    { get; }
        public int            DriveNumber          { get; }
        public StorageBusType BusType              { get; }
        public string         ControllerVendorName { get; }
        public string         ControllerDeviceName { get; }
        public ulong          TotalSize            { get; }
        public string         Model                { get; }
        public string         Firmware             { get; }
        public string         FirmwareRev          { get; }
        public string         SerialNumber         { get; }

        [ObservableProperty]
        bool _showSerialNumber = false;

        #endregion

        #region Volatile

        [ObservableProperty]
        bool _isDevicePowerOn;

        [ObservableProperty]
        StorageHealthStatus? _healthStatus;

        [ObservableProperty]
        string _healthStatusReason;

        [ObservableProperty]
        bool _isSmartSupported;

        [ObservableProperty]
        int? _temperature;

        [ObservableProperty]
        int? _temperatureWarning;

        [ObservableProperty]
        int? _temperatureCritical;

        [ObservableProperty]
        int? _life;

        [ObservableProperty]
        ulong? _hostReads;

        [ObservableProperty]
        ulong? _hostWrites;

        [ObservableProperty]
        ulong? _powerOnCount;

        [ObservableProperty]
        ulong? _powerOnHours;

        [ObservableProperty]
        bool _isDynamicDisk;

        [ObservableProperty]
        ulong? _totalFreeSize;

        [ObservableProperty]
        ObservableCollection<PartitionVM> _partitions = new();

        [ObservableProperty]
        ObservableCollectionEx<SmartAttributeVM> _smartAttributes = new();

        #endregion

        #endregion

        #region Public

        public bool EqualsStorage(StorageDevice other)
        {
            return _Storage == other;
        }

        public void Update()
        {
            bool isDevicePowerOn = _Storage.IsDevicePowerOn.GetValueOrDefault();

            //If device is powered off we don't want to cause a spin up by refreshing the disk,
            //so we skip entire update
            if (isDevicePowerOn)
            {
                //Update disk
                if (!Storage.Refresh(_Storage))
                {
                    if (!_Initialized)
                    {
                        _Initialized = true;
                    }
                    else
                    {
                        //No changes
                        return;
                    }
                }
            }

            Dispatcher.UIThread.Invoke(() =>
            {
                IsDevicePowerOn = isDevicePowerOn;

                //Skip update as device is powered off
                if (!IsDevicePowerOn)
                {
                    return;
                }

                //Update volatile properties
                HealthStatus        = _Storage.HealthStatus;
                HealthStatusReason  = _Storage.HealthStatusReason;
                IsSmartSupported    = _Storage.SupportsSmart;

                Temperature         = _Storage.Temperature;
                TemperatureWarning  = _Storage.TemperatureWarning;
                TemperatureCritical = _Storage.TemperatureCritical;
                Life                = _Storage.Health;
                HostReads           = _Storage.HostReads;
                HostWrites          = _Storage.HostWrites;
                PowerOnCount        = _Storage.PowerOnCount;
                PowerOnHours        = _Storage.PowerOnHours;
                IsDynamicDisk       = _Storage.IsDynamicDisk;
                TotalFreeSize       = _Storage.TotalPartitionFreeSpaceBytes;

                //Partitions can be changed, added or removed

                //Get added partitions
                var added = _Storage.Partitions
                    .Where(p => !Partitions.Any(vm => vm.EqualsPartition(p)))
                    .ToList();

                //Get removed partitions
                var removed = Partitions
                    .Where(vm => !_Storage.Partitions.Any(p => vm.EqualsPartition(p)))
                    .ToList();

                //Update existing first
                foreach (var partition in Partitions)
                {
                    var found = _Storage.Partitions.Find(partition.EqualsPartition);

                    //Exists
                    if (found != null)
                    {
                        partition.Update(found);
                    }
                }

                //Add new partitions
                foreach (var newPart in added)
                {
                    var partition = new PartitionVM(newPart);
                    Partitions.Add(partition);
                }

                //Remove partitions
                foreach (var remove in removed)
                {
                    Partitions.Remove(remove);
                }

                //Update attributes
                foreach (var attribute in _Storage.SmartAttributes)
                {
                    //Try to find attribute
                    var found = SmartAttributes.Find(sa => sa.EqualsSmartAttribute(attribute));

                    //Found attribute, update it
                    if (found != null)
                    {
                        found.Update(attribute);
                    }
                    else //Not found, add it
                    {
                        var vm = new SmartAttributeVM(attribute);
                        SmartAttributes.Add(vm);
                    }
                }
            });
        }

        #endregion
    }
}
