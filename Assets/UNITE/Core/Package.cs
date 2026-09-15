using System;

namespace Unite.Core
{
    /// <summary>
    /// Minimal wrapper for study-defined data travelling through the pipeline.
    /// </summary>
    public sealed class Package : IDisposable
    {
        private int payloadOwners = 1;
        private bool disposed;

        public object Payload { get; set; }
        public double SourceTimestampSeconds { get; set; }
        public string StreamId { get; set; }

        public Package(object payload, double sourceTimestampSeconds)
            : this(payload, sourceTimestampSeconds, null)
        {
        }

        public Package(
            object payload,
            double sourceTimestampSeconds,
            string streamId)
        {
            Payload = payload;
            SourceTimestampSeconds = sourceTimestampSeconds;
            StreamId = streamId;
        }

        /// <summary>
        /// Keeps a resource-owning payload alive beyond its receiver's scope.
        /// Dispose the returned lease when the retained reference is replaced.
        /// Payloads must not be replaced or disposed directly after publication.
        /// Ownership operations run on the single kernel thread.
        /// </summary>
        public IDisposable RetainPayload()
        {
            if (payloadOwners == 0)
                throw new ObjectDisposedException(nameof(Package));
            payloadOwners++;
            return new PayloadLease(this);
        }

        /// <summary>
        /// Releases the original ownership once. The payload's IDisposable hook
        /// runs only after this ownership and every retained lease are released.
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ReleasePayload();
        }

        private void ReleasePayload()
        {
            if (--payloadOwners == 0 && Payload is IDisposable resource)
                resource.Dispose();
        }

        private sealed class PayloadLease : IDisposable
        {
            private Package owner;

            internal PayloadLease(Package owner) { this.owner = owner; }

            public void Dispose()
            {
                Package previous = owner;
                owner = null;
                previous?.ReleasePayload();
            }
        }
    }
}
