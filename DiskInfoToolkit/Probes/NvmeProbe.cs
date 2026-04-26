/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Constants;
using DiskInfoToolkit.Core;
using DiskInfoToolkit.Interop;
using DiskInfoToolkit.Utilities;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DiskInfoToolkit.Probes
{
    public static class NvmeProbe
    {
        #region Fields

        private const int NvmeBufferLength = 4096;

        private const uint StorageAdapterProtocolSpecificProperty = 49;

        private const uint StorageDeviceProtocolSpecificProperty = 50;

        private const uint PropertyStandardQuery = 0;

        private const uint ProtocolTypeNvme = 3;

        private const uint NvmeDataTypeIdentify = 1;

        private const uint NvmeDataTypeLogPage = 2;

        private const uint NvmeIdentifyControllerCns = 1;

        private const uint NvmeIdentifyNamespaceCns = 0;

        private const uint NvmeNamespaceIdZero = 0;

        private const uint NvmeNamespaceIdOne = 1;

        private const uint NvmeNamespaceIdAll = 0xFFFFFFFF;

        private const uint NvmeLogPageSmartHealthInformation = 2;

        #endregion

        #region Public

        public static bool TryPopulateStandardNvmeData(StorageDevice device, IStorageIoControl ioControl)
        {
            if (device == null || ioControl == null || string.IsNullOrWhiteSpace(device.DevicePath))
            {
                return false;
            }

            SafeFileHandle handle = ioControl.OpenDevice(
                device.DevicePath,
                IoAccess.GenericRead | IoAccess.GenericWrite,
                IoShare.ReadWrite,
                IoCreation.OpenExisting,
                IoFlags.Normal);

            if (handle == null || handle.IsInvalid)
            {
                return false;
            }

            using (handle)
            {
                byte[] identifyControllerData = null;
                byte[] identifyNamespaceData = null;
                byte[] smartLogData = null;

                bool identifyControllerOk =
                    TryStorageQueryNvmeIdentifyController(ioControl, handle, StorageAdapterProtocolSpecificProperty, out identifyControllerData)
                    || TryStorageQueryNvmeIdentifyController(ioControl, handle, StorageDeviceProtocolSpecificProperty, out identifyControllerData);

                bool identifyNamespaceOk =
                    TryStorageQueryNvmeIdentifyNamespace(ioControl, handle, StorageAdapterProtocolSpecificProperty, NvmeNamespaceIdOne, out identifyNamespaceData)
                    || TryStorageQueryNvmeIdentifyNamespace(ioControl, handle, StorageDeviceProtocolSpecificProperty, NvmeNamespaceIdOne, out identifyNamespaceData)
                    || TryStorageQueryNvmeIdentifyNamespace(ioControl, handle, StorageAdapterProtocolSpecificProperty, NvmeNamespaceIdAll, out identifyNamespaceData)
                    || TryStorageQueryNvmeIdentifyNamespace(ioControl, handle, StorageDeviceProtocolSpecificProperty, NvmeNamespaceIdAll, out identifyNamespaceData)
                    //Micron driver (mtinvme.sys) wants namespace 0
                    || TryStorageQueryNvmeIdentifyNamespace(ioControl, handle, StorageAdapterProtocolSpecificProperty, NvmeNamespaceIdZero, out identifyNamespaceData)
                    || TryStorageQueryNvmeIdentifyNamespace(ioControl, handle, StorageDeviceProtocolSpecificProperty, NvmeNamespaceIdZero, out identifyNamespaceData);

                bool smartOk =
                    TryStorageQueryNvmeSmartLog(ioControl, handle, StorageAdapterProtocolSpecificProperty, NvmeNamespaceIdAll, out smartLogData)
                    || TryStorageQueryNvmeSmartLog(ioControl, handle, StorageDeviceProtocolSpecificProperty, NvmeNamespaceIdAll, out smartLogData)
                    || TryStorageQueryNvmeSmartLog(ioControl, handle, StorageAdapterProtocolSpecificProperty, NvmeNamespaceIdOne, out smartLogData)
                    || TryStorageQueryNvmeSmartLog(ioControl, handle, StorageDeviceProtocolSpecificProperty, NvmeNamespaceIdOne, out smartLogData)
                    //Micron driver (mtinvme.sys) wants namespace 0
                    || TryStorageQueryNvmeSmartLog(ioControl, handle, StorageAdapterProtocolSpecificProperty, NvmeNamespaceIdZero, out smartLogData)
                    || TryStorageQueryNvmeSmartLog(ioControl, handle, StorageDeviceProtocolSpecificProperty, NvmeNamespaceIdZero, out smartLogData);

                if (identifyControllerOk)
                {
                    device.Nvme.IdentifyControllerData = identifyControllerData;

                    NvmeProbeUtil.ApplyIdentifyControllerStrings(device, identifyControllerData);

                    device.BusType = StorageBusType.Nvme;
                    device.TransportKind = StorageTransportKind.Nvme;
                }

                if (identifyNamespaceOk)
                {
                    device.Nvme.IdentifyNamespaceData = identifyNamespaceData;

                    NvmeNamespaceParser.ApplyNamespaceData(device, identifyNamespaceData);

                    if (device.BusType == StorageBusType.Unknown)
                    {
                        device.BusType = StorageBusType.Nvme;
                    }

                    if (device.TransportKind == StorageTransportKind.Unknown)
                    {
                        device.TransportKind = StorageTransportKind.Nvme;
                    }
                }

                if (smartOk)
                {
                    device.Nvme.SmartLogData = smartLogData;
                    NvmeSmartLogParser.ApplySmartLog(device, smartLogData);
                    device.SupportsSmart = true;
                    device.BusType = StorageBusType.Nvme;
                    device.TransportKind = StorageTransportKind.Nvme;
                }

                return identifyControllerOk || identifyNamespaceOk || smartOk;
            }
        }

        #endregion

        #region Private

        private static bool TryStorageQueryNvmeIdentifyController(IStorageIoControl ioControl, SafeFileHandle handle, uint propertyId, out byte[] controllerData)
        {
            var query = NVME_STORAGE_QUERY_WITH_BUFFER.CreateDefault();
            query.Query.PropertyID = (int)propertyId;
            query.Query.QueryType = (int)PropertyStandardQuery;
            query.ProtocolSpecific.ProtocolType = ProtocolTypeNvme;
            query.ProtocolSpecific.DataType = NvmeDataTypeIdentify;
            query.ProtocolSpecific.ProtocolDataRequestValue = NvmeIdentifyControllerCns;
            query.ProtocolSpecific.ProtocolDataRequestSubValue = 0;
            query.ProtocolSpecific.ProtocolDataOffset = (uint)Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();
            query.ProtocolSpecific.ProtocolDataLength = NvmeBufferLength;

            return ExecuteNvmeStorageQuery(ioControl, handle, query, out controllerData);
        }

        private static bool TryStorageQueryNvmeIdentifyNamespace(IStorageIoControl ioControl, SafeFileHandle handle, uint propertyId, uint namespaceId, out byte[] namespaceData)
        {
            var query = NVME_STORAGE_QUERY_WITH_BUFFER.CreateDefault();
            query.Query.PropertyID = (int)propertyId;
            query.Query.QueryType = (int)PropertyStandardQuery;
            query.ProtocolSpecific.ProtocolType = ProtocolTypeNvme;
            query.ProtocolSpecific.DataType = NvmeDataTypeIdentify;
            query.ProtocolSpecific.ProtocolDataRequestValue = NvmeIdentifyNamespaceCns;
            query.ProtocolSpecific.ProtocolDataRequestSubValue = namespaceId;
            query.ProtocolSpecific.ProtocolDataOffset = (uint)Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();
            query.ProtocolSpecific.ProtocolDataLength = NvmeBufferLength;

            return ExecuteNvmeStorageQuery(ioControl, handle, query, out namespaceData);
        }

        private static bool TryStorageQueryNvmeSmartLog(IStorageIoControl ioControl, SafeFileHandle handle, uint propertyId, uint namespaceId, out byte[] smartLogData)
        {
            var query = NVME_STORAGE_QUERY_WITH_BUFFER.CreateDefault();
            query.Query.PropertyID = (int)propertyId;
            query.Query.QueryType = (int)PropertyStandardQuery;
            query.ProtocolSpecific.ProtocolType = ProtocolTypeNvme;
            query.ProtocolSpecific.DataType = NvmeDataTypeLogPage;
            query.ProtocolSpecific.ProtocolDataRequestValue = NvmeLogPageSmartHealthInformation;
            query.ProtocolSpecific.ProtocolDataRequestSubValue = namespaceId;
            query.ProtocolSpecific.ProtocolDataOffset = (uint)Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();
            query.ProtocolSpecific.ProtocolDataLength = NvmeBufferLength;

            return ExecuteNvmeStorageQuery(ioControl, handle, query, out smartLogData);
        }

        private static bool ExecuteNvmeStorageQuery(IStorageIoControl ioControl, SafeFileHandle handle, NVME_STORAGE_QUERY_WITH_BUFFER query, out byte[] protocolData)
        {
            protocolData = null;

            int queryHeaderSize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY_NVME>();
            int protocolSize = Marshal.SizeOf<STORAGE_PROTOCOL_SPECIFIC_DATA>();

            int totalSize = queryHeaderSize + protocolSize + query.Buffer.Length;
            var buffer = new byte[totalSize];

            var queryBytes = StructureHelper.GetBytes(query.Query);
            var protocolBytes = StructureHelper.GetBytes(query.ProtocolSpecific);

            Buffer.BlockCopy(queryBytes, 0, buffer, 0, queryBytes.Length);
            Buffer.BlockCopy(protocolBytes, 0, buffer, queryHeaderSize, protocolBytes.Length);
            Buffer.BlockCopy(query.Buffer, 0, buffer, queryHeaderSize + protocolSize, query.Buffer.Length);

            if (!ioControl.SendRawIoControl(handle, IoControlCodes.IOCTL_STORAGE_QUERY_PROPERTY, buffer, buffer, out var bytesReturned))
            {
                return false;
            }

            if (bytesReturned < queryHeaderSize + protocolSize)
            {
                return false;
            }

            var responseProtocolBytes = new byte[protocolSize];
            Buffer.BlockCopy(buffer, queryHeaderSize, responseProtocolBytes, 0, protocolSize);

            var response = StructureHelper.FromBytes<STORAGE_PROTOCOL_SPECIFIC_DATA>(responseProtocolBytes);

            uint dataOffset = response.ProtocolDataOffset;
            uint dataLength = response.ProtocolDataLength;

            if (dataOffset < (uint)protocolSize)
            {
                return false;
            }

            int payloadOffset = queryHeaderSize + (int)dataOffset;
            if (payloadOffset < 0 || payloadOffset + dataLength > buffer.Length)
            {
                return false;
            }

            protocolData = new byte[dataLength];
            Buffer.BlockCopy(buffer, payloadOffset, protocolData, 0, (int)dataLength);

            return NvmeProbeUtil.HasAnyNonZeroByte(protocolData);
        }

        #endregion
    }
}
