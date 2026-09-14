using System;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// One independently configured local-to-remote command stream.
    /// </summary>
    [Serializable]
    public sealed class UplinkCommunicationChannel
    {
        [SerializeField]
        private string streamId;

        [SerializeReference]
        private CommunicationCondition condition;

        private ScheduledPackageQueue queue;
        private readonly System.Collections.Generic.List<
            ScheduledPackageQueue.PendingPackageSnapshot> pendingSnapshots =
                new System.Collections.Generic.List<
                    ScheduledPackageQueue.PendingPackageSnapshot>();

        public string StreamId =>
            string.IsNullOrWhiteSpace(streamId) ? string.Empty : streamId.Trim();

        internal bool TryInitialize(out string error)
        {
            if (string.IsNullOrWhiteSpace(StreamId))
            {
                error = "contains a channel without a command stream identifier.";
                return false;
            }

            queue = new ScheduledPackageQueue();
            condition?.Reset();
            error = null;
            return true;
        }

        internal void ReceivePackage(
            Package package,
            double ingressTimestampSeconds,
            MonoBehaviour owner)
        {
            double delaySeconds = 0d;
            if (condition != null &&
                !condition.TryGetTransmissionDelay(
                    package,
                    ingressTimestampSeconds,
                    out delaySeconds))
            {
                return;
            }

            if (double.IsNaN(delaySeconds) ||
                double.IsInfinity(delaySeconds) ||
                delaySeconds < 0d)
            {
                Debug.LogError(
                    $"{owner.GetType().Name} channel '{StreamId}' produced " +
                    $"an invalid transmission delay of {delaySeconds} seconds.",
                    owner);
                return;
            }

            queue.Enqueue(
                package,
                ingressTimestampSeconds + delaySeconds);
        }

        internal void ReleaseDuePackages(
            double timestampSeconds,
            Action<Package> releasePackage)
        {
            while (queue.TryReleaseDue(timestampSeconds, out Package package))
            {
                releasePackage?.Invoke(package);
            }
        }

        internal void CopyPendingSnapshots(
            System.Collections.Generic.List<UplinkPendingPackageSnapshot>
                destination)
        {
            if (destination == null || queue == null)
            {
                return;
            }

            pendingSnapshots.Clear();
            queue.CopyPendingSnapshots(pendingSnapshots);

            for (int index = 0; index < pendingSnapshots.Count; index++)
            {
                ScheduledPackageQueue.PendingPackageSnapshot pending =
                    pendingSnapshots[index];
                destination.Add(new UplinkPendingPackageSnapshot(
                    StreamId,
                    pending.Package,
                    pending.ReleaseTimestampSeconds));
            }
        }
    }
}
