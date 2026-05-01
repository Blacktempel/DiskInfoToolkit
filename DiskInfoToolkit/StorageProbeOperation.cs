/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit
{
    /// <summary>
    /// Defines individual probe operations that can provide disk data.
    /// </summary>
    public enum StorageProbeOperation
    {
        StorageDeviceDescriptor,
        StorageAdapterDescriptor,
        ScsiAddress,
        SmartVersion,
        StorageDeviceNumber,
        DriveGeometryEx,
        PredictFailure,
        SffDiskDeviceProtocol,

        StandardAtaIdentify,
        AtaSmart,
        ScsiSatIdentify,
        ScsiSatSmart,
        ScsiInquiry,
        ScsiCapacity,
        VrocNvmePassThrough,
        IntelRaidMiniport,
        IntelRaidMiniportSignatureSweep,
        StandardNvme,
        IntelNvme,
        SamsungNvmeScsi,
        UsbBridge,
        UsbMassStorage,
        UsbVendorScsiSmart,
        MegaRaidMiniport,
        HighPointMiniport,
        CsmiDriverInfo,
        CsmiTopology,
        CsmiAta,
        CsmiPortComposite,
        RaidMasterPortComposite,
        RaidPortComposite,
        ScsiMiniportPort,
        RaidScsiPort,
        RaidSatPort,
        RaidControllerPort,
    }
}
