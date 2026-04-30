/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2026 Florian K.
 */

namespace DiskInfoToolkit.Constants
{
    internal static class ControllerServiceGroups
    {
        #region Fields

        public static readonly string[] NvmeControllerServices =
        {
            ControllerServiceNames.StorNvme,
            ControllerServiceNames.Nvme,
            ControllerServiceNames.Nvme2K,
            ControllerServiceNames.IaNvme,
            ControllerServiceNames.MtInvme,
            ControllerServiceNames.SecNvme
        };

        public static readonly string[] IntelRstControllerServices =
        {
            ControllerServiceNames.IaStorA,
            ControllerServiceNames.IaStorAC,
            ControllerServiceNames.IaStorAV,
            ControllerServiceNames.IaStorAVC,
            ControllerServiceNames.IaStorVD
        };

        public static readonly string[] IntelRaidProbeServices =
        {
            ControllerServiceNames.IaVroc,
            ControllerServiceNames.IaStorAC,
            ControllerServiceNames.IaStorAVC,
            ControllerServiceNames.IaStorVD
        };

        public static readonly string[] RocketRaidControllerServices =
        {
            ControllerServiceNames.HptIoP,
            ControllerServiceNames.HptMv,
            ControllerServiceNames.Hpt3xx,
            ControllerServiceNames.Rr62x,
            ControllerServiceNames.Rr64x,
            ControllerServiceNames.Rr24xx,
            ControllerServiceNames.Rr26xx,
            ControllerServiceNames.RsNvme
        };

        public static readonly string[] MegaRaidControllerServices =
        {
            ControllerServiceNames.MegaSas,
            ControllerServiceNames.MegaSas2,
            ControllerServiceNames.MegaSas2i,
            ControllerServiceNames.MegaSas35,
            ControllerServiceNames.MegaSas35i,
            ControllerServiceNames.PercSas,
            ControllerServiceNames.PercSas2,
            ControllerServiceNames.PercSas3
        };

        public static readonly string[] UsbMassStorageServices =
        {
            ControllerServiceNames.UaspStor,
            ControllerServiceNames.UsbStor,
            ControllerServiceNames.AsusStpt
        };

        public static readonly string[] UsbMassStorageServicesWithTrailingVariant =
        {
            ControllerServiceNames.UaspStor,
            ControllerServiceNames.UsbStor,
            ControllerServiceNames.UsbStorWithTrailingSpace,
            ControllerServiceNames.AsusStpt
        };

        public static readonly string[] SdControllerServices =
        {
            ControllerServiceNames.RtsUer,
            ControllerServiceNames.SdStor
        };

        public static readonly string[] SasServicePrefixes =
        {
            ControllerServiceNames.LsiSas,
            ControllerServiceNames.ItSas35
        };

        public static readonly string[] AhciControllerServices =
        {
            ControllerServiceNames.Storahci
        };

        public static readonly string[] AmdSataControllerServices =
        {
            ControllerServiceNames.AmdSata,
            ControllerServiceNames.AmdSataAlt
        };

        public static readonly string[] ExtendedLsiSasServiceNames =
        {
            ControllerServiceNames.MegaSas,
            ControllerServiceNames.MegaSas2,
            ControllerServiceNames.MegaSas2i,
            ControllerServiceNames.MegaSas35,
            ControllerServiceNames.MegaSas35i,
            ControllerServiceNames.LsiSas,
            ControllerServiceNames.LsiSas2,
            ControllerServiceNames.LsiSas2i,
            ControllerServiceNames.LsiSas3,
            ControllerServiceNames.LsiSas3i
        };

        public static readonly string[] ExtendedItSas35ServiceNames =
        {
            ControllerServiceNames.ItSas35,
            ControllerServiceNames.ItSas35i
        };

        #endregion
    }
}
