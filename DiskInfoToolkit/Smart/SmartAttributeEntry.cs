/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Smart
{
    /// <summary>
    /// Represents a SMART attribute entry.
    /// </summary>
    public sealed class SmartAttributeEntry : IEquatable<SmartAttributeEntry>
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="SmartAttributeEntry"/> class.
        /// </summary>
        public SmartAttributeEntry()
        {
            AttributeKey = string.Empty;
            Name = string.Empty;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        public byte ID { get; set; }

        /// <summary>
        /// Gets or sets the status flags.
        /// </summary>
        public ushort StatusFlags { get; set; }

        /// <summary>
        /// Gets or sets the current value.
        /// </summary>
        public byte CurrentValue { get; set; }

        /// <summary>
        /// Gets or sets the worst value.
        /// </summary>
        public byte WorstValue { get; set; }

        /// <summary>
        /// Gets or sets the raw value.
        /// </summary>
        public ulong RawValue { get; set; }

        /// <summary>
        /// Gets or sets the threshold value.
        /// </summary>
        public byte ThresholdValue { get; set; }

        /// <summary>
        /// Gets or sets the attribute key.
        /// </summary>
        public string AttributeKey { get; set; }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets the resource text key for the attribute name.
        /// </summary>
        public string TextKey
        {
            get
            {
                return SmartTextKeys.GetAttributeNameKey(AttributeKey);
            }
        }

        #endregion

        #region Operators

        public static bool operator ==(SmartAttributeEntry left, SmartAttributeEntry right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null || right is null)
            {
                return false;
            }

            return left.Equals(right);
        }

        public static bool operator !=(SmartAttributeEntry left, SmartAttributeEntry right)
        {
            return !(left == right);
        }

        #endregion

        #region Public

        public override bool Equals(object obj)
        {
            return Equals(obj as SmartAttributeEntry);
        }

        public bool Equals(SmartAttributeEntry other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return ID == other.ID
                && string.Equals(TextKey, other.TextKey, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 17;
                hashCode = (hashCode * 31) + ID.GetHashCode();
                hashCode = (hashCode * 31) + (TextKey != null ? StringComparer.Ordinal.GetHashCode(TextKey) : 0);
                return hashCode;
            }
        }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        /// <returns>The get display name.</returns>
        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(Name))
            {
                return Name;
            }

            return SmartAttributeCatalog.BuildUnknownAttributeName(ID);
        }

        #endregion
    }
}
