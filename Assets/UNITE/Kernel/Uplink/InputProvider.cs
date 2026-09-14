using System;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    public abstract class InputProviderModule : TeleroboticsModule
    {
    }

    /// <summary>
    /// Acquires device-specific operator signals and publishes typed snapshots.
    /// </summary>
    /// <typeparam name="TOutputContract">
    /// The exact output contract published by this provider implementation.
    /// </typeparam>
    public abstract class InputProvider<TOutputContract> : InputProviderModule
        where TOutputContract : InputProviderOutputContract
    {
        /// <summary>
        /// Raised after a new input snapshot has been captured successfully.
        /// </summary>
        public event Action<TOutputContract> OutputProduced;

        /// <summary>
        /// The most recently published output, or <c>null</c> before the first output.
        /// </summary>
        public TOutputContract LatestOutput { get; private set; }

        /// <summary>
        /// Indicates whether this provider has published at least one output.
        /// </summary>
        public bool HasOutput { get; private set; }

        internal sealed override int ExecutionOrder => 100;

        internal sealed override void Step(double timestampSeconds)
        {
            if (!TryCaptureInput(timestampSeconds, out TOutputContract output))
            {
                return;
            }

            if (output == null)
            {
                Debug.LogError(
                    $"{GetType().Name} reported a captured input but returned a null " +
                    $"{typeof(TOutputContract).Name} contract.",
                    this);
                return;
            }

            if (output.TimestampSeconds != timestampSeconds)
            {
                Debug.LogError(
                    $"{GetType().Name} returned a {typeof(TOutputContract).Name} " +
                    "contract with a timestamp different from the Kernel-provided " +
                    "capture timestamp.",
                    this);
                return;
            }

            LatestOutput = output;
            HasOutput = true;
            OnOutputProduced(output);
            OutputProduced?.Invoke(output);
        }

        /// <summary>
        /// Optional implementation hook invoked immediately before publication.
        /// </summary>
        protected virtual void OnOutputProduced(TOutputContract output)
        {
        }

        /// <summary>
        /// Reads the concrete input device and creates its declared output contract.
        /// </summary>
        /// <param name="timestampSeconds">
        /// The Kernel-provided monotonic capture time. Pass this value to the output
        /// contract constructor unchanged.
        /// </param>
        /// <param name="output">
        /// The captured contract when returning <c>true</c>; otherwise <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> when an output should be published; otherwise <c>false</c>.
        /// </returns>
        protected abstract bool TryCaptureInput(
            double timestampSeconds,
            out TOutputContract output);

    }
}
