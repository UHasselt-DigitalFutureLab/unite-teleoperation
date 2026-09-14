namespace Unite.Kernel
{
    /// <summary>
    /// Timestamped event emitted when the active trial reaches a terminal state.
    /// </summary>
    public sealed class TrialEndedEvent
    {
        public TrialTerminationOutcome Outcome { get; }
        public string Reason { get; }
        public string CriterionName { get; }
        public double TimestampSeconds { get; }

        public TrialEndedEvent(
            TrialTerminationOutcome outcome,
            string reason,
            string criterionName,
            double timestampSeconds)
        {
            Outcome = outcome;
            Reason = reason;
            CriterionName = criterionName;
            TimestampSeconds = timestampSeconds;
        }
    }
}
