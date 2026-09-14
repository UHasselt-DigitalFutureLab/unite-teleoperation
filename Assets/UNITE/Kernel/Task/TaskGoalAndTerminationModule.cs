using System;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Evaluates task criteria and ends the active trial on the first terminal
    /// result.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TaskGoalAndTerminationModule : TeleroboticsModule
    {
        [SerializeField]
        private TaskTerminationCriterion[] criteria =
            Array.Empty<TaskTerminationCriterion>();

        private bool hasTerminated;

        public TrialEndedEvent LastTrialEndedEvent { get; private set; }

        internal override int ExecutionOrder => 1200;

        internal override void Step(double timestampSeconds)
        {
            if (hasTerminated)
            {
                return;
            }

            for (int index = 0; index < criteria.Length; index++)
            {
                TaskTerminationCriterion criterion = criteria[index];

                if (!criterion.TryEvaluateCriterion(
                        timestampSeconds,
                        out TrialTerminationResult result))
                {
                    continue;
                }

                if (result == null)
                {
                    Debug.LogError(
                        $"{criterion.GetType().Name} reported termination but " +
                        "returned a null result.",
                        criterion);
                    continue;
                }

                AcceptTerminalResult(criterion, result, timestampSeconds);
                return;
            }
        }

        protected override void OnInitialize()
        {
            hasTerminated = false;
            LastTrialEndedEvent = null;

            if (criteria == null || criteria.Length == 0)
            {
                Debug.LogError(
                    $"{GetType().Name} requires at least one termination criterion.",
                    this);
                enabled = false;
                return;
            }

            for (int index = 0; index < criteria.Length; index++)
            {
                if (criteria[index] != null)
                {
                    continue;
                }

                Debug.LogError(
                    $"{GetType().Name} contains an unassigned criterion at index {index}.",
                    this);
                enabled = false;
                return;
            }
        }

        private void AcceptTerminalResult(
            TaskTerminationCriterion criterion,
            TrialTerminationResult result,
            double timestampSeconds)
        {
            hasTerminated = true;
            LastTrialEndedEvent = new TrialEndedEvent(
                result.Outcome,
                result.Reason,
                criterion.GetType().Name,
                timestampSeconds);

            Agent.EndTrial(LastTrialEndedEvent);
        }
    }
}
