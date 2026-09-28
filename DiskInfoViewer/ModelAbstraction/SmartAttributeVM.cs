/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using CommunityToolkit.Mvvm.ComponentModel;
using DiskInfoToolkit.Smart;
using DiskInfoViewer.ViewModels;

namespace DiskInfoViewer.ModelAbstraction
{
    public partial class SmartAttributeVM : ViewModelBase
    {
        #region Constructor

        public SmartAttributeVM(SmartAttributeEntry smartAttributeEntry)
        {
            Update(smartAttributeEntry);
        }

        #endregion

        #region Properties

        [ObservableProperty]
        byte _ID;

        [ObservableProperty]
        string _name;

        [ObservableProperty]
        ulong _value;

        #endregion

        #region Public

        public bool EqualsSmartAttribute(SmartAttributeEntry other)
        {
            return ID == other.ID;
        }

        public void Update(SmartAttributeEntry smartAttributeEntry)
        {
            ID    = smartAttributeEntry.ID;
            Name  = smartAttributeEntry.Name;
            Value = smartAttributeEntry.RawValue;
        }

        #endregion
    }
}
