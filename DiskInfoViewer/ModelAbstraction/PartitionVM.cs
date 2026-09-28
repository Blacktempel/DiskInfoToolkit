/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using CommunityToolkit.Mvvm.ComponentModel;
using DiskInfoToolkit.Partitions;
using DiskInfoViewer.ViewModels;

namespace DiskInfoViewer.ModelAbstraction
{
    public partial class PartitionVM : ViewModelBase
    {
        #region Constructor

        public PartitionVM(StoragePartitionInfo storagePartitionInfo)
        {
            Update(storagePartitionInfo);
        }

        #endregion

        #region Properties

        [ObservableProperty]
        DiskPartitionStyle _partitionStyle;

        [ObservableProperty]
        long _startingOffset;

        [ObservableProperty]
        long _partitionLength;

        [ObservableProperty]
        uint _partitionNumber;

        [ObservableProperty]
        char? _driveLetter;

        [ObservableProperty]
        ulong? _availableFreeSpace;

        #endregion

        #region Public

        public bool EqualsPartition(StoragePartitionInfo other)
        {
            return PartitionStyle  == other.PartitionStyle
                && StartingOffset  == other.StartingOffset
                && PartitionLength == other.PartitionLength
                && PartitionNumber == other.PartitionNumber;
        }

        public void Update(StoragePartitionInfo partition)
        {
            PartitionStyle     = partition.PartitionStyle         ;
            StartingOffset     = partition.StartingOffset         ;
            PartitionLength    = partition.PartitionLength        ;
            PartitionNumber    = partition.PartitionNumber        ;
            DriveLetter        = partition.DriveLetter            ;
            AvailableFreeSpace = partition.AvailableFreeSpaceBytes;
        }

        #endregion
    }
}
