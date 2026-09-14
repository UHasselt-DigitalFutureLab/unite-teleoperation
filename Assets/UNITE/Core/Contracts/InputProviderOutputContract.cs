using System;

namespace Unite.Core
{
    /// <summary>
    /// Base type for a typed snapshot produced by an input provider.
    /// </summary>
    public abstract class InputProviderOutputContract
    {
        /// <summary>
        /// Monotonic capture time, in seconds since the Unity application started.
        /// </summary>
        public double TimestampSeconds { get; }

        protected InputProviderOutputContract(double timestampSeconds)
        {
            if (double.IsNaN(timestampSeconds) ||
                double.IsInfinity(timestampSeconds) ||
                timestampSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestampSeconds),
                    timestampSeconds,
                    "The capture timestamp must be a finite, non-negative value.");
            }

            TimestampSeconds = timestampSeconds;
        }
    }
}

