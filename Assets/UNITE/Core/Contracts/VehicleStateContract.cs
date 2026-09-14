using System;

namespace Unite.Core
{
    /// <summary>
    /// Base type for authoritative state published by a Vehicle/Robot Model.
    /// </summary>
    public abstract class VehicleStateContract
    {
        public double TimestampSeconds { get; }

        protected VehicleStateContract(double timestampSeconds)
        {
            if (double.IsNaN(timestampSeconds) ||
                double.IsInfinity(timestampSeconds) ||
                timestampSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
            }

            TimestampSeconds = timestampSeconds;
        }
    }
}
