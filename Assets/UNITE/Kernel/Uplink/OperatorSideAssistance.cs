using System;
using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Optional attachment point for study-defined operator-side assistance.
    /// </summary>
    public abstract class OperatorSideAssistanceModule
        : TeleroboticsModule, IPackageSource, IPackageConsumer
    {
        private readonly Queue<Package> pendingPackages = new Queue<Package>();

        public event Action<Package> PackageProduced;

        internal sealed override int ExecutionOrder => 300;

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
                Package input = pendingPackages.Dequeue();

                if (!TryProcessPackage(
                        input,
                        timestampSeconds,
                        out Package output))
                {
                    continue;
                }

                if (output == null)
                {
                    Debug.LogError(
                        $"{GetType().Name} reported an assisted package but " +
                        "returned null.",
                        this);
                    continue;
                }

                PackageProduced?.Invoke(output);
            }
        }

        /// <summary>
        /// Processes one mapped-command package and returns either no package or
        /// exactly one package.
        /// </summary>
        protected abstract bool TryProcessPackage(
            Package package,
            double timestampSeconds,
            out Package output);
    }
}
