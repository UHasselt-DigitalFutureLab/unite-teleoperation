using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Combines reconstructed information and optional assistance output using
    /// configured operator-side sources. Study code defines composition and
    /// any explicit replacement or suppression of base feedback.
    /// </summary>
    public abstract class OperatorPresentationModule
        : TeleroboticsModule, IPackageConsumer
    {
        [SerializeField]
        private OperatorPresentationSource[] sources =
            System.Array.Empty<OperatorPresentationSource>();

        private readonly Dictionary<string, OperatorPresentationSource>
            sourcesById =
                new Dictionary<string, OperatorPresentationSource>(
                    System.StringComparer.Ordinal);
        private readonly Queue<Package> pendingPackages = new Queue<Package>();

        internal sealed override int ExecutionOrder => 1000;

        public void ReceivePackage(Package package)
        {
            if (package == null)
            {
                Debug.LogError($"{GetType().Name} received a null package.", this);
                return;
            }

            pendingPackages.Enqueue(package);
        }

        protected bool TryGetSource(
            string sourceId,
            out OperatorPresentationSource source)
        {
            string normalized = string.IsNullOrWhiteSpace(sourceId)
                ? string.Empty
                : sourceId.Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                source = null;
                return false;
            }

            return sourcesById.TryGetValue(normalized, out source);
        }

        protected IReadOnlyCollection<OperatorPresentationSource> Sources =>
            sourcesById.Values;

        internal sealed override void Step(double timestampSeconds)
        {
            while (pendingPackages.Count > 0)
            {
                PresentPackage(pendingPackages.Dequeue(), timestampSeconds);
            }

            UpdatePresentation(timestampSeconds);
        }

        protected override void OnInitialize()
        {
            sourcesById.Clear();

            if (sources == null)
            {
                sources = System.Array.Empty<OperatorPresentationSource>();
            }

            for (int index = 0; index < sources.Length; index++)
            {
                OperatorPresentationSource source = sources[index];

                if (source == null)
                {
                    Debug.LogError(
                        $"{GetType().Name} contains an unassigned presentation " +
                        $"source at index {index}.",
                        this);
                    enabled = false;
                    return;
                }

                if (string.IsNullOrWhiteSpace(source.SourceId))
                {
                    Debug.LogError(
                        $"{source.GetType().Name} requires a presentation source id.",
                        source);
                    enabled = false;
                    return;
                }

                if (sourcesById.ContainsKey(source.SourceId))
                {
                    Debug.LogError(
                        $"{GetType().Name} contains duplicate presentation " +
                        $"source id '{source.SourceId}'.",
                        this);
                    enabled = false;
                    return;
                }

                sourcesById.Add(source.SourceId, source);
            }

            InitializePresentationSources();
        }

        /// <summary>
        /// Optional setup after the source registry has been validated.
        /// </summary>
        protected virtual void InitializePresentationSources()
        {
        }

        /// <summary>
        /// Applies reconstructed information or assistance output to operator-side
        /// presentation targets without changing its semantic content.
        /// </summary>
        protected abstract void PresentPackage(
            Package package,
            double timestampSeconds);

        /// <summary>
        /// Selects and arranges local presentation sources for the current
        /// operator view.
        /// </summary>
        protected virtual void UpdatePresentation(double timestampSeconds)
        {
        }
    }
}
