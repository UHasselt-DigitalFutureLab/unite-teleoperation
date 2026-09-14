using Unite.Core;

namespace Unite.Kernel
{
    public readonly struct UplinkPendingPackageSnapshot
    {
        public UplinkPendingPackageSnapshot(
            string streamId,
            Package package,
            double releaseTimestampSeconds)
        {
            StreamId = streamId;
            Package = package;
            ReleaseTimestampSeconds = releaseTimestampSeconds;
        }

        public string StreamId { get; }
        public Package Package { get; }
        public double ReleaseTimestampSeconds { get; }
    }
}
