using System;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    public abstract class CommandMappingAndEncodingModule : TeleroboticsModule
        , IPackageSource
    {
        [SerializeField]
        private string outputStreamId = "vehicle-control";

        public event Action<Package> PackageProduced;

        protected string OutputStreamId =>
            string.IsNullOrWhiteSpace(outputStreamId)
                ? string.Empty
                : outputStreamId.Trim();

        protected void PublishPackage(Package package)
        {
            PackageProduced?.Invoke(package);
        }
    }

    /// <summary>
    /// Converts one declared Input Provider contract into one declared,
    /// device-independent command contract.
    /// </summary>
    public abstract class CommandMappingAndEncoding<TInputContract, TCommandContract>
        : CommandMappingAndEncodingModule
        where TInputContract : InputProviderOutputContract
        where TCommandContract : EncodedCommandContract
    {
        private InputProvider<TInputContract> inputProvider;
        private TInputContract pendingInput;
        private bool hasPendingInput;

        public event Action<TCommandContract> OutputProduced;

        public TCommandContract LatestOutput { get; private set; }
        public bool HasOutput { get; private set; }

        internal sealed override int ExecutionOrder => 200;

        protected override void OnInitialize()
        {
            inputProvider =
                Agent.SelectedInputProvider as InputProvider<TInputContract>;

            if (inputProvider == null)
            {
                Debug.LogError(
                    $"{GetType().Name} requires the agent's selected Input Provider " +
                    $"to produce {typeof(TInputContract).Name}.",
                    this);
                enabled = false;
                return;
            }

            inputProvider.OutputProduced += ReceiveInput;
        }

        private void OnDestroy()
        {
            if (inputProvider != null)
            {
                inputProvider.OutputProduced -= ReceiveInput;
            }
        }

        private void ReceiveInput(TInputContract input)
        {
            pendingInput = input;
            hasPendingInput = true;
        }

        internal sealed override void Step(double timestampSeconds)
        {
            if (!hasPendingInput)
            {
                return;
            }

            TInputContract input = pendingInput;
            hasPendingInput = false;

            if (!TryMapAndEncode(input, timestampSeconds, out TCommandContract output))
            {
                return;
            }

            if (output == null)
            {
                Debug.LogError(
                    $"{GetType().Name} reported an encoded command but returned null.",
                    this);
                return;
            }

            if (output.SourceTimestampSeconds != input.TimestampSeconds ||
                output.TimestampSeconds != timestampSeconds)
            {
                Debug.LogError(
                    $"{GetType().Name} returned a command with timestamps that do not " +
                    "match the source input and Kernel mapping step.",
                    this);
                return;
            }

            LatestOutput = output;
            HasOutput = true;
            PublishPackage(new Package(
                output,
                output.SourceTimestampSeconds,
                OutputStreamId));
            OutputProduced?.Invoke(output);
        }

        protected abstract bool TryMapAndEncode(
            TInputContract input,
            double timestampSeconds,
            out TCommandContract output);
    }
}
