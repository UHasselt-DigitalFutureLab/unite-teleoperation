namespace Unite.Core
{
    /// <summary>
    /// Minimal wrapper for study-defined data travelling through the pipeline.
    /// </summary>
    public sealed class Package
    {
        public object Payload { get; set; }
        public double SourceTimestampSeconds { get; set; }
        public string StreamId { get; set; }

        public Package(object payload, double sourceTimestampSeconds)
            : this(payload, sourceTimestampSeconds, null)
        {
        }

        public Package(
            object payload,
            double sourceTimestampSeconds,
            string streamId)
        {
            Payload = payload;
            SourceTimestampSeconds = sourceTimestampSeconds;
            StreamId = streamId;
        }
    }
}
