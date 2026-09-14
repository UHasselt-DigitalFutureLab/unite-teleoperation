using System;
using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Optional operator-side attachment point after operator-side state
    /// reconstruction and before feedback presentation. Reconstruction also
    /// feeds presentation directly; assistance need not forward base feedback.
    /// </summary>
    public abstract class DownlinkOperatorSideAssistanceModule
        : TeleroboticsModule, IPackageSource, IPackageConsumer
    {
        private readonly Queue<Package> pendingPackages = new Queue<Package>();

        public event Action<Package> PackageProduced;

        internal sealed override int ExecutionOrder => 900;

        public void ReceivePackage(Package package)
        {
            if (package == null || !(package.Payload is ReconstructedFeedbackContract))
            {
                Debug.LogError(
                    $"{GetType().Name} requires a reconstructed feedback package.", this);
                return;
            }

            pendingPackages.Enqueue(package);
        }

        internal sealed override void Step(double timestampSeconds)
        {
            while (pendingPackages.Count > 0)
            {
                ProcessPackage(pendingPackages.Dequeue(), timestampSeconds);
            }

            UpdateAssistance(timestampSeconds);
        }

        /// <summary>
        /// Publishes one feedback package to the downstream presentation stage.
        /// </summary>
        protected void PublishPackage(Package package)
        {
            if (package == null)
            {
                Debug.LogError($"{GetType().Name} tried to publish a null package.", this);
                return;
            }

            PackageProduced?.Invoke(package);
        }

        /// <summary>
        /// Processes one reconstructed result when needed by the technique.
        /// Output supplements the independent reconstruction-to-presentation
        /// path. Replacing or hiding base feedback requires a study-defined
        /// presentation contract; omitting output does not suppress that path.
        /// </summary>
        protected abstract void ProcessPackage(
            Package package,
            double timestampSeconds);

        /// <summary>
        /// Allows assistance implementations to publish locally computed output
        /// from declared dependencies even when no downlink package was released
        /// during this tick.
        /// </summary>
        protected virtual void UpdateAssistance(double timestampSeconds)
        {
        }
    }
}
