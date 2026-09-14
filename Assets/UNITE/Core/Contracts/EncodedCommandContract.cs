using System;

namespace Unite.Core
{
    /// <summary>
    /// Base type for a device-independent command representation produced by
    /// Command Mapping and Encoding.
    /// </summary>
    public abstract class EncodedCommandContract
    {
        /// <summary>
        /// Timestamp of the provider output from which this command was derived.
        /// </summary>
        public double SourceTimestampSeconds { get; }

        /// <summary>
        /// Timestamp at which mapping and encoding produced this command.
        /// </summary>
        public double TimestampSeconds { get; }

        protected EncodedCommandContract(
            double sourceTimestampSeconds,
            double timestampSeconds)
        {
            ValidateTimestamp(sourceTimestampSeconds, nameof(sourceTimestampSeconds));
            ValidateTimestamp(timestampSeconds, nameof(timestampSeconds));

            if (timestampSeconds < sourceTimestampSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestampSeconds),
                    timestampSeconds,
                    "The encoding timestamp cannot precede its source timestamp.");
            }

            SourceTimestampSeconds = sourceTimestampSeconds;
            TimestampSeconds = timestampSeconds;
        }

        private static void ValidateTimestamp(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "The timestamp must be a finite, non-negative value.");
            }
        }
    }
}

