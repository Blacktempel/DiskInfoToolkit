/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Devices;
using DiskInfoToolkit.Models;
using Microsoft.Win32.SafeHandles;

namespace DiskInfoToolkit.Core
{
    /// <summary>
    /// Opens storage device handles and sends platform-specific device control requests.
    /// </summary>
    public interface IStorageIoControl
    {
        #region Public

        /// <summary>
        /// Opens a storage device with the requested access, sharing, and creation flags.
        /// </summary>
        /// <param name="path">The platform-specific path to the device.</param>
        /// <param name="desiredAccess">The requested access flags.</param>
        /// <param name="shareMode">The requested sharing flags.</param>
        /// <param name="creationDisposition">The handle creation mode.</param>
        /// <param name="flagsAndAttributes">Additional platform-specific handle flags.</param>
        /// <returns>The opened handle, or an invalid handle when opening fails.</returns>
        SafeFileHandle OpenDevice(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes);

        /// <summary>
        /// Sends a raw device control request and receives its response.
        /// </summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="ioControlCode">The control code to send.</param>
        /// <param name="inBuffer">The optional request buffer.</param>
        /// <param name="outBuffer">The optional response buffer.</param>
        /// <param name="bytesReturned">The number of response bytes reported by the platform.</param>
        /// <returns>Whether the request succeeded.</returns>
        bool SendRawIoControl(SafeFileHandle handle, uint ioControlCode, byte[] inBuffer, byte[] outBuffer, out int bytesReturned);

        /// <summary>Reads the storage device descriptor.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="descriptor">The descriptor returned on success.</param>
        /// <returns>Whether the descriptor was read.</returns>
        bool TryGetStorageDeviceDescriptor(SafeFileHandle handle, out StorageDeviceDescriptorInfo descriptor);

        /// <summary>Reads the storage adapter descriptor.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="descriptor">The descriptor returned on success.</param>
        /// <returns>Whether the descriptor was read.</returns>
        bool TryGetStorageAdapterDescriptor(SafeFileHandle handle, out StorageAdapterDescriptorInfo descriptor);

        /// <summary>Reads the raw drive layout response.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="rawLayout">The layout bytes returned on success.</param>
        /// <returns>Whether the layout was read.</returns>
        bool TryGetDriveLayout(SafeFileHandle handle, out byte[] rawLayout);

        /// <summary>Reads the SCSI address of the device.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="scsiAddress">The address returned on success.</param>
        /// <returns>Whether the address was read.</returns>
        bool TryGetScsiAddress(SafeFileHandle handle, out ScsiAddressInfo scsiAddress);

        /// <summary>Reads the operating system's storage device number.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="info">The device number returned on success.</param>
        /// <returns>Whether the device number was read.</returns>
        bool TryGetStorageDeviceNumber(SafeFileHandle handle, out StorageDeviceNumberInfo info);

        /// <summary>Reads the extended drive geometry.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="info">The geometry returned on success.</param>
        /// <returns>Whether the geometry was read.</returns>
        bool TryGetDriveGeometryEx(SafeFileHandle handle, out DiskGeometryInfo info);

        /// <summary>Reads the device's predictive failure information.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="info">The failure information returned on success.</param>
        /// <returns>Whether the information was read.</returns>
        bool TryGetPredictFailure(SafeFileHandle handle, out PredictFailureInfo info);

        /// <summary>Reads the protocol type reported by an SFF storage device.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="protocolType">The protocol type returned on success.</param>
        /// <returns>Whether the protocol type was read.</returns>
        bool TryGetSffDiskDeviceProtocol(SafeFileHandle handle, out StorageProtocolType protocolType);

        /// <summary>Reads the version of the device's SMART interface.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="info">The SMART version returned on success.</param>
        /// <returns>Whether the version was read.</returns>
        bool TryGetSmartVersion(SafeFileHandle handle, out SmartVersionInfo info);

        /// <summary>Sends a SCSI pass-through request.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="requestBuffer">The request bytes.</param>
        /// <param name="responseBuffer">The response bytes.</param>
        /// <param name="bytesReturned">The reported response byte count.</param>
        /// <returns>Whether the request succeeded.</returns>
        bool TryScsiPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);

        /// <summary>Sends a SCSI miniport request.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="requestBuffer">The request bytes.</param>
        /// <param name="responseBuffer">The response bytes.</param>
        /// <param name="bytesReturned">The reported response byte count.</param>
        /// <returns>Whether the request succeeded.</returns>
        bool TryScsiMiniport(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);

        /// <summary>Sends an ATA pass-through request.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="requestBuffer">The request bytes.</param>
        /// <param name="responseBuffer">The response bytes.</param>
        /// <param name="bytesReturned">The reported response byte count.</param>
        /// <returns>Whether the request succeeded.</returns>
        bool TryAtaPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);

        /// <summary>Sends an IDE pass-through request.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="requestBuffer">The request bytes.</param>
        /// <param name="responseBuffer">The response bytes.</param>
        /// <param name="bytesReturned">The reported response byte count.</param>
        /// <returns>Whether the request succeeded.</returns>
        bool TryIdePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);

        /// <summary>Requests SMART data from the device.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="requestBuffer">The request bytes.</param>
        /// <param name="responseBuffer">The response bytes.</param>
        /// <param name="bytesReturned">The reported response byte count.</param>
        /// <returns>Whether the request succeeded.</returns>
        bool TrySmartReceiveDriveData(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);

        /// <summary>Sends a SMART command to the device.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="requestBuffer">The request bytes.</param>
        /// <param name="responseBuffer">The response bytes.</param>
        /// <param name="bytesReturned">The reported response byte count.</param>
        /// <returns>Whether the request succeeded.</returns>
        bool TrySmartSendDriveCommand(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);

        /// <summary>Sends an Intel NVMe pass-through request.</summary>
        /// <param name="handle">The device handle.</param>
        /// <param name="requestBuffer">The request bytes.</param>
        /// <param name="responseBuffer">The response bytes.</param>
        /// <param name="bytesReturned">The reported response byte count.</param>
        /// <returns>Whether the request succeeded.</returns>
        bool TryIntelNvmePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);

        #endregion
    }
}
