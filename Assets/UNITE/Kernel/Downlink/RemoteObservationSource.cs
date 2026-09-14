using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Base type for one configured remote observation source. Concrete sources
    /// own their dependencies, output contract, and sampling behavior.
    /// </summary>
    public abstract class RemoteObservationSource : MonoBehaviour
    {
        [SerializeField]
        private string streamId;

        [SerializeField]
        private bool publishToDownlink = true;

        public string StreamId =>
            string.IsNullOrWhiteSpace(streamId) ? string.Empty : streamId.Trim();

        public bool PublishToDownlink => publishToDownlink;

        internal abstract bool TryCapturePackage(
            double timestampSeconds,
            out Package package);
    }

    public abstract class RemoteObservationSource<TObservation>
        : RemoteObservationSource
        where TObservation : class
    {
        internal sealed override bool TryCapturePackage(
            double timestampSeconds,
            out Package package)
        {
            if (!isActiveAndEnabled)
            {
                package = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(StreamId))
            {
                Debug.LogError(
                    $"{GetType().Name} requires a feedback stream identifier.",
                    this);
                package = null;
                return false;
            }

            if (!TryCapture(timestampSeconds, out TObservation observation))
            {
                package = null;
                return false;
            }

            if (observation == null)
            {
                Debug.LogError(
                    $"{GetType().Name} reported an observation but returned null.",
                    this);
                package = null;
                return false;
            }

            package = new Package(observation, timestampSeconds, StreamId);
            return true;
        }

        /// <summary>
        /// Samples the declared remote dependencies. Return false when this
        /// source is not scheduled to publish during the current capture step.
        /// </summary>
        protected abstract bool TryCapture(
            double timestampSeconds,
            out TObservation observation);
    }

    /// <summary>
    /// Base class for observations derived from the vehicle model's latest
    /// authoritative state. The model owns the complete state snapshot; the
    /// source decides which fields become this stream's observation payload.
    /// </summary>
    public abstract class VehicleStateObservationSource<TVehicleState, TObservation>
        : RemoteObservationSource<TObservation>
        where TVehicleState : VehicleStateContract
        where TObservation : class
    {
        [SerializeField]
        private VehicleRobotModelModule vehicle;

        protected VehicleRobotModelModule Vehicle => vehicle;

        protected sealed override bool TryCapture(
            double timestampSeconds,
            out TObservation observation)
        {
            if (!vehicle || !(vehicle.LatestState is TVehicleState state))
            {
                observation = null;
                return false;
            }

            return TryCapture(state, timestampSeconds, out observation);
        }

        /// <summary>
        /// Projects the complete vehicle state into this source's typed payload.
        /// Return false when the selected observation is not available this tick.
        /// </summary>
        protected abstract bool TryCapture(
            TVehicleState state,
            double timestampSeconds,
            out TObservation observation);
    }
}
