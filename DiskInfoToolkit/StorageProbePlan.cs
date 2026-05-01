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
    /// Stores the successful probe operations for a storage device.
    /// </summary>
    public sealed class StorageProbePlan
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageProbePlan"/> class.
        /// </summary>
        public StorageProbePlan()
        {
            DevicePath = string.Empty;
            DeviceInstanceID = string.Empty;
            ControllerService = string.Empty;
            ControllerIdentifier = string.Empty;
            SuccessfulOperations = new List<StorageProbeOperation>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether the plan was created from a completed full probe run.
        /// </summary>
        public bool IsInitialized { get; internal set; }

        /// <summary>
        /// Gets or sets the probe strategy that produced this plan.
        /// </summary>
        public ProbeStrategy Strategy { get; internal set; }

        /// <summary>
        /// Gets or sets the controller family that produced this plan.
        /// </summary>
        public StorageControllerFamily ControllerFamily { get; internal set; }

        /// <summary>
        /// Gets or sets the device path that produced this plan.
        /// </summary>
        public string DevicePath { get; internal set; }

        /// <summary>
        /// Gets or sets the device instance ID that produced this plan.
        /// </summary>
        public string DeviceInstanceID { get; internal set; }

        /// <summary>
        /// Gets or sets the controller service that produced this plan.
        /// </summary>
        public string ControllerService { get; internal set; }

        /// <summary>
        /// Gets or sets the controller identifier that produced this plan.
        /// </summary>
        public string ControllerIdentifier { get; internal set; }

        /// <summary>
        /// <inheritdoc cref="SuccessfulOperations"/>
        /// </summary>
        public IReadOnlyList<StorageProbeOperation> SuccessfulOperationsStore => SuccessfulOperations;

        /// <summary>
        /// Gets or sets the successful standard-property and strategy-specific probe operations in execution order.
        /// </summary>
        internal List<StorageProbeOperation> SuccessfulOperations { get; set; } = new List<StorageProbeOperation>();

        #endregion

        #region Public

        /// <summary>
        /// Creates a new uninitialized plan for the specified storage device.
        /// </summary>
        /// <param name="device">The storage device.</param>
        /// <returns>A new <see cref="StorageProbePlan"/> instance.</returns>
        public static StorageProbePlan CreateForFullProbe(StorageDevice device)
        {
            return CreateForFullProbe(device, null);
        }

        /// <summary>
        /// Creates a new uninitialized plan for the specified storage device and preserves already known standard-property operations.
        /// </summary>
        /// <param name="device">The storage device.</param>
        /// <param name="previousPlan">The previous probe plan.</param>
        /// <returns>A new <see cref="StorageProbePlan"/> instance.</returns>
        public static StorageProbePlan CreateForFullProbe(StorageDevice device, StorageProbePlan previousPlan)
        {
            var plan = new StorageProbePlan();

            if (previousPlan != null && previousPlan.SuccessfulOperations != null)
            {
                foreach (var operation in previousPlan.SuccessfulOperations)
                {
                    if (IsStandardPropertyOperation(operation))
                    {
                        plan.RecordSuccess(operation);
                    }
                }
            }

            plan.CaptureDeviceState(device);
            return plan;
        }

        /// <summary>
        /// Captures the stable device/controller state used to validate this plan during future refreshes.
        /// </summary>
        /// <param name="device">The storage device.</param>
        public void CaptureDeviceState(StorageDevice device)
        {
            if (device == null)
            {
                return;
            }

            Strategy = device.ProbeStrategy;
            ControllerFamily = device.Controller != null ? device.Controller.Family : StorageControllerFamily.Unknown;
            DevicePath = device.DevicePath ?? string.Empty;
            DeviceInstanceID = device.DeviceInstanceID ?? string.Empty;
            ControllerService = device.Controller != null ? device.Controller.Service ?? string.Empty : string.Empty;
            ControllerIdentifier = device.Controller != null ? device.Controller.Identifier ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// Determines whether the cached probe plan can still be used for the specified storage device.
        /// </summary>
        /// <param name="device">The storage device.</param>
        /// <returns>Whether this probe plan matches the current device/controller state.</returns>
        public bool IsCompatibleWith(StorageDevice device)
        {
            if (!IsInitialized || device == null)
            {
                return false;
            }

            if (Strategy != device.ProbeStrategy)
            {
                return false;
            }

            if (ControllerFamily != (device.Controller != null ? device.Controller.Family : StorageControllerFamily.Unknown))
            {
                return false;
            }

            if (!string.Equals(DevicePath, device.DevicePath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(DeviceInstanceID, device.DeviceInstanceID ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(ControllerService, device.Controller != null ? device.Controller.Service ?? string.Empty : string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(ControllerIdentifier, device.Controller != null ? device.Controller.Identifier ?? string.Empty : string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether the cached standard-property part of the probe plan can still be used for the specified storage device.
        /// </summary>
        /// <param name="device">The storage device.</param>
        /// <returns>Whether the standard-property probe plan matches the current device/controller state.</returns>
        public bool IsStandardPropertyCompatibleWith(StorageDevice device)
        {
            if (!IsInitialized || device == null)
            {
                return false;
            }

            if (!string.Equals(DevicePath, device.DevicePath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(DeviceInstanceID, device.DeviceInstanceID ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(ControllerService, device.Controller != null ? device.Controller.Service ?? string.Empty : string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(ControllerIdentifier, device.Controller != null ? device.Controller.Identifier ?? string.Empty : string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether the specified operation belongs to the standard property probing phase.
        /// </summary>
        /// <param name="operation">The probe operation.</param>
        /// <returns>Whether the specified operation belongs to the standard property probing phase.</returns>
        public static bool IsStandardPropertyOperation(StorageProbeOperation operation)
        {
            switch (operation)
            {
                case StorageProbeOperation.StorageDeviceDescriptor:
                case StorageProbeOperation.StorageAdapterDescriptor:
                case StorageProbeOperation.ScsiAddress:
                case StorageProbeOperation.SmartVersion:
                case StorageProbeOperation.StorageDeviceNumber:
                case StorageProbeOperation.DriveGeometryEx:
                case StorageProbeOperation.PredictFailure:
                case StorageProbeOperation.SffDiskDeviceProtocol:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Records a successful probe operation.
        /// </summary>
        /// <param name="operation">The successful probe operation.</param>
        public void RecordSuccess(StorageProbeOperation operation)
        {
            if (!SuccessfulOperations.Contains(operation))
            {
                SuccessfulOperations.Add(operation);
            }
        }

        /// <summary>
        /// Determines whether the specified operation is part of this plan.
        /// </summary>
        /// <param name="operation">The probe operation.</param>
        /// <returns>Whether the operation is part of this plan.</returns>
        public bool Contains(StorageProbeOperation operation)
        {
            return SuccessfulOperations.Contains(operation);
        }

        #endregion
    }
}
