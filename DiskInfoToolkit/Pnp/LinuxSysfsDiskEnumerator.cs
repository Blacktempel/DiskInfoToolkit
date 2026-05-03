/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Native;
using DiskInfoToolkit.Utilities;
using System.Globalization;

namespace DiskInfoToolkit.Pnp
{
    internal static class LinuxSysfsDiskEnumerator
    {
        #region Fields

        private const string SysBlockPath = "/sys/block";

        private const string DevPath = "/dev";

        #endregion

        #region Public

        public static List<PnpDiskNode> EnumerateDiskInterfaces()
        {
            var result = new List<PnpDiskNode>();

            if (!Directory.Exists(SysBlockPath))
            {
                return result;
            }

            foreach (var blockDirectory in Directory.EnumerateDirectories(SysBlockPath))
            {
                string name = Path.GetFileName(blockDirectory);
                if (ShouldSkipBlockDevice(name))
                {
                    continue;
                }

                string devicePath = Path.Combine(DevPath, name);
                if (!File.Exists(devicePath))
                {
                    continue;
                }

                result.Add(CreateNode(blockDirectory, name, devicePath));
            }

            return result;
        }

        #endregion

        #region Private

        private static bool ShouldSkipBlockDevice(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            return name.StartsWith("loop", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ram", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("zram", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("dm-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("md", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("fd", StringComparison.OrdinalIgnoreCase);
        }

        private static PnpDiskNode CreateNode(string blockDirectory, string name, string devicePath)
        {
            string deviceDirectory = Path.Combine(blockDirectory, "device");
            string model = ReadSysfsString(deviceDirectory, "model");
            string vendor = ReadSysfsString(deviceDirectory, "vendor");
            string revision = ReadSysfsString(deviceDirectory, "rev");
            string serial = ReadSysfsString(deviceDirectory, "serial");
            string driver = ResolveDriverName(deviceDirectory);
            string subsystem = ResolveSubsystemName(deviceDirectory);
            string hardwareId = BuildHardwareId(deviceDirectory);
            string controllerName = ResolveControllerName(deviceDirectory, driver, subsystem);

            var node = new PnpDiskNode();
            node.DevicePath = devicePath;
            node.DeviceInstanceID = ResolveRealPath(blockDirectory);
            node.ParentInstanceID = ResolveRealPath(deviceDirectory);
            node.DeviceDescription = StringUtil.FirstNonEmpty(model, name);
            node.FriendlyName = StringUtil.FirstNonEmpty(model, name);
            node.HardwareID = hardwareId;
            node.ParentHardwareID = hardwareId;
            node.ParentClass = ResolveControllerClass(driver, subsystem, name);
            node.ParentService = driver;
            node.ParentDisplayName = controllerName;
            node.ControllerIdentifier = BuildControllerIdentifier(driver, hardwareId, deviceDirectory);

            if (!string.IsNullOrWhiteSpace(vendor) && string.IsNullOrWhiteSpace(node.ParentDisplayName))
            {
                node.ParentDisplayName = vendor;
            }

            if (!string.IsNullOrWhiteSpace(serial) && string.IsNullOrWhiteSpace(node.DeviceInstanceID))
            {
                node.DeviceInstanceID = serial;
            }

            if (!string.IsNullOrWhiteSpace(revision) && node.DeviceDescription.IndexOf(revision, StringComparison.OrdinalIgnoreCase) < 0)
            {
                node.DeviceDescription = StringUtil.TrimStorageString(node.DeviceDescription + " " + revision);
            }

            return node;
        }

        private static string ResolveControllerClass(string driver, string subsystem, string blockName)
        {
            if (blockName.StartsWith("nvme", StringComparison.OrdinalIgnoreCase) || string.Equals(driver, "nvme", StringComparison.OrdinalIgnoreCase))
            {
                return ControllerClassNames.ScsiAdapter;
            }

            if (string.Equals(driver, "usb-storage", StringComparison.OrdinalIgnoreCase) || string.Equals(driver, "uas", StringComparison.OrdinalIgnoreCase))
            {
                return ControllerClassNames.Usb;
            }

            if (string.Equals(subsystem, "scsi", StringComparison.OrdinalIgnoreCase) || string.Equals(subsystem, "block", StringComparison.OrdinalIgnoreCase))
            {
                return ControllerClassNames.ScsiAdapter;
            }

            return ControllerClassNames.ScsiAdapter;
        }

        private static string ResolveControllerName(string deviceDirectory, string driver, string subsystem)
        {
            string pciName = FindFirstParentFileValue(deviceDirectory, "label");
            if (!string.IsNullOrWhiteSpace(pciName))
            {
                return pciName;
            }

            if (!string.IsNullOrWhiteSpace(driver))
            {
                return driver;
            }

            return subsystem ?? string.Empty;
        }

        private static string BuildHardwareId(string deviceDirectory)
        {
            string vendor = FindFirstParentFileValue(deviceDirectory, "vendor");
            string device = FindFirstParentFileValue(deviceDirectory, "device");
            string revision = FindFirstParentFileValue(deviceDirectory, "revision");

            if (TryParseLinuxHex(vendor, out var vendorId) && TryParseLinuxHex(device, out var deviceId))
            {
                string hardwareId = string.Format(CultureInfo.InvariantCulture, @"PCI\VEN_{0:X4}&DEV_{1:X4}", vendorId, deviceId);
                if (TryParseLinuxHex(revision, out var revisionId))
                {
                    hardwareId += string.Format(CultureInfo.InvariantCulture, "&REV_{0:X2}", revisionId & 0xFF);
                }

                return hardwareId;
            }

            return string.Empty;
        }

        private static string BuildControllerIdentifier(string driver, string hardwareId, string deviceDirectory)
        {
            string left = string.IsNullOrWhiteSpace(driver)
                ? StorageTextConstants.GenericControllerIdentifierPrefix
                : driver.Substring(0, Math.Min(3, driver.Length)).ToUpperInvariant();

            string right = string.Empty;
            if (!string.IsNullOrWhiteSpace(hardwareId))
            {
                right = hardwareId.Length >= 21 ? hardwareId.Substring(17, 4).ToUpperInvariant() : hardwareId.ToUpperInvariant();
            }
            else
            {
                string realPath = ResolveRealPath(deviceDirectory);

                right = Math.Abs(realPath.GetHashCode()).ToString("X4", CultureInfo.InvariantCulture);
                if (right.Length > 4)
                {
                    right = right.Substring(0, 4);
                }
            }

            return left + StorageTextConstants.ControllerIdentifierSeparator + right;
        }

        private static string ResolveDriverName(string deviceDirectory)
        {
            return ResolveLinkName(SearchUpwardsForLink(deviceDirectory, "driver"));
        }

        private static string ResolveSubsystemName(string deviceDirectory)
        {
            return ResolveLinkName(SearchUpwardsForLink(deviceDirectory, "subsystem"));
        }

        private static string SearchUpwardsForLink(string startDirectory, string linkName)
        {
            string current = ResolveRealPath(startDirectory);
            for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); ++i)
            {
                string candidate = Path.Combine(current, linkName);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            return string.Empty;
        }

        private static string ResolveLinkName(string linkPath)
        {
            string target = ResolveRealPath(linkPath);
            return string.IsNullOrWhiteSpace(target) ? string.Empty : Path.GetFileName(target);
        }

        private static string FindFirstParentFileValue(string startDirectory, string fileName)
        {
            string current = ResolveRealPath(startDirectory);
            for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); ++i)
            {
                string candidate = Path.Combine(current, fileName);
                string value = ReadFileString(candidate);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            return string.Empty;
        }

