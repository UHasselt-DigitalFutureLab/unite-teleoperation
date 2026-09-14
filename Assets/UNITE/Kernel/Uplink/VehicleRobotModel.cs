using System;
using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    public abstract class VehicleRobotModelModule : TeleroboticsModule, IPackageConsumer
    {
        public abstract void ReceivePackage(Package package);

        /// <summary>Whether a state has been published yet.</summary>
        public bool HasState { get; protected set; }

        /// <summary>
        /// The most recently published state. A module that only holds a reference to
        /// this Kernel base type (rather than the concrete vehicle model) reads state
        /// through here and pattern-matches down to the concrete state type it expects.
        /// </summary>
        public VehicleStateContract LatestState { get; protected set; }
    }

    /// <summary>
    /// Consumes a declared command payload, applies vehicle-specific behavior,
    /// and publishes the authoritative remote vehicle state.
    /// </summary>
    public abstract class VehicleRobotModel<TCommand, TVehicleState>
        : VehicleRobotModelModule
        where TCommand : EncodedCommandContract
        where TVehicleState : VehicleStateContract
    {
        private readonly Queue<Package> pendingPackages = new Queue<Package>();

        public event Action<TVehicleState> StateUpdated;

        internal sealed override int ExecutionOrder => 600;

        public sealed override void ReceivePackage(Package package)
        {
            if (package == null)
            {
                Debug.LogError($"{GetType().Name} received a null package.", this);
                return;
            }

            pendingPackages.Enqueue(package);
        }

        internal sealed override void Step(double timestampSeconds)
        {
            while (pendingPackages.Count > 0)
            {
                Package package = pendingPackages.Dequeue();
                if (!(package.Payload is TCommand command))
                {
                    Debug.LogError(
                        $"{GetType().Name} requires a {typeof(TCommand).Name} payload.",
                        this);
                    continue;
                }

                ApplyCommand(command, timestampSeconds);
            }

            UpdateVehicleState(timestampSeconds);
        }

        protected abstract void ApplyCommand(TCommand command, double timestampSeconds);
        protected abstract void UpdateVehicleState(double timestampSeconds);

        protected void PublishState(TVehicleState state)
        {
            if (state == null)
            {
                Debug.LogError($"{GetType().Name} attempted to publish null state.", this);
                return;
            }

            if (state.TimestampSeconds != Agent.CurrentTimestampSeconds)
            {
                Debug.LogError(
                    $"{GetType().Name} returned state with a timestamp that does not " +
                    "match the current model step.", this);
                return;
            }

            LatestState = state;
            HasState = true;
            StateUpdated?.Invoke(state);
        }
    }
}
