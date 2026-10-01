/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Smart;

namespace DiskInfoToolkit.Probes
{
    public static class NvmeSmartLogParser
    {
        #region Fields

        private const byte NvmeSmartAttributeBaseId = 0xE0;

        private const byte NvmeCriticalWarningAttributeId = NvmeSmartAttributeBaseId + 0x00;

        private const byte NvmeCompositeTemperatureAttributeId = NvmeSmartAttributeBaseId + 0x01;

        private const byte NvmeAvailableSpareAttributeId = NvmeSmartAttributeBaseId + 0x02;

        private const byte NvmeAvailableSpareThresholdAttributeId = NvmeSmartAttributeBaseId + 0x03;

        private const byte NvmePercentageUsedAttributeId = NvmeSmartAttributeBaseId + 0x04;

        private const byte NvmeDataUnitsReadAttributeId = NvmeSmartAttributeBaseId + 0x05;

        private const byte NvmeDataUnitsWrittenAttributeId = NvmeSmartAttributeBaseId + 0x06;

        private const byte NvmeHostReadCommandsAttributeId = NvmeSmartAttributeBaseId + 0x07;

        private const byte NvmeHostWriteCommandsAttributeId = NvmeSmartAttributeBaseId + 0x08;

        private const byte NvmeControllerBusyTimeAttributeId = NvmeSmartAttributeBaseId + 0x09;

        private const byte NvmePowerCyclesAttributeId = NvmeSmartAttributeBaseId + 0x0A;

        private const byte NvmePowerOnHoursAttributeId = NvmeSmartAttributeBaseId + 0x0B;

        private const byte NvmeUnsafeShutdownsAttributeId = NvmeSmartAttributeBaseId + 0x0C;

        private const byte NvmeMediaErrorsAttributeId = NvmeSmartAttributeBaseId + 0x0D;

        private const byte NvmeErrorInfoLogEntriesAttributeId = NvmeSmartAttributeBaseId + 0x0E;

        private const byte NvmeWarningCompositeTemperatureTimeAttributeId = NvmeSmartAttributeBaseId + 0x0F;

        private const byte NvmeCriticalCompositeTemperatureTimeAttributeId = NvmeSmartAttributeBaseId + 0x10;

        private const byte NvmeTemperatureSensor1AttributeId = NvmeSmartAttributeBaseId + 0x11;

        private const byte NvmeTemperatureSensor2AttributeId = NvmeSmartAttributeBaseId + 0x12;

        private const byte NvmeTemperatureSensor3AttributeId = NvmeSmartAttributeBaseId + 0x13;

        private const byte NvmeTemperatureSensor4AttributeId = NvmeSmartAttributeBaseId + 0x14;

        private const byte NvmeTemperatureSensor5AttributeId = NvmeSmartAttributeBaseId + 0x15;

        private const byte NvmeTemperatureSensor6AttributeId = NvmeSmartAttributeBaseId + 0x16;

        private const byte NvmeTemperatureSensor7AttributeId = NvmeSmartAttributeBaseId + 0x17;

        private const byte NvmeTemperatureSensor8AttributeId = NvmeSmartAttributeBaseId + 0x18;

        private const byte NvmeThermalManagementTemperature1TransitionCountAttributeId = NvmeSmartAttributeBaseId + 0x19;

        private const byte NvmeThermalManagementTemperature2TransitionCountAttributeId = NvmeSmartAttributeBaseId + 0x1A;

        private const byte NvmeTotalTimeThermalManagementTemperature1AttributeId = NvmeSmartAttributeBaseId + 0x1B;

        private const byte NvmeTotalTimeThermalManagementTemperature2AttributeId = NvmeSmartAttributeBaseId + 0x1C;

        #endregion

        #region Public

        public static void Apply(StorageDevice device, byte[] smartLogData)
        {
            ApplySmartLog(device, smartLogData);
        }

        public static void ApplySmartLog(StorageDevice device, byte[] smartLogData)
        {
            if (device == null || smartLogData == null || smartLogData.Length < 192)
            {
                return;
            }

            device.SupportsSmart = true;
            device.SmartAttributes = BuildSmartAttributes(smartLogData);
        }

        #endregion

        #region Private