        private static string ReadSysfsString(string directory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return string.Empty;
            }

            return ReadFileString(Path.Combine(directory, fileName));
        }

        private static string ReadFileString(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return string.Empty;
                }

                return StringUtil.TrimStorageString(File.ReadAllText(path));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveRealPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return string.Empty;
                }

                return ResolveSymlinkChain(Path.GetFullPath(path), 0);
            }
            catch
            {
                return path ?? string.Empty;
            }
        }

        private static string ResolveSymlinkChain(string path, int depth)
        {
            if (depth > 16 || string.IsNullOrWhiteSpace(path))
            {
                return path ?? string.Empty;
            }

            var buffer = new byte[4096];

            var length = LinuxNative.readlink(path, buffer, (UIntPtr)buffer.Length);
            long count = length.ToInt64();

            if (count <= 0 || count >= buffer.Length)
            {
                return path;
            }

            string target = System.Text.Encoding.UTF8.GetString(buffer, 0, (int)count);
            if (!Path.IsPathRooted(target))
            {
                string parent = Path.GetDirectoryName(path) ?? string.Empty;
                target = Path.Combine(parent, target);
            }

            return ResolveSymlinkChain(Path.GetFullPath(target), depth + 1);
        }

        private static bool TryParseLinuxHex(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }

            return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        #endregion
    }
}
