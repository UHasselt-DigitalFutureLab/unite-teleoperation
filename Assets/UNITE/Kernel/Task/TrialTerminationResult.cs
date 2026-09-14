namespace Unite.Kernel
{
    /// <summary>
    /// Result reported by a concrete task termination criterion.
    /// </summary>
    public sealed class TrialTerminationResult
    {
        public TrialTerminationOutcome Outcome { get; }
        public string Reason { get; }

        public TrialTerminationResult(
            TrialTerminationOutcome outcome,
            string reason)
        {
            Outcome = outcome;
            Reason = reason;
        }
    }
}
