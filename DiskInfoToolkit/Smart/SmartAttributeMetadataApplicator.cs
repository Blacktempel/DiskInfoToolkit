/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;

namespace DiskInfoToolkit.Smart
{
    internal static class SmartAttributeMetadataApplicator
    {
        #region Public

        public static void Apply(StorageDevice device)
        {
            if (device == null)
            {
                return;
            }

            device.SmartAttributeProfile = SmartAttributeProfileResolver.Resolve(device);

            if (device.SmartAttributes == null)
            {
                return;
            }

            var textProvider = Storage.GetTextProvider();

            foreach (var entry in device.SmartAttributes)
            {
                if (entry == null)
                {
                    continue;
                }

                var definition = SmartAttributeCatalog.GetDefinition(device.SmartAttributeProfile, entry.ID);

                entry.AttributeKey = definition != null ? definition.AttributeKey : string.Empty;
                entry.Name         = SmartAttributeCatalog.GetDisplayName(device.SmartAttributeProfile, entry.ID, textProvider);
            }
        }

        #endregion
    }
}
