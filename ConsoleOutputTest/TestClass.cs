/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Florian K.
 *
 * Code inspiration, improvements and fixes are from, but not limited to, following projects:
 * CrystalDiskInfo
 */

using BlackSharp.Core.Logging;
using DiskInfoToolkit;
using DiskInfoToolkit.Enums.Interop;
using DiskInfoToolkit.Events;

namespace ConsoleOutputTest
{
    public class TestClass
    {
        public TestClass()
        {
        }

        public static void Log()
        {
            Log(string.Empty);
        }

        public static void Log(string message)
        {
            Logger.Instance.Add(LogLevel.Debug, message, DateTime.Now);
        }

        public void DoTest()
        {
            Log($"Detecting all devices.");

            StorageManager.ReloadStorages();

            //Go through all devices
            foreach (var storage in StorageManager.Storages)
            {
                //Log data from device
                LogData(storage);
            }

            Log($"Detecting done.");

            //Register change event
            StorageManager.StoragesChanged += DevicesChanged;

            var secondsToWait = 10;

            //Wait for specified amount of time and listen to device changes
            Console.WriteLine($"Waiting {secondsToWait} seconds for device changes.");
            Thread.Sleep(TimeSpan.FromSeconds(secondsToWait));

            //Go through all devices
            foreach (var storage in StorageManager.Storages)
            {
                //Update device
                storage.Update();

                //Output updated temperature as an example
                Log($"Updated '{storage.Model}'. Temperature is {storage.Smart.Temperature}°C.");
            }
        }

        static void DevicesChanged(StoragesChangedEventArgs e)
        {
            //Devices have changed, log event information
            Log($"Eventhandler {nameof(StorageManager.StoragesChanged)} - {e.StorageChangeIdentifier} => {e.Storage.Model}.");

            if (e.StorageChangeIdentifier == StorageChangeIdentifier.Added)
            {
                //Log data of device passed in event
                LogData(e.Storage);
            }

            Console.WriteLine($"Devices changed ({e.StorageChangeIdentifier} => {e.Storage.Model}).");
        }

