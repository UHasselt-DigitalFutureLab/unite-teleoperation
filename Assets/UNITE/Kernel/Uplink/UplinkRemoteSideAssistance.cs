using System;
using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Optional remote-side attachment point for packages released by the uplink.
    /// The module may also read the latest locally captured remote observations;
    /// those observations do not pass through communication delay.
    /// </summary>
    public abstract class UplinkRemoteSideAssistanceModule
        : TeleroboticsModule, IPackageSource, IPackageConsumer
    {
        private readonly Queue<Package> pendingPackages = new Queue<Package>();
        private readonly Dictionary<string, Package> latestObservationsByStreamId =
            new Dictionary<string, Package>(StringComparer.Ordinal);

        public event Action<Package> PackageProduced;

        internal sealed override int ExecutionOrder => 500;

        public void ReceivePackage(Package package)
        {
            if (package == null)
            {
                Debug.LogError($"{GetType().Name} received a null package.", this);
                return;
            }

            pendingPackages.Enqueue(package);
        }

        /// <summary>
        /// Receives a locally captured observation package. This is a separate
        /// input from command packages: a pre-control refresh is available for
        /// the current assistance step, while the post-control refresh becomes
        /// the observation available on the next control tick.
        /// </summary>
        public void ReceiveObservation(Package package)
        {
            if (package == null)
            {
                Debug.LogError($"{GetType().Name} received a null observation.", this);
                return;
            }

            string streamId = NormalizeStreamId(package.StreamId);
            if (string.IsNullOrWhiteSpace(streamId))
            {
                Debug.LogError(
                    $"{GetType().Name} received an observation without a StreamId.",
                    this);
                return;
            }

            latestObservationsByStreamId[streamId] = package;
        }

        /// <summary>
        /// Reads the latest locally captured observation for a stream. The result
        /// is the observation available at the remote control tick, not a delayed
        /// downlink package.
        /// </summary>
        protected bool TryGetLatestObservation(
            string streamId,
            out Package package)
        {
            return latestObservationsByStreamId.TryGetValue(
                NormalizeStreamId(streamId),
                out package);
        }

        /// <summary>
        /// Reads and type-checks the latest locally captured observation.
        /// </summary>
        protected bool TryGetLatestObservation<TObservation>(
            string streamId,
            out TObservation observation)
            where TObservation : class
        {
            observation = null;
            if (!TryGetLatestObservation(streamId, out Package package))
            {
                return false;
            }

            observation = package.Payload as TObservation;
            return observation != null;
        }

        internal sealed override void Step(double timestampSeconds)
        {
            while (pendingPackages.Count > 0)
            {
                Package input = pendingPackages.Dequeue();

                if (!TryProcessPackage(
                        input,
                        timestampSeconds,
                        out Package output))
                {
                    continue;
                }

                if (output == null)
                {
                    Debug.LogError(
                        $"{GetType().Name} reported an assisted package but " +
                        "returned null.",
                        this);
                    continue;
                }

                PackageProduced?.Invoke(output);
            }
        }

        private static string NormalizeStreamId(string streamId)
        {
            return string.IsNullOrWhiteSpace(streamId)
                ? string.Empty
                : streamId.Trim();
        }

        /// <summary>
        /// Processes one received command package and returns either no package or
        /// exactly one package.
        /// </summary>
        protected abstract bool TryProcessPackage(
            Package package,
            double timestampSeconds,
            out Package output);
    }
}
