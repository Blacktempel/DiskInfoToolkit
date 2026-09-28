/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using BlackSharp.Core.Asynchronous;
using BlackSharp.Core.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskInfoToolkit;
using DiskInfoToolkit.Monitoring;

namespace DiskInfoViewer.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        #region Constructor

        public MainWindowViewModel()
        {
        }

        #endregion

        #region Properties

        [ObservableProperty]
        ObservableCollectionEx<StorageViewModel> _storageVMs;

        [ObservableProperty]
        bool _isBusy;

        [ObservableProperty]
        string _busyMessage = "Please wait";

        #endregion

        #region Commands

        [RelayCommand]
        void RefreshStorages()
        {
            IsBusy = true;

            Storage.DevicesChanged -= OnStoragesChanged;

            Executor.Run(() =>
            {
                var disks = Storage.GetDisks();

                var storages = new ObservableCollectionEx<StorageViewModel>(
                                    disks
                                        .OrderBy(s => s.StorageDeviceNumber)
                                        .Select(s => new StorageViewModel(new(s))));

                Storage.DevicesChanged += OnStoragesChanged;

                return () =>
                {
                    StorageVMs = storages;

                    IsBusy = false;
                };
            });
        }

        #endregion

        #region Private

        void OnStoragesChanged(object sender, StorageDevicesChangedEventArgs e)
        {
            if (!e.HasChanges)
            {
                return;
            }

            e.Added.ForEach(added =>
            {
                StorageVMs.Add(new(new(added)));
            });

            e.Removed.ForEach(removed =>
            {
                var removedVM = StorageVMs.FirstOrDefault(s => s.Storage.EqualsStorage(removed));
                if (removedVM != null)
                {
                    StorageVMs.Remove(removedVM);
                }
            });
        }

        #endregion
    }
}
