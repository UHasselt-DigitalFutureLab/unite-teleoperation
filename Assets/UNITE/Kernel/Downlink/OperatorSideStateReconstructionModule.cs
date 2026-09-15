using System;
using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Reconstructs operator-side information from packages released by the
    /// downlink and exposes reconstructed information to presentation and assistance.
    /// Implementations update local data representations only; they do not render
    /// Unity objects or compute assistance.
    /// </summary>
    public abstract class OperatorSideStateReconstructionModule
        : TeleroboticsModule, IPackageSource, IPackageConsumer
    {
        private readonly Queue<Package> pendingPackages = new Queue<Package>();

        public event Action<Package> PackageProduced;

        internal sealed override int ExecutionOrder => 850;

        public void ReceivePackage(Package package)
        {
            if (package == null)
            {
                Debug.LogError($"{GetType().Name} received a null package.", this);
                return;
            }

            pendingPackages.Enqueue(package);
        }

        internal sealed override void Step(double timestampSeconds)
        {
            while (pendingPackages.Count > 0)
            {
                Package package = pendingPackages.Dequeue();
                try
                {
                    ReconstructPackage(package, timestampSeconds);
                }
                finally
                {
                    package.Dispose();
                }
            }
        }

        /// <summary>
        /// Publishes an explicitly reconstructed result. The kernel never
        /// forwards received transport packages automatically. Implementations
        /// may publish zero or more results and expose owned local buffers.
        /// </summary>
        protected void PublishReconstructedFeedback(
            ReconstructedFeedbackContract information,
            double sourceTimestampSeconds,
            string streamId)
        {
            if (information == null || string.IsNullOrWhiteSpace(streamId))
            {
                Debug.LogError(
                    $"{GetType().Name} requires reconstructed information and a stream id.",
                    this);
                return;
            }

            PackageProduced?.Invoke(new Package(
                information, sourceTimestampSeconds, streamId));
        }

        /// <summary>
        /// Updates a local representation from one released feedback package.
        /// Publish only usable reconstructed results; unknown or incomplete
        /// inputs need not produce output. The input is disposed after this hook;
        /// retain a payload lease if a local buffer needs its resources longer.
        /// </summary>
        protected abstract void ReconstructPackage(
            Package package,
            double timestampSeconds);

        protected virtual void OnDestroy()
        {
            while (pendingPackages.Count > 0)
                pendingPackages.Dequeue().Dispose();
        }
    }
}
