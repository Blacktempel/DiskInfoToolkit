/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using System.Globalization;
using System.Resources;

namespace DiskInfoToolkit.Localization
{
    /// <summary>
    /// Represents the resource manager localized text provider.
    /// </summary>
    public sealed class ResourceManagerLocalizedTextProvider : ILocalizedTextProvider
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceManagerLocalizedTextProvider"/> class.
        /// </summary>
        /// <param name="resourceManager">The resource manager which provides localized strings.</param>
        public ResourceManagerLocalizedTextProvider(ResourceManager resourceManager)
            : this(resourceManager, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceManagerLocalizedTextProvider"/> class.
        /// </summary>
        /// <param name="resourceManager">The resource manager which provides localized strings.</param>
        /// <param name="culture">The culture for which to retrieve the localized strings.</param>
        public ResourceManagerLocalizedTextProvider(ResourceManager resourceManager, CultureInfo culture)
        {
            _resourceManager = resourceManager;
            _culture = culture;
        }

        #endregion

        #region Fields

        private readonly ResourceManager _resourceManager;

        private readonly CultureInfo _culture;

        #endregion

        #region Public

        /// <summary>
        /// Gets the text.
        /// </summary>
        /// <param name="textKey">The text key.</param>
        /// <returns>The get text.</returns>
        public string GetText(string textKey)
        {
            if (_resourceManager == null || string.IsNullOrWhiteSpace(textKey))
            {
                return string.Empty;
            }

            return _resourceManager.GetString(textKey, _culture) ?? string.Empty;
        }

        #endregion
    }
}
