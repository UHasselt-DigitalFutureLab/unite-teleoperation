using System;
using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Coordinates configured remote sources across two control phases. The
    /// latest locally captured observations are available to remote-side
    /// assistance before control. After the vehicle model updates, a new capture
    /// updates that local state and publishes feedback for delayed downlink.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RemoteObservationAndStateCapture
        : TeleroboticsModule, IPackageSource
    {
        [SerializeField]
        private RemoteObservationSource[] sources = Array.Empty<RemoteObservationSource>();

        private readonly Dictionary<string, Package> latestObservationsByStreamId =
            new Dictionary<string, Package>(StringComparer.Ordinal);
        private readonly Dictionary<string, Package> preControlObservationsByStreamId =
            new Dictionary<string, Package>(StringComparer.Ordinal);

        /// <summary>
        /// Raised when a locally available observation changes. Consumers such as
        /// remote-side assistance read this path without communication delay.
        /// </summary>
        public event Action<Package> LocalObservationProduced;

        /// <summary>
        /// Raised only for post-control feedback packages that should enter the
        /// downlink communication pipeline.
        /// </summary>
        public event Action<Package> PackageProduced;

        // Refresh the local observation after uplink releases due commands, but
        // before remote-side assistance decides how to apply them.
        internal sealed override int ExecutionOrder => 450;

        protected override void OnInitialize()
        {
            latestObservationsByStreamId.Clear();
            preControlObservationsByStreamId.Clear();

            if (sources == null || sources.Length == 0)
            {
                Debug.LogError(
                    $"{GetType().Name} requires at least one remote observation source.",
                    this);
                enabled = false;
                return;
            }

            for (int index = 0; index < sources.Length; index++)
            {
                if (sources[index] != null)
                {
                    continue;
                }

                Debug.LogError(
                    $"{GetType().Name} contains an unassigned source at index {index}.",
                    this);
                enabled = false;
                return;
            }
        }

        internal sealed override void Step(double timestampSeconds)
        {
            // First refresh of every control tick. This snapshot represents the
            // remote robot and environment before remote-side assistance acts on
            // a released command. It is local only and never enters downlink.
            CaptureObservations(timestampSeconds, publishFeedback: false);
        }

        /// <summary>
        /// Captures the state resulting from the current vehicle-model step. The
        /// updated observation becomes available to remote-side assistance on the
        /// next control tick and the same feedback package enters the downlink.
        /// </summary>
        internal void CaptureFinalObservation(double timestampSeconds)
        {
            // Second refresh of every control tick. It replaces the local state
            // used by assistance above, and this final snapshot is the feedback
            // package that enters the delayed downlink.
            CaptureObservations(timestampSeconds, publishFeedback: true);
        }

        /// <summary>
        /// Reads the latest locally captured observation for a stream. This is
        /// never delayed and is intended for remote-side dependencies.
        /// </summary>
        public bool TryGetLatestObservation(string streamId, out Package package)
        {
            string normalized = NormalizeStreamId(streamId);
            return latestObservationsByStreamId.TryGetValue(normalized, out package);
        }

        private void CaptureObservations(
            double timestampSeconds,
            bool publishFeedback)
        {
            for (int index = 0; index < sources.Length; index++)
            {
                RemoteObservationSource source = sources[index];
                if (source.TryCapturePackage(timestampSeconds, out Package package))
                {
                    string streamId = NormalizeStreamId(package.StreamId);
                    if (publishFeedback)
                    {
                        // Assistance has already completed this tick, so a
                        // local-only pre-control frame is no longer needed.
                        ReleasePreControlObservation(streamId);
                    }
                    else
                    {
                        ReplacePreControlObservation(streamId, package);
                    }

                    latestObservationsByStreamId[streamId] = package;
                    LocalObservationProduced?.Invoke(package);

                    if (publishFeedback && source.PublishToDownlink)
                    {
                        PackageProduced?.Invoke(package);
                    }
                }
            }
        }

        private static string NormalizeStreamId(string streamId)
        {
            return string.IsNullOrWhiteSpace(streamId)
                ? string.Empty
                : streamId.Trim();
        }

        private void ReplacePreControlObservation(string streamId, Package package)
        {
            ReleasePreControlObservation(streamId);
            preControlObservationsByStreamId[streamId] = package;
        }

        private void ReleasePreControlObservation(string streamId)
        {
            if (!preControlObservationsByStreamId.TryGetValue(
                    streamId,
                    out Package previous))
            {
                return;
            }

            preControlObservationsByStreamId.Remove(streamId);
            if (previous.Payload is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private void OnDestroy()
        {
            foreach (Package package in preControlObservationsByStreamId.Values)
            {
                if (package.Payload is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            preControlObservationsByStreamId.Clear();
        }
    }
}
