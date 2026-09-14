using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Base type for one configured capture implementation.
    /// </summary>
    public abstract class DataCaptureImplementation : MonoBehaviour
    {
        private DataCaptureAndLoggingModule owner;

        protected TeleroboticsAgent Agent { get; private set; }

        internal void InitializeCapture(
            DataCaptureAndLoggingModule module,
            TeleroboticsAgent agent)
        {
            owner = module;
            Agent = agent;
            OnCaptureInitialized();
        }

        internal void StepCapture(double timestampSeconds)
        {
            if (isActiveAndEnabled)
            {
                OnCaptureStep(timestampSeconds);
            }
        }

        internal void NotifyTrialEnded(TrialEndedEvent trialEndedEvent)
        {
            if (isActiveAndEnabled)
            {
                OnTrialEnded(trialEndedEvent);
            }
        }

        protected void Record(
            string dataId,
            object value,
            double timestampSeconds,
            string unit = null)
        {
            if (owner == null)
            {
                Debug.LogError(
                    $"{GetType().Name} tried to record before initialization.",
                    this);
                return;
            }

            owner.RecordObservation(new DataCaptureObservation(
                GetType().Name,
                dataId,
                value,
                timestampSeconds,
                unit));
        }

        protected virtual void OnCaptureInitialized()
        {
        }

        protected virtual void OnCaptureStep(double timestampSeconds)
        {
        }

        protected virtual void OnTrialEnded(TrialEndedEvent trialEndedEvent)
        {
        }
    }
}
