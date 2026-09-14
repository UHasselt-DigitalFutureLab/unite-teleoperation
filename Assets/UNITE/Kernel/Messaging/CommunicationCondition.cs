using System;
using Unite.Core;

namespace Unite.Kernel
{
    /// <summary>
    /// Defines an optional, study-provided payload-agnostic communication
    /// condition for one channel. The Kernel does not prescribe particular
    /// delay, loss, bandwidth, or network-emulation behaviors. A condition
    /// either suppresses the package or supplies one transmission delay. When
    /// no condition is configured on a channel, the Kernel delivers the
    /// package immediately through the channel's existing FIFO queue.
    /// </summary>
    [Serializable]
    public abstract class CommunicationCondition
    {
        /// <summary>
        /// Returns whether one transmission should be scheduled and, when it
        /// should, supplies its delay in seconds. Returning false suppresses
        /// the package. A package cannot produce multiple deliveries through
        /// this contract.
        /// </summary>
        public abstract bool TryGetTransmissionDelay(
            Package package,
            double ingressTimestampSeconds,
            out double delaySeconds);

        /// <summary>
        /// Resets condition-local state when its channel is initialized.
        /// Stateful conditions can override this without changing the
        /// existing condition contract.
        /// </summary>
        public virtual void Reset()
        {
        }
    }
}
