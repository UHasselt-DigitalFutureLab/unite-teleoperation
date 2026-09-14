using System;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Describes how recorded data should be converted into a reported measure.
    /// </summary>
    [Serializable]
    public sealed class DataMetricDefinition
    {
        [SerializeField]
        private string metricId;

        [SerializeField]
        private string unit;

        [SerializeField]
        [TextArea]
        private string calculationProcedure;

        [SerializeField]
        [TextArea]
        private string thresholds;

        [SerializeField]
        [TextArea]
        private string aggregationRule;

        [SerializeField]
        private string startEvent;

        [SerializeField]
        private string stopEvent;

        public string MetricId => metricId;
        public string Unit => unit;
        public string CalculationProcedure => calculationProcedure;
        public string Thresholds => thresholds;
        public string AggregationRule => aggregationRule;
        public string StartEvent => startEvent;
        public string StopEvent => stopEvent;

        public DataMetricDefinition()
        {
        }

        public DataMetricDefinition(
            string metricId,
            string unit,
            string calculationProcedure,
            string thresholds,
            string aggregationRule,
            string startEvent,
            string stopEvent)
        {
            this.metricId = metricId;
            this.unit = unit;
            this.calculationProcedure = calculationProcedure;
            this.thresholds = thresholds;
            this.aggregationRule = aggregationRule;
            this.startEvent = startEvent;
            this.stopEvent = stopEvent;
        }
    }
}