        static object _LogDataLock = new object();
        static void LogData(Storage storage)
        {
            const int Padding = -35;

            lock (_LogDataLock)
            {
                Log($"Storage '{storage.Model}':");

                if (!storage.IsDynamicDisk)
                {
                    Log($"  -> {nameof(storage.Partitions)}:");

                    foreach (var partition in storage.Partitions)
                    {
                        Log($"    + {nameof(Partition)} #{partition.PartitionNumber}:");

                        if (partition.DriveLetter != null)
                        {
                            Log($"      # {nameof(partition.DriveLetter       ),Padding} = {partition.DriveLetter       }");
                        }

                        if (partition.AvailableFreeSpace != null)
                        {
                            Log($"      # {nameof(partition.AvailableFreeSpace),Padding} = {partition.AvailableFreeSpace}");
                        }

                        Log($"      # {nameof(partition.PartitionStyle                 ),Padding} = {partition.PartitionStyle                 }");
                        Log($"      # {nameof(partition.StartingOffset                 ),Padding} = {partition.StartingOffset                 }");
                        Log($"      # {nameof(partition.PartitionLength                ),Padding} = {partition.PartitionLength                }");

                        switch (partition.PartitionStyle)
                        {
                            case PartitionStyle.PartitionStyleMBR:
                                Log($"      # {nameof(partition.PartitionInformation.Mbr.PartitionType),Padding} = {partition.PartitionInformation.Mbr.PartitionType}");
                                Log($"      # {nameof(partition.PartitionInformation.Mbr.PartitionId  ),Padding} = {partition.PartitionInformation.Mbr.PartitionId  }");
                                break;
                            case PartitionStyle.PartitionStyleGPT:
                                Log($"      # {nameof(partition.PartitionInformation.Gpt.PartitionType),Padding} = {partition.PartitionInformation.Gpt.PartitionType}");
                                Log($"      # {nameof(partition.PartitionInformation.Gpt.PartitionId  ),Padding} = {partition.PartitionInformation.Gpt.PartitionId  }");
                                break;
                        }

                        Log($"      # {nameof(partition.IsDynamicDiskPartition         ),Padding} = {partition.IsDynamicDiskPartition         }");
                        Log($"      # {nameof(partition.IsOtherOperatingSystemPartition),Padding} = {partition.IsOtherOperatingSystemPartition}");
                    }
                }
                else
                {
                    Log($"  -> {nameof(storage.Partitions)} not being logged due to {nameof(storage.IsDynamicDisk)}");
                }

                Log();

                Log($"  -> {nameof(storage.IsNVMe               ),Padding} = {storage.IsNVMe}");
                Log($"  -> {nameof(storage.IsSSD                ),Padding} = {storage.IsSSD}");

                if (storage.ATAInfo != null)
                {
                    Log($"    + {nameof(storage.ATAInfo)}:");

                    Log($"      # {nameof(storage.ATAInfo.Cylinders         ),Padding} = {storage.ATAInfo.Cylinders         }");
                    Log($"      # {nameof(storage.ATAInfo.DiskSizeChs       ),Padding} = {storage.ATAInfo.DiskSizeChs       }");
                    Log($"      # {nameof(storage.ATAInfo.DiskSizeLba28     ),Padding} = {storage.ATAInfo.DiskSizeLba28     }");
                    Log($"      # {nameof(storage.ATAInfo.DiskSizeLba48     ),Padding} = {storage.ATAInfo.DiskSizeLba48     }");
                    Log($"      # {nameof(storage.ATAInfo.Heads             ),Padding} = {storage.ATAInfo.Heads             }");
                    Log($"      # {nameof(storage.ATAInfo.LogicalSectorSize ),Padding} = {storage.ATAInfo.LogicalSectorSize }");
                    Log($"      # {nameof(storage.ATAInfo.NumberOfSectors   ),Padding} = {storage.ATAInfo.NumberOfSectors   }");
                    Log($"      # {nameof(storage.ATAInfo.PhysicalSectorSize),Padding} = {storage.ATAInfo.PhysicalSectorSize}");
                    Log($"      # {nameof(storage.ATAInfo.Sector28          ),Padding} = {storage.ATAInfo.Sector28          }");
                    Log($"      # {nameof(storage.ATAInfo.Sector48          ),Padding} = {storage.ATAInfo.Sector48          }");
                    Log($"      # {nameof(storage.ATAInfo.Sectors           ),Padding} = {storage.ATAInfo.Sectors           }");
                    Log($"      # {nameof(storage.ATAInfo.TransferMode      ),Padding} = {storage.ATAInfo.TransferMode      }");
                }

                Log($"  -> {nameof(storage.BusType                    ),Padding} = {storage.BusType                    }");
                Log($"  -> {nameof(storage.DetectedTimeUnitType       ),Padding} = {storage.DetectedTimeUnitType       }");
                Log($"  -> {nameof(storage.DeviceID                   ),Padding} = {storage.DeviceID                   }");
                Log($"  -> {nameof(storage.DriveNumber                ),Padding} = {storage.DriveNumber                }");
                Log($"  -> {nameof(storage.Firmware                   ),Padding} = {storage.Firmware                   }");
                Log($"  -> {nameof(storage.FirmwareRev                ),Padding} = {storage.FirmwareRev                }");
                Log($"  -> {nameof(storage.IsTrimSupported            ),Padding} = {storage.IsTrimSupported            }");
                Log($"  -> {nameof(storage.IsVolatileWriteCachePresent),Padding} = {storage.IsVolatileWriteCachePresent}");
                Log($"  -> {nameof(storage.MeasuredTimeUnitType       ),Padding} = {storage.MeasuredTimeUnitType       }");
                Log($"  -> {nameof(storage.Model                      ),Padding} = {storage.Model                      }");
                Log($"  -> {nameof(storage.PhysicalPath               ),Padding} = {storage.PhysicalPath               }");

                if (storage.ProductID != null)
                {
                    Log($"  -> {nameof(storage.ProductID                  ),Padding} = {storage.ProductID                  }");
                }

                Log($"  -> {nameof(storage.SerialNumber               ),Padding} = {storage.SerialNumber               }");
                Log($"  -> {nameof(storage.StorageController          ),Padding} = {storage.StorageController          }");
                Log($"  -> {nameof(storage.StorageControllerType      ),Padding} = {storage.StorageControllerType      }");

                Log($"  -> {nameof(storage.IsDynamicDisk              ),Padding} = {storage.IsDynamicDisk              }");

                if (!storage.IsDynamicDisk)
                {
                    var totalFreeSize = storage.TotalFreeSize;

                    var percentFree = totalFreeSize == 0 ? 0 : 100M * totalFreeSize / storage.TotalSize;

                    Log($"  -> {nameof(storage.TotalFreeSize              ),Padding} = {totalFreeSize                      } ({percentFree:F2}%)");
                }

                Log($"  -> {nameof(storage.TotalSize                  ),Padding} = {storage.TotalSize                  }");

                if (storage.Vendor != null)
                {
                    Log($"  -> {nameof(storage.Vendor                     ),Padding} = {storage.Vendor                     }");
                }

                if (storage.VendorID != null)
                {
                    Log($"  -> {nameof(storage.VendorID                   ),Padding} = {storage.VendorID                   } ({storage.VendorID:X4})");
                }

                Log();

                if (storage.Smart != null)
                {
                    Log($"  -> {nameof(storage.Smart)}:");

                    Log($"    + {nameof(storage.Smart.DiskStatus          ),Padding} = {storage.Smart.DiskStatus          }");

                    if (storage.Smart.Temperature != null)
                    {
                        Log($"    + {nameof(storage.Smart.Temperature         ),Padding} = {storage.Smart.Temperature         }°C");
                    }

                    if (storage.Smart.TemperatureWarning != null)
                    {
                        Log($"    + {nameof(storage.Smart.TemperatureWarning  ),Padding} = {storage.Smart.TemperatureWarning  }°C");
                    }

                    if (storage.Smart.TemperatureCritical != null)
                    {
                        Log($"    + {nameof(storage.Smart.TemperatureCritical ),Padding} = {storage.Smart.TemperatureCritical }°C");
                    }

                    if (storage.Smart.Life != null)
                    {
                        Log($"    + {nameof(storage.Smart.Life                ),Padding} = {storage.Smart.Life                }%");
                    }

                    if (storage.Smart.HostReads != null)
                    {
                        Log($"    + {nameof(storage.Smart.HostReads           ),Padding} = {storage.Smart.HostReads           }");
                    }

                    if (storage.Smart.HostWrites != null)
                    {
                        Log($"    + {nameof(storage.Smart.HostWrites          ),Padding} = {storage.Smart.HostWrites          }");
                    }

                    Log($"    + {nameof(storage.Smart.PowerOnCount        ),Padding} = {storage.Smart.PowerOnCount        }");
                    Log($"    + {nameof(storage.Smart.MeasuredPowerOnHours),Padding} = {storage.Smart.MeasuredPowerOnHours}h");
                    Log($"    + {nameof(storage.Smart.DetectedPowerOnHours),Padding} = {storage.Smart.DetectedPowerOnHours}h");

                    if (storage.Smart.NandWrites != null)
                    {
                        Log($"    + {nameof(storage.Smart.NandWrites          ),Padding} = {storage.Smart.NandWrites          }");
                    }

                    if (storage.Smart.GBytesErased != null)
                    {
                        Log($"    + {nameof(storage.Smart.GBytesErased        ),Padding} = {storage.Smart.GBytesErased        }");
                    }

                    if (storage.Smart.WearLevelingCount != null)
                    {
                        Log($"    + {nameof(storage.Smart.WearLevelingCount   ),Padding} = {storage.Smart.WearLevelingCount   }");
                    }

                    Log();

                    if (storage.Smart.SmartAttributes.Count > 0)
                    {
                        Log($"{nameof(storage.Smart.SmartAttributes)}:");

                        foreach (var attr in storage.Smart.SmartAttributes)
                        {
                            if (storage.IsNVMe)
                            {
                                Log($"    * {attr.Info.ID:X2} ('{attr.Info.Name,-50}') = 0x{attr.Attribute.RawValueUInt:X12} ({attr.Attribute.RawValueUInt,20})");
                            }
                            else
                            {
                                Log($"    * {attr.Info.ID:X2} ('{attr.Info.Name,-50}') = 0x{attr.Attribute.RawValueUInt:X12} ({attr.Attribute.RawValueUInt,20}) ({nameof(attr.Attribute.Threshold)} = {attr.Attribute.Threshold,5})");
                            }
                        }

                        Log();
                    }
                }
            }
        }
    }
}