        private static List<SmartAttributeEntry> BuildSmartAttributes(byte[] smartLogData)
        {
            var attributes = new List<SmartAttributeEntry>();

            AddAttribute(attributes, NvmeCriticalWarningAttributeId, smartLogData[0], smartLogData[0], 0, 0);
            AddAttribute(attributes, NvmeCompositeTemperatureAttributeId, BitConverter.ToUInt16(smartLogData, 1), 0, 0, 0);
            AddAttribute(attributes, NvmeAvailableSpareAttributeId, smartLogData[3], smartLogData[3], 0, smartLogData[4]);
            AddAttribute(attributes, NvmeAvailableSpareThresholdAttributeId, smartLogData[4], smartLogData[4], 0, 0);

            AddAttribute(attributes, NvmePercentageUsedAttributeId, smartLogData[5], smartLogData[5], 0, 0);

            AddAttribute(attributes, NvmeDataUnitsReadAttributeId, ReadLittleEndianUInt64(smartLogData, 32), 0, 0, 0);
            AddAttribute(attributes, NvmeDataUnitsWrittenAttributeId, ReadLittleEndianUInt64(smartLogData, 48), 0, 0, 0);
            AddAttribute(attributes, NvmeHostReadCommandsAttributeId, ReadLittleEndianUInt64(smartLogData, 64), 0, 0, 0);
            AddAttribute(attributes, NvmeHostWriteCommandsAttributeId, ReadLittleEndianUInt64(smartLogData, 80), 0, 0, 0);
            AddAttribute(attributes, NvmeControllerBusyTimeAttributeId, ReadLittleEndianUInt64(smartLogData, 96), 0, 0, 0);
            AddAttribute(attributes, NvmePowerCyclesAttributeId, ReadLittleEndianUInt64(smartLogData, 112), 0, 0, 0);
            AddAttribute(attributes, NvmePowerOnHoursAttributeId, ReadLittleEndianUInt64(smartLogData, 128), 0, 0, 0);
            AddAttribute(attributes, NvmeUnsafeShutdownsAttributeId, ReadLittleEndianUInt64(smartLogData, 144), 0, 0, 0);
            AddAttribute(attributes, NvmeMediaErrorsAttributeId, ReadLittleEndianUInt64(smartLogData, 160), 0, 0, 0);
            AddAttribute(attributes, NvmeErrorInfoLogEntriesAttributeId, ReadLittleEndianUInt64(smartLogData, 176), 0, 0, 0);

            if (smartLogData.Length >= 200)
            {
                AddAttribute(attributes, NvmeWarningCompositeTemperatureTimeAttributeId, ReadLittleEndianUInt32(smartLogData, 192), 0, 0, 0);
                AddAttribute(attributes, NvmeCriticalCompositeTemperatureTimeAttributeId, ReadLittleEndianUInt32(smartLogData, 196), 0, 0, 0);
            }

            if (smartLogData.Length >= 216)
            {
                AddAttribute(attributes, NvmeTemperatureSensor1AttributeId, ReadUInt16Safe(smartLogData, 200), 0, 0, 0);
                AddAttribute(attributes, NvmeTemperatureSensor2AttributeId, ReadUInt16Safe(smartLogData, 202), 0, 0, 0);
                AddAttribute(attributes, NvmeTemperatureSensor3AttributeId, ReadUInt16Safe(smartLogData, 204), 0, 0, 0);
                AddAttribute(attributes, NvmeTemperatureSensor4AttributeId, ReadUInt16Safe(smartLogData, 206), 0, 0, 0);
                AddAttribute(attributes, NvmeTemperatureSensor5AttributeId, ReadUInt16Safe(smartLogData, 208), 0, 0, 0);
                AddAttribute(attributes, NvmeTemperatureSensor6AttributeId, ReadUInt16Safe(smartLogData, 210), 0, 0, 0);
                AddAttribute(attributes, NvmeTemperatureSensor7AttributeId, ReadUInt16Safe(smartLogData, 212), 0, 0, 0);
                AddAttribute(attributes, NvmeTemperatureSensor8AttributeId, ReadUInt16Safe(smartLogData, 214), 0, 0, 0);
            }

            if (smartLogData.Length >= 232)
            {
                AddAttribute(attributes, NvmeThermalManagementTemperature1TransitionCountAttributeId, ReadLittleEndianUInt32(smartLogData, 216), 0, 0, 0);
                AddAttribute(attributes, NvmeThermalManagementTemperature2TransitionCountAttributeId, ReadLittleEndianUInt32(smartLogData, 220), 0, 0, 0);
                AddAttribute(attributes, NvmeTotalTimeThermalManagementTemperature1AttributeId, ReadLittleEndianUInt32(smartLogData, 224), 0, 0, 0);
                AddAttribute(attributes, NvmeTotalTimeThermalManagementTemperature2AttributeId, ReadLittleEndianUInt32(smartLogData, 228), 0, 0, 0);
            }

            return attributes;
        }

        private static void AddAttribute(List<SmartAttributeEntry> attributes, byte id, ulong rawValue, byte currentValue, byte worstValue, byte thresholdValue)
        {
            var entry = new SmartAttributeEntry();
            entry.ID = id;
            entry.StatusFlags = 0;
            entry.CurrentValue = currentValue;
            entry.WorstValue = worstValue;
            entry.RawValue = rawValue;
            entry.ThresholdValue = thresholdValue;

            attributes.Add(entry);
        }

        private static ushort ReadUInt16Safe(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 2 > data.Length)
            {
                return 0;
            }

            return BitConverter.ToUInt16(data, offset);
        }

        private static uint ReadLittleEndianUInt32(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 4 > data.Length)
            {
                return 0;
            }

            return BitConverter.ToUInt32(data, offset);
        }

        private static ulong ReadLittleEndianUInt64(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 8 > data.Length)
            {
                return 0;
            }

            return BitConverter.ToUInt64(data, offset);
        }

        #endregion
    }
}
