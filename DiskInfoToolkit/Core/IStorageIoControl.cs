/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Models;
using Microsoft.Win32.SafeHandles;

namespace DiskInfoToolkit.Core
{
    public interface IStorageIoControl
    {
        SafeFileHandle OpenDevice(string path, uint desiredAccess, uint shareMode, uint creationDisposition, uint flagsAndAttributes);
        bool SendRawIoControl(SafeFileHandle handle, uint ioControlCode, byte[] inBuffer, byte[] outBuffer, out int bytesReturned);
        bool TryGetStorageDeviceDescriptor(SafeFileHandle handle, out StorageDeviceDescriptorInfo descriptor);
        bool TryGetStorageAdapterDescriptor(SafeFileHandle handle, out StorageAdapterDescriptorInfo descriptor);
        bool TryGetDriveLayout(SafeFileHandle handle, out byte[] rawLayout);
        bool TryGetScsiAddress(SafeFileHandle handle, out ScsiAddressInfo scsiAddress);
        bool TryGetStorageDeviceNumber(SafeFileHandle handle, out StorageDeviceNumberInfo info);
        bool TryGetDriveGeometryEx(SafeFileHandle handle, out DiskGeometryInfo info);
        bool TryGetPredictFailure(SafeFileHandle handle, out PredictFailureInfo info);
        bool TryGetSffDiskDeviceProtocol(SafeFileHandle handle, out StorageProtocolType protocolType);
        bool TryGetSmartVersion(SafeFileHandle handle, out SmartVersionInfo info);
        bool TryScsiPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);
        bool TryScsiMiniport(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);
        bool TryAtaPassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);
        bool TryIdePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);
        bool TrySmartReceiveDriveData(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);
        bool TrySmartSendDriveCommand(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);
        bool TryIntelNvmePassThrough(SafeFileHandle handle, byte[] requestBuffer, byte[] responseBuffer, out int bytesReturned);
    }
}
