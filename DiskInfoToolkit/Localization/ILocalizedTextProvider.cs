/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Localization
{
    /// <summary>
    /// Defines a contract for providing localized or context-specific text based on a key.
    /// </summary>
    public interface ILocalizedTextProvider
    {
        string GetText(string textKey);
    }
}
