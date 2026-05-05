/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using CommunityToolkit.Mvvm.ComponentModel;
using DiskInfoViewer.ModelAbstraction;

namespace DiskInfoViewer.ViewModels
{
    public partial class StorageViewModel : ViewModelBase
    {
        #region Constructor

        public StorageViewModel(StorageVM storage)
        {
            storage.Update();
            Storage = storage;

            _UpdateThread = new Thread(UpdateStorage);
            _UpdateThread.Name = $"{nameof(DiskInfoViewer)}.{nameof(UpdateStorage)}";
            _UpdateThread.IsBackground = true;
            _UpdateThread.Start();
        }

        #endregion

        #region Fields

        Thread _UpdateThread;

        #endregion

        #region Properties

        [ObservableProperty]
        StorageVM _storage;

        #endregion

        #region Private

        void UpdateStorage()
        {
            const int UpdateTimeInMilliseconds = 2500;

            while (true)
            {
                Thread.Sleep(UpdateTimeInMilliseconds);

                Storage.Update();
            }
        }

        #endregion
    }
}
