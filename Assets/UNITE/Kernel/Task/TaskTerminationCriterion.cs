using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Base type for one configured task completion, failure, timeout, or abort
    /// criterion.
    /// </summary>
    public abstract class TaskTerminationCriterion : MonoBehaviour
    {
        internal bool TryEvaluateCriterion(
            double timestampSeconds,
            out TrialTerminationResult result)
        {
            if (!isActiveAndEnabled)
            {
                result = null;
                return false;
            }

            return TryEvaluate(timestampSeconds, out result);
        }

        /// <summary>
        /// Returns true when this criterion has reached a terminal state.
        /// </summary>
        protected abstract bool TryEvaluate(
            double timestampSeconds,
            out TrialTerminationResult result);
    }
}
