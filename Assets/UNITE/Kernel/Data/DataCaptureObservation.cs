namespace Unite.Kernel
{
    /// <summary>
    /// One timestamped raw variable or event recorded during a trial.
    /// </summary>
    public sealed class DataCaptureObservation
    {
        public string SourceName { get; }
        public string DataId { get; }
        public object Value { get; }
        public double TimestampSeconds { get; }
        public string Unit { get; }

        public DataCaptureObservation(
            string sourceName,
            string dataId,
            object value,
            double timestampSeconds,
            string unit)
        {
            SourceName = sourceName;
            DataId = dataId;
            Value = value;
            TimestampSeconds = timestampSeconds;
            Unit = unit;
        }
    }
}
