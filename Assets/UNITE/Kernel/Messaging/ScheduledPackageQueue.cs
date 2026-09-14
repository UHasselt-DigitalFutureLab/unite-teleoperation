using System.Collections.Generic;
using Unite.Core;

namespace Unite.Kernel
{
    internal sealed class ScheduledPackageQueue
    {
        internal readonly struct PendingPackageSnapshot
        {
            public PendingPackageSnapshot(
                Package package,
                double releaseTimestampSeconds)
            {
                Package = package;
                ReleaseTimestampSeconds = releaseTimestampSeconds;
            }

            public Package Package { get; }
            public double ReleaseTimestampSeconds { get; }
        }

        private sealed class ScheduledPackage
        {
            public Package Package;
            public double ReleaseTimestampSeconds;
            public long Sequence;
        }

        private readonly List<ScheduledPackage> queue =
            new List<ScheduledPackage>();
        private long nextSequence;

        public void Enqueue(Package package, double releaseTimestampSeconds)
        {
            var scheduledPackage = new ScheduledPackage
            {
                Package = package,
                ReleaseTimestampSeconds = releaseTimestampSeconds,
                Sequence = nextSequence++
            };

            int low = 0;
            int high = queue.Count;

            while (low < high)
            {
                int middle = low + (high - low) / 2;
                ScheduledPackage candidate = queue[middle];
                bool candidateComesFirst =
                    candidate.ReleaseTimestampSeconds <
                        scheduledPackage.ReleaseTimestampSeconds ||
                    (candidate.ReleaseTimestampSeconds ==
                        scheduledPackage.ReleaseTimestampSeconds &&
                     candidate.Sequence < scheduledPackage.Sequence);

                if (candidateComesFirst)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            queue.Insert(low, scheduledPackage);
        }

        public bool TryReleaseDue(
            double timestampSeconds,
            out Package package)
        {
            if (queue.Count == 0 ||
                queue[0].ReleaseTimestampSeconds > timestampSeconds)
            {
                package = null;
                return false;
            }

            package = queue[0].Package;
            queue.RemoveAt(0);
            return true;
        }

        public void CopyPendingSnapshots(
            List<PendingPackageSnapshot> destination)
        {
            if (destination == null)
            {
                return;
            }

            for (int index = 0; index < queue.Count; index++)
            {
                ScheduledPackage scheduledPackage = queue[index];
                destination.Add(new PendingPackageSnapshot(
                    scheduledPackage.Package,
                    scheduledPackage.ReleaseTimestampSeconds));
            }
        }
    }
}
