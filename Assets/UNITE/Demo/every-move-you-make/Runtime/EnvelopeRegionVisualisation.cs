using Unite.Core;
using UnityEngine;

namespace Unite.Demo.EveryMoveYouMake
{
    /// <summary>
    /// Envelope visualisation: builds a single filled uncertainty-corridor mesh from the
    /// predicted centre, widened side boundaries, and end-cap extrema. Consumes
    /// <see cref="EnvelopeAssistancePackage"/>.
    /// </summary>
    public sealed class EnvelopeRegionVisualisation : EveryMoveVisualisation
    {
        [SerializeField] private MeshFilter uncertaintyRegion;
        [SerializeField] private MeshCollider terrain;
        [SerializeField, Min(1f)] private float trajectoryRefreshRateHz = 20f;

        private Mesh regionMesh;
        private EnvelopeAssistancePackage pending;
        private double nextRefreshAt;

        private void Awake()
        {
            regionMesh = CreatePredictionMesh(uncertaintyRegion, "UncertaintyRegion");
        }

        public override bool TryPresent(Package package, double timestampSeconds)
        {
            if (!(package.Payload is EnvelopeAssistancePackage envelope))
                return false;
            pending = envelope;
            return true;
        }

        public override void UpdateVisual(double timestampSeconds)
        {
            if (pending == null || timestampSeconds < nextRefreshAt)
                return;
            TrajectoryMeshing.BuildStudyEnvelopeMesh(
                pending.Centre,
                pending.Left,
                pending.Right,
                pending.ExtremaLeft,
                pending.ExtremaRight,
                terrain,
                regionMesh);
            pending = null;
            nextRefreshAt = timestampSeconds + 1d / Mathf.Max(1f, trajectoryRefreshRateHz);
        }
    }
}
