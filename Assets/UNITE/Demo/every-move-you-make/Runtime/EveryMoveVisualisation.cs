using Unite.Core;
using Unite.Kernel;
using UnityEngine;

namespace Unite.Demo.EveryMoveYouMake
{
    /// <summary>
    /// A standalone operator-side visualisation. Each visualisation renders exactly one kind
    /// of assistance payload and is plugged into <see cref="RoverViewPresentation"/> as a
    /// presentation source. The host offers every released feedback package to each
    /// visualisation's <see cref="TryPresent"/>; the one that recognises the payload renders
    /// it. Adding a visualisation is therefore a new subclass dropped into the scene — no
    /// host code and no shared switch to edit.
    /// </summary>
    public abstract class EveryMoveVisualisation : OperatorPresentationSource
    {
        /// <summary>
        /// Renders (or stores for the next <see cref="UpdateVisual"/>) the package when this
        /// visualisation recognises its payload. Returns true when the package was handled.
        /// </summary>
        public abstract bool TryPresent(Package package, double timestampSeconds);

        /// <summary>
        /// Per-step hook for time-based animation, such as a throttled mesh refresh or
        /// advancing timeline nodes. Called every operator-presentation step.
        /// </summary>
        public virtual void UpdateVisual(double timestampSeconds)
        {
        }

        /// <summary>
        /// Creates a dynamic mesh for the given filter and moves the filter's GameObject onto
        /// the OperatorPrediction layer so the host's prediction camera composites it.
        /// </summary>
        protected static Mesh CreatePredictionMesh(MeshFilter filter, string meshName)
        {
            if (!filter)
                return null;
            int layer = LayerMask.NameToLayer("OperatorPrediction");
            if (layer >= 0)
                filter.gameObject.layer = layer;
            var mesh = new Mesh { name = meshName };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            return mesh;
        }

        protected static void ClearMesh(Mesh mesh)
        {
            if (mesh != null && mesh.vertexCount > 0)
                mesh.Clear(false);
        }
    }
}
