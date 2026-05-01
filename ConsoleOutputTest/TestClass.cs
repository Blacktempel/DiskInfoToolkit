/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using BlackSharp.Core.Logging;
using DiskInfoToolkit;
using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace ConsoleOutputTest
{
    public class TestClass
    {
        #region Constructor

        public TestClass()
        {
        }

        #endregion

        #region Fields

        static object _LogDataLock = new object();

        #endregion

        #region Public

        public void DoTest()
        {
            var sw = new Stopwatch();
            sw.Start();

            WriteOutput($"### Detecting all disks. ###");
            WriteOutput();

            //You can, optionally, set language of smart attributes to any supported culture
            //Culture can be changed at any time but it will only affect the next fetch or update of the disk, it will not update already fetched attributes

            //Storage.ResourceCulture = new System.Globalization.CultureInfo("de-DE");
            //Storage.ResourceCulture = new System.Globalization.CultureInfo("es-ES");

            //Get all disks
            var disks = Storage.GetDisks();

            //Go through all devices
            foreach (var disk in disks)
            {
                //Log data from device
                WriteOutput($"'{disk.DisplayName}':");
                PrintPublicProperties(disk);
                WriteOutput();
            }

            sw.Stop();

            WriteOutput($"### Detecting all disks took {sw.Elapsed.TotalMilliseconds:F2} ms. ###");
            WriteOutput();

            //Register change event
            Storage.DevicesChanged += DevicesChanged;

            var secondsToWait = 10;

            Console.WriteLine($"Waiting {secondsToWait} seconds for device changes.");

            //Wait for specified amount of time and listen to device changes in background
            while (secondsToWait-- > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }

            WriteOutput("### Updating all disks ###");

            sw.Restart();

            //Go through all current devices
            foreach (var disk in Storage.CurrentDisks)
            {
                //Update device
                if (Storage.Refresh(disk))
                {
                    //Check if temperature is available
                    if (disk.Temperature != null)
                    {
                        WriteOutput($"Disk '{disk.DisplayName}' has been updated (Temp = {disk.Temperature}°C).");
                    }
                    else
                    {
                        WriteOutput($"Disk '{disk.DisplayName}' has been updated.");
                    }
                }
                else
                {
                    //Check if temperature is available
                    if (disk.Temperature != null)
                    {
                        WriteOutput($"Disk '{disk.DisplayName}' has been updated but has no changes (Temp = {disk.Temperature}°C).");
                    }
                    else
                    {
                        WriteOutput($"Disk '{disk.DisplayName}' has been updated but has no changes.");
                    }
                }
            }

            sw.Stop();

            WriteOutput($"### Update of all disks took {sw.Elapsed.TotalMilliseconds:F2} ms. ###");
        }

        #endregion

        #region Private

        void DevicesChanged(object sender, StorageDevicesChangedEventArgs e)
        {
            //Check if there are any changes at all
            if (!e.HasChanges)
            {
                WriteOutput($"{nameof(Storage.DevicesChanged)} was fired but no changes were detected.");
            }

            //Check for added devices
            if (e.Added?.Count > 0)
            {
                WriteOutput($"Added {e.Added.Count} devices:");
                foreach (var disk in e.Added)
                {
                    WriteOutput($"-> '{disk.DisplayName}'", 1);
                }
            }

            //Check for removed devices
            if (e.Removed?.Count > 0)
            {
                WriteOutput($"Removed {e.Removed.Count} devices:");
                foreach (var disk in e.Removed)
                {
                    WriteOutput($"-> '{disk.DisplayName}'", 1);
                }
            }
        }

        private static void PrintPublicProperties(object obj, bool skipArrays = true, int level = 0, HashSet<object> visited = null)
        {
            if (obj is null)
            {
                WriteOutput("null", level);
                return;
            }

            visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (!visited.Add(obj))
            {
                WriteOutput("(already visited)", level);
                return;
            }

            var type = obj.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object value;

                try
                {
                    value = property.GetValue(obj);
                }
                catch
                {
                    WriteOutput($"{property.Name}: <unavailable>", level);
                    continue;
                }

                if (value is null)
                {
                    WriteOutput($"{property.Name}: null", level);
                    continue;
                }

                if (IsSimpleType(property.PropertyType))
                {
                    WriteOutput($"{property.Name}: {value}", level);
                    continue;
                }

                if (value is IEnumerable enumerable && value is not string)
                {
                    WriteOutput($"{property.Name}:", level);

                    if (value is Array && skipArrays)
                    {
                        WriteOutput($"Skipping array.", level + 1);
                    }
                    else if (value is IEnumerable<string> strings)
                    {
                        var index = 0;
                        foreach (var str in strings)
                        {
                            WriteOutput($"[{index}]: {str}", level + 1);
                            index++;
                        }
                    }
                    else
                    {
                        var index = 0;
                        foreach (var item in enumerable)
                        {
                            WriteOutput($"[{index}]", level + 1);
                            PrintPublicProperties(item, skipArrays, level + 2, visited);
                            index++;
                        }
                    }

                    continue;
                }

                WriteOutput($"{property.Name}:", level);
                PrintPublicProperties(value, skipArrays, level + 1, visited);
            }
        }

        private static void WriteOutput()
        {
            WriteOutput(string.Empty);
        }

        private static void WriteOutput(string text, int level)
        {
            WriteOutput($"{new string(' ', level * 4)}{text}");
        }

        private static void WriteOutput(string text)
        {
            lock (_LogDataLock)
            {
                Logger.Instance.Add(LogLevel.Trace, text, DateTime.Now);
                Console.WriteLine(text);
            }
        }

        private static bool IsSimpleType(Type type)
        {
            var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

            return effectiveType.IsPrimitive
                   || effectiveType.IsEnum
                   || effectiveType == typeof(string)
                   || effectiveType == typeof(decimal)
                   || effectiveType == typeof(DateTime)
                   || effectiveType == typeof(DateTimeOffset)
                   || effectiveType == typeof(TimeSpan)
                   || effectiveType == typeof(Guid);
        }

        #endregion
    }
}
