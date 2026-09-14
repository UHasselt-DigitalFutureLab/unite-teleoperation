using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Base type for a module driven by the single TeleroboticsAgent loop.
    /// </summary>
    public abstract class TeleroboticsModule : MonoBehaviour
    {
        /// <summary>
        /// The agent that owns this module's runtime lifecycle.
        /// </summary>
        protected TeleroboticsAgent Agent { get; private set; }

        /// <summary>
        /// Defines deterministic execution order inside an agent step.
        /// </summary>
        internal virtual int ExecutionOrder => 0;

        internal void Initialize(TeleroboticsAgent agent)
        {
            Agent = agent;
            OnInitialize();
        }

        internal abstract void Step(double timestampSeconds);

        /// <summary>
        /// Optional module initialization after the owning agent is available.
        /// </summary>
        protected virtual void OnInitialize()
        {
        }
    }
}
