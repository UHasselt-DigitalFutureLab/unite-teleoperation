using Unite.Core;
using Unite.Kernel;
using UnityEngine;

namespace Unite.Demo.EveryMoveYouMake
{
    /// <summary>
    /// Module 5.9 implementation for the study. It owns the latest released local
    /// pose and video-frame representations and never accesses authoritative state.
    /// </summary>
    public sealed class EveryMoveStateReconstruction
        : OperatorSideStateReconstructionModule
    {
        private ViewFramePackage ownedViewFrame;

        public ReconstructedPose LatestPose { get; private set; }
        public ReconstructedVideoFrame LatestViewFrame { get; private set; }
        public ReconstructedMotionState LatestViewState { get; private set; }
        public Texture CurrentViewFrame { get; private set; }
        public double CurrentViewSampledAt { get; private set; }
        public long CurrentViewSequence { get; private set; } = -1;

        protected override void ReconstructPackage(Package package, double timestampSeconds)
        {
            if (package.Payload is PosePackage pose)
            {
                LatestPose = new ReconstructedPose(pose.Position, pose.Heading, pose.SampledAt);
                PublishReconstructedFeedback(
                    LatestPose, package.SourceTimestampSeconds, EveryMoveStreams.Pose);
                return;
            }

            if (!(package.Payload is ViewFramePackage view))
                return;

            if (!view.Frame)
            {
                view.Release();
                return;
            }

            ViewFramePackage previous = ownedViewFrame;
            ownedViewFrame = view;
            LatestViewFrame = new ReconstructedVideoFrame(
                view.Frame, view.SampledAt, view.Sequence,
                view.WorldToCameraMatrix, view.ProjectionMatrix);
            CurrentViewFrame = view.Frame;
            CurrentViewSampledAt = view.SampledAt;
            CurrentViewSequence = view.Sequence;

            // Copy just the received motion values needed by prediction. Do not
            // expose the transport package or its nested remote sensor handles.
            TurtleBotState captured = view.StateAtCapture;
            LatestViewState = captured == null ? null : new ReconstructedMotionState(
                captured.Position, captured.Heading,
                captured.LeftVelocity, captured.RightVelocity,
                view.SampledAt, view.Sequence);
            if (LatestViewState != null)
                PublishReconstructedFeedback(
                    LatestViewState, package.SourceTimestampSeconds,
                    EveryMoveStreams.ReconstructedViewState);

            if (previous != null && previous != view)
                previous.Release();
        }

        private void OnDestroy()
        {
            ownedViewFrame?.Release();
            ownedViewFrame = null;
            CurrentViewFrame = null;
            LatestViewFrame = null;
            LatestViewState = null;
        }
    }
}
