using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Demo.EveryMoveYouMake
{
    /// <summary>
    /// Path visualisation: draws the predicted centre trace plus the two wheel-offset traces
    /// as terrain-projected ribbon meshes. Consumes <see cref="PathAssistancePackage"/>.
    /// </summary>
    public sealed class PathRibbonVisualisation : EveryMoveVisualisation
    {
        [SerializeField] private MeshFilter centreTrace;
        [SerializeField] private MeshFilter leftTrace;
        [SerializeField] private MeshFilter rightTrace;
        [SerializeField] private MeshCollider terrain;
        [SerializeField, Min(1f)] private float trajectoryRefreshRateHz = 20f;

        private readonly List<Vector3> projectionBuffer = new List<Vector3>();
        private Mesh centreMesh;
        private Mesh leftMesh;
        private Mesh rightMesh;
        private PathAssistancePackage pending;
        private double nextRefreshAt;

        private void Awake()
        {
            centreMesh = CreatePredictionMesh(centreTrace, "CentreTrace");
            leftMesh = CreatePredictionMesh(leftTrace, "LeftTrace");
            rightMesh = CreatePredictionMesh(rightTrace, "RightTrace");
        }

        public override bool TryPresent(Package package, double timestampSeconds)
        {
            if (!(package.Payload is PathAssistancePackage path))
                return false;
            // Keep only the newest prediction; mesh projection is throttled below so the
            // expensive MeshCollider raycasts cannot stall the fixed simulation step.
            pending = path;
            return true;
        }

        public override void UpdateVisual(double timestampSeconds)
        {
            if (pending == null || timestampSeconds < nextRefreshAt)
                return;
            DrawRibbon(centreMesh, pending.Centre);
            DrawRibbon(leftMesh, pending.Left);
            DrawRibbon(rightMesh, pending.Right);
            pending = null;
            nextRefreshAt = timestampSeconds + 1d / Mathf.Max(1f, trajectoryRefreshRateHz);
        }

        private void DrawRibbon(Mesh mesh, PredictedPose[] poses)
        {
            if (mesh == null) return;
            TrajectoryMeshing.ProjectPoses(poses, terrain, projectionBuffer);
            TrajectoryMeshing.BuildRibbonMesh(projectionBuffer, 0.01f, 5, mesh);
        }
    }
}
