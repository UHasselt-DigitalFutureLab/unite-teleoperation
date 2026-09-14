using Unite.Core;
using UnityEngine;

namespace Unite.Demo.EveryMoveYouMake
{
    /// <summary>Operator-side pose reconstructed from received telemetry.</summary>
    public sealed class ReconstructedPose : ReconstructedFeedbackContract
    {
        public Vector3 Position { get; }
        public float Heading { get; }
        public double SampledAt { get; }

        public ReconstructedPose(Vector3 position, float heading, double sampledAt)
        {
            Position = position;
            Heading = heading;
            SampledAt = sampledAt;
        }
    }

    /// <summary>
    /// Value-only prediction input aligned to a reconstructed video exposure.
    /// It contains no texture, transport package, or authoritative state reference.
    /// </summary>
    public sealed class ReconstructedMotionState : ReconstructedFeedbackContract
    {
        public Vector3 Position { get; }
        public float Heading { get; }
        public float LeftVelocity { get; }
        public float RightVelocity { get; }
        public double SampledAt { get; }
        public long ViewSequence { get; }

        public ReconstructedMotionState(
            Vector3 position, float heading, float leftVelocity, float rightVelocity,
            double sampledAt, long viewSequence)
        {
            Position = position;
            Heading = heading;
            LeftVelocity = leftVelocity;
            RightVelocity = rightVelocity;
            SampledAt = sampledAt;
            ViewSequence = viewSequence;
        }
    }

    /// <summary>
    /// Local video representation read by presentation after reconstruction.
    /// The reconstruction module owns the texture. Read LatestViewFrame each
    /// presentation step; do not retain or dispose replaced frame references.
    /// </summary>
    public sealed class ReconstructedVideoFrame
    {
        public Texture Frame { get; }
        public double SampledAt { get; }
        public long Sequence { get; }
        public Matrix4x4 WorldToCameraMatrix { get; }
        public Matrix4x4 ProjectionMatrix { get; }

        public ReconstructedVideoFrame(
            Texture frame, double sampledAt, long sequence,
            Matrix4x4 worldToCameraMatrix, Matrix4x4 projectionMatrix)
        {
            Frame = frame;
            SampledAt = sampledAt;
            Sequence = sequence;
            WorldToCameraMatrix = worldToCameraMatrix;
            ProjectionMatrix = projectionMatrix;
        }
    }
}
