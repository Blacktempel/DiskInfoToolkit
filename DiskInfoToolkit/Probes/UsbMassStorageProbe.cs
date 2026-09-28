/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

using DiskInfoToolkit.Core;
using DiskInfoToolkit.Devices;

namespace DiskInfoToolkit.Probes
{
    public static class UsbMassStorageProbe
    {
        #region Public

        public static bool TryPopulateData(StorageDevice device, IStorageIoControl ioControl)
        {
            bool identified = false;
            bool smart = false;

            if (ScsiSatProbe.TryPopulateIdentifyData(device, ioControl))
            {
                identified = true;
                ProbeTraceRecorder.Add(device, "USB mass-storage: SAT identify succeeded.");
            }
            else if (StandardAtaProbe.TryPopulateIdentifyData(device, ioControl))
            {
                identified = true;
                ProbeTraceRecorder.Add(device, "USB mass-storage: ATA identify succeeded.");
            }
            else if (ScsiInquiryProbe.TryPopulateData(device, ioControl))
            {
                identified = true;
                ProbeTraceRecorder.Add(device, "USB mass-storage: SCSI inquiry succeeded.");
            }

            if (ScsiSatProbe.TryPopulateSmartData(device, ioControl))
            {
                smart = true;
                ProbeTraceRecorder.Add(device, "USB mass-storage: SAT SMART succeeded.");
            }
            else if (SmartProbe.TryPopulateSmartData(device, ioControl))
            {
                smart = true;
                ProbeTraceRecorder.Add(device, "USB mass-storage: ATA SMART succeeded.");
            }

            if (ScsiCapacityProbe.TryPopulateData(device, ioControl))
            {
                identified = true;
                ProbeTraceRecorder.Add(device, "USB mass-storage: SCSI capacity succeeded.");
            }

            UsbBridgeClassifier.ApplyInquiryHeuristics(device);
            return identified || smart;
        }

        #endregion
    }
}
