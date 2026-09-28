/*
* This Source Code Form is subject to the terms of the Mozilla Public
* License, v. 2.0. If a copy of the MPL was not distributed with this
* file, You can obtain one at https://mozilla.org/MPL/2.0/.
*
* Copyright (c) 2026 Florian K.
*/

using Avalonia.Data.Converters;
using Avalonia.Media;
using DiskInfoToolkit.Smart;
using System.Globalization;

namespace DiskInfoViewer.Converter
{
    internal class StorageHealthStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is StorageHealthStatus health)
            {
                switch (health)
                {
                    case StorageHealthStatus.Good:
                        return Brushes.Green;
                    case StorageHealthStatus.Caution:
                        return Brushes.Yellow;
                    case StorageHealthStatus.Warning:
                        return Brushes.Orange;
                    case StorageHealthStatus.Bad:
                        return Brushes.Red;
                }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
