using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Coordinates passive trial data capture and stores metric definitions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DataCaptureAndLoggingModule : TeleroboticsModule
    {
        [SerializeField]
        private DataCaptureImplementation[] captureImplementations =
            Array.Empty<DataCaptureImplementation>();

        [SerializeField]
        private DataMetricDefinition[] metricDefinitions =
            Array.Empty<DataMetricDefinition>();

        private readonly List<DataCaptureObservation> observations =
            new List<DataCaptureObservation>();
        private bool subscribedToTrialEnded;

        public event Action<DataCaptureObservation> ObservationRecorded;

        public IReadOnlyList<DataCaptureObservation> Observations => observations;
        public IReadOnlyList<DataMetricDefinition> MetricDefinitions =>
            metricDefinitions;

        internal override int ExecutionOrder => 1300;

        internal override void Step(double timestampSeconds)
        {
            for (int index = 0; index < captureImplementations.Length; index++)
            {
                captureImplementations[index].StepCapture(timestampSeconds);
            }
        }

        internal void RecordObservation(DataCaptureObservation observation)
        {
            if (observation == null)
            {
                Debug.LogError(
                    $"{GetType().Name} received a null data observation.",
                    this);
                return;
            }

            if (string.IsNullOrWhiteSpace(observation.DataId))
            {
                Debug.LogError(
                    $"{GetType().Name} received an observation without a data id.",
                    this);
                return;
            }

            observations.Add(observation);
            ObservationRecorded?.Invoke(observation);
        }

        protected override void OnInitialize()
        {
            observations.Clear();

            if (captureImplementations == null ||
                captureImplementations.Length == 0)
            {
                Debug.LogError(
                    $"{GetType().Name} requires at least one capture implementation.",
                    this);
                enabled = false;
                return;
            }

            for (int index = 0; index < captureImplementations.Length; index++)
            {
                DataCaptureImplementation implementation =
                    captureImplementations[index];

                if (implementation == null)
                {
                    Debug.LogError(
                        $"{GetType().Name} contains an unassigned capture " +
                        $"implementation at index {index}.",
                        this);
                    enabled = false;
                    return;
                }

                implementation.InitializeCapture(this, Agent);
            }

            ValidateMetricDefinitions();

            Agent.TrialEnded += HandleTrialEnded;
            subscribedToTrialEnded = true;
        }

        private void OnDisable()
        {
            if (!subscribedToTrialEnded || Agent == null)
            {
                return;
            }

            Agent.TrialEnded -= HandleTrialEnded;
            subscribedToTrialEnded = false;
        }

        private void HandleTrialEnded(TrialEndedEvent trialEndedEvent)
        {
            for (int index = 0; index < captureImplementations.Length; index++)
            {
                captureImplementations[index].NotifyTrialEnded(trialEndedEvent);
            }
        }

        private void ValidateMetricDefinitions()
        {
            if (metricDefinitions == null)
            {
                metricDefinitions = Array.Empty<DataMetricDefinition>();
                return;
            }

            for (int index = 0; index < metricDefinitions.Length; index++)
            {
                DataMetricDefinition definition = metricDefinitions[index];

                if (definition == null)
                {
                    Debug.LogError(
                        $"{GetType().Name} contains an unassigned metric " +
                        $"definition at index {index}.",
                        this);
                    enabled = false;
                    return;
                }

                if (string.IsNullOrWhiteSpace(definition.MetricId))
                {
                    Debug.LogError(
                        $"{GetType().Name} contains a metric definition " +
                        $"without a metric id at index {index}.",
                        this);
                    enabled = false;
                    return;
                }
            }
        }
    }
}
