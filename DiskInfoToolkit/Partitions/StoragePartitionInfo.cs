/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Partitions
{
    /// <summary>
    /// Represents storage partition info.
    /// </summary>
    public sealed class StoragePartitionInfo : IEquatable<StoragePartitionInfo>
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="StoragePartitionInfo"/> class.
        /// </summary>
        public StoragePartitionInfo()
        {
            VolumePath = string.Empty;
            GptName = string.Empty;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the partition style.
        /// </summary>
        public DiskPartitionStyle PartitionStyle { get; set; }

        /// <summary>
        /// Gets or sets the starting offset.
        /// </summary>
        public long StartingOffset { get; set; }

        /// <summary>
        /// Gets or sets the partition length.
        /// </summary>
        public long PartitionLength { get; set; }

        /// <summary>
        /// Gets or sets the partition number.
        /// </summary>
        public uint PartitionNumber { get; set; }

        /// <summary>
        /// Gets or sets the rewrite partition.
        /// </summary>
        public bool RewritePartition { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether service partition.
        /// </summary>
        public bool IsServicePartition { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether dynamic disk partition.
        /// </summary>
        public bool IsDynamicDiskPartition { get; set; }

        /// <summary>
        /// Gets or sets the drive letter.
        /// </summary>
        public char? DriveLetter { get; set; }

        /// <summary>
        /// Gets or sets the volume path.
        /// </summary>
        public string VolumePath { get; set; }

        /// <summary>
        /// Gets or sets the available free space bytes.
        /// </summary>
        public ulong? AvailableFreeSpaceBytes { get; set; }

        /// <summary>
        /// Gets or sets the mbr partition type.
        /// </summary>
        public byte? MbrPartitionType { get; set; }

        /// <summary>
        /// Gets or sets the mbr boot indicator.
        /// </summary>
        public bool? MbrBootIndicator { get; set; }

        /// <summary>
        /// Gets or sets the mbr recognized partition.
        /// </summary>
        public bool? MbrRecognizedPartition { get; set; }

        /// <summary>
        /// Gets or sets the mbr partition id.
        /// </summary>
        public Guid? MbrPartitionID { get; set; }

        /// <summary>
        /// Gets or sets the gpt partition type.
        /// </summary>
        public Guid? GptPartitionType { get; set; }

        /// <summary>
        /// Gets or sets the gpt partition id.
        /// </summary>
        public Guid? GptPartitionID { get; set; }

        /// <summary>
        /// Gets or sets the gpt attributes.
        /// </summary>
        public ulong? GptAttributes { get; set; }

        /// <summary>
        /// Gets or sets the gpt name.
        /// </summary>
        public string GptName { get; set; }

        /// <summary>
        /// Gets a value indicating whether the partition appears to belong to another operating system.
        /// </summary>
        public bool IsOtherOperatingSystemPartition
        {
            get
            {
                if (MbrPartitionType.HasValue)
                {
                    return MbrPartitionType.Value == 0x82 //Linux swap
                        || MbrPartitionType.Value == 0x83 //Linux
                        || MbrPartitionType.Value == 0x8E //Linux LVM
                        || MbrPartitionType.Value == 0xA5 //FreeBSD
                        || MbrPartitionType.Value == 0xA6 //OpenBSD
                        || MbrPartitionType.Value == 0xA8 //Mac OS X
                        || MbrPartitionType.Value == 0xAB //Mac OS X Boot
                        || MbrPartitionType.Value == 0xAF //Mac OS X HFS+
                    ;
                }

                if (GptPartitionType.HasValue)
                {
                    return GptPartitionType.Value == new Guid("0FC63DAF-8483-4772-8E79-3D69D8477DE4") //Linux filesystem
                        || GptPartitionType.Value == new Guid("0657FD6D-A4AB-43C4-84E5-0933C84B4F4F") //Linux swap
                        || GptPartitionType.Value == new Guid("E6D6D379-F507-44C2-A23C-238F2A3DF928") //Linux LVM
                    ;
                }

                return false;
            }
        }

        #endregion

        #region Operators

        public static bool operator ==(StoragePartitionInfo left, StoragePartitionInfo right)
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

        public static bool operator !=(StoragePartitionInfo left, StoragePartitionInfo right)
        {
            return !(left == right);
        }

        #endregion

        #region Public

        public override bool Equals(object obj)
        {
            return Equals(obj as StoragePartitionInfo);
        }

        public bool Equals(StoragePartitionInfo other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return PartitionStyle  == other.PartitionStyle
                && StartingOffset  == other.StartingOffset
                && PartitionLength == other.PartitionLength
                && PartitionNumber == other.PartitionNumber;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = 17;
                hashCode = (hashCode * 31) + PartitionStyle .GetHashCode();
                hashCode = (hashCode * 31) + StartingOffset .GetHashCode();
                hashCode = (hashCode * 31) + PartitionLength.GetHashCode();
                hashCode = (hashCode * 31) + PartitionNumber.GetHashCode();
                return hashCode;
            }
        }

        #endregion
    }
}
