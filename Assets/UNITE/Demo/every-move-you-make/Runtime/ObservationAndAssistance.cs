using System.Collections.Generic;
using Unite.Core;
using Unite.Kernel;
using UnityEngine;

namespace Unite.Demo.EveryMoveYouMake
{
    public sealed partial class PoseSource
        : VehicleStateObservationSource<TurtleBotState, PosePackage>
    {
        protected override bool TryCapture(
            TurtleBotState state,
            double timestamp,
            out PosePackage observation)
        {
            observation = new PosePackage(state, timestamp);
            return true;
        }
    }

    public sealed partial class RobotStateSource
        : VehicleStateObservationSource<TurtleBotState, RobotStatePackage>
    {
        protected override bool TryCapture(
            TurtleBotState state,
            double timestamp,
            out RobotStatePackage observation)
        {
            observation = new RobotStatePackage(state, timestamp);
            return true;
        }
    }

    public sealed partial class RobotViewSource
        : VehicleStateObservationSource<TurtleBotState, ViewFramePackage>
    {
        protected override bool TryCapture(
            TurtleBotState state,
            double timestamp,
            out ViewFramePackage observation)
        {
            TurtleBot3WafflePiEnhanced turtleBot = Vehicle as TurtleBot3WafflePiEnhanced;
            CameraFrameSnapshot cameraFrame = state != null &&
                state.Snapshot.Sensors != null &&
                state.Snapshot.Sensors.Camera != null
                ? state.Snapshot.Sensors.Camera.CapturedFrame
                : null;
            if (!turtleBot || !turtleBot.TryCopyCameraFrame(
                    cameraFrame,
                    out RenderTexture frame,
                    out TurtleBotState stateAtCapture))
            {
                observation = null;
                return false;
            }
            observation = new ViewFramePackage(
                frame,
                cameraFrame.SampledAt,
                cameraFrame.Sequence,
                cameraFrame.WorldToCameraMatrix,
                cameraFrame.ProjectionMatrix,
                stateAtCapture,
                turtleBot.RecycleCameraFrame);
            return true;
        }
    }

    /// <summary>
    /// Shared base for the Every Move You Make downlink assistance modules. The default
    /// behaviour is the Baseline condition: publish no operator overlay.
    /// Presentation receives reconstructed feedback independently. Each visualisation overrides the
    /// hooks it needs, so a new condition is added by writing a new subclass with no shared
    /// switch to edit. The four serialized dependencies are wired by the scene builder on
    /// every assistance type, whether or not a given subclass uses them.
    /// </summary>
    public abstract class EveryMoveAssistanceBase : DownlinkOperatorSideAssistanceModule
    {
        [SerializeField] protected UplinkCommunicationModule uplink;
        [SerializeField] protected ArrowToWheelVelocityMapping commandMapping;
        [SerializeField] protected EveryMoveVehicleConfiguration vehicleConfiguration;
        [SerializeField] protected EveryMoveCommunicationConfiguration communicationConfiguration;

        // Baseline: no assistance output; reconstruction supplies the base video.
        protected override void ProcessPackage(Package package, double timestamp)
        {
        }
    }

    /// <summary>
    /// Network condition: republishes each mapped command as a semantic keypress-timeline
    /// event. It needs no reconstructed input and leaves ProcessPackage empty.
    /// </summary>
    public abstract class CommandTimelineAssistanceBase : EveryMoveAssistanceBase
    {
        private readonly Queue<WheelVelocityCommand> pendingTimelineCommands =
            new Queue<WheelVelocityCommand>();
        private readonly List<UplinkPendingPackageSnapshot> pending =
            new List<UplinkPendingPackageSnapshot>();

        protected override void OnInitialize()
        {
            if (!commandMapping)
                commandMapping = GetComponent<ArrowToWheelVelocityMapping>();
            if (!commandMapping)
            {
                Debug.LogError(
                    $"{GetType().Name} requires the selected command mapping as its " +
                    "declared command-history dependency.",
                    this);
                enabled = false;
                return;
            }
            commandMapping.OutputProduced += ReceiveMappedCommand;
        }

        private void OnDestroy()
        {
            if (commandMapping)
                commandMapping.OutputProduced -= ReceiveMappedCommand;
        }

        private void ReceiveMappedCommand(WheelVelocityCommand command)
        {
            if (command != null)
                pendingTimelineCommands.Enqueue(command);
        }

        protected override void UpdateAssistance(double timestampSeconds)
        {
            while (pendingTimelineCommands.Count > 0)
            {
                WheelVelocityCommand command = pendingTimelineCommands.Dequeue();
                ArrowInputPackage input = command.Input;
                if (input == null)
                    continue;

                var directions = new List<NetworkTimelineDirection>(2);
                if (input.Up)
                    directions.Add(NetworkTimelineDirection.Up);
                else if (input.Down)
                    directions.Add(NetworkTimelineDirection.Down);
                if (input.Left)
                    directions.Add(NetworkTimelineDirection.Left);
                else if (input.Right)
                    directions.Add(NetworkTimelineDirection.Right);
                if (directions.Count == 0)
                    continue;

                float delaySeconds = ResolveRoundTripDuration(command);
                PublishPackage(new Package(
                    new NetworkTimelinePackage(
                        directions.ToArray(),
                        command.SourceTimestampSeconds,
                        delaySeconds),
                    command.SourceTimestampSeconds,
                    EveryMoveStreams.Assistance));
            }
        }

        private float ResolveRoundTripDuration(WheelVelocityCommand command)
        {
            float uplinkDurationSeconds = communicationConfiguration
                ? Mathf.Max(0f, communicationConfiguration.uplinkDelayMilliseconds) / 1000f
                : 2.56f;

            pending.Clear();
            if (uplink)
                uplink.CopyPendingTransmissions(EveryMoveStreams.Command, pending);
            for (int index = 0; index < pending.Count; index++)
            {
                UplinkPendingPackageSnapshot queued = pending[index];
                if (ReferenceEquals(queued.Package?.Payload, command))
                {
                    uplinkDurationSeconds = Mathf.Max(
                        0f,
                        (float)(queued.ReleaseTimestampSeconds -
                                command.SourceTimestampSeconds));
                    break;
                }
            }

            float downlinkDurationSeconds = communicationConfiguration
                ? Mathf.Max(0f, communicationConfiguration.downlinkDelayMilliseconds) / 1000f
                : 0f;
            return Mathf.Max(
                0.01f,
                uplinkDurationSeconds + downlinkDurationSeconds);
        }
    }

    /// <summary>
    /// Shared machinery for the trajectory-prediction conditions (Path, Envelope): it
    /// tracks the released-command history, replays it from the reconstructed video frame's
    /// captured state, and integrates the centre trace. Each subclass builds its own
    /// overlay payload from the integrated prediction via <see cref="BuildOverlayPayload"/>.
    /// </summary>
    public abstract class TrajectoryAssistanceBase : EveryMoveAssistanceBase
    {
        private readonly List<UplinkPendingPackageSnapshot> pending =
            new List<UplinkPendingPackageSnapshot>();
        private readonly List<TimedPredictionCommand> releasedCommandHistory =
            new List<TimedPredictionCommand>();
        private readonly List<TimedPredictionCommand> predictionCommands =
            new List<TimedPredictionCommand>();
        private readonly Queue<WheelVelocityCommand> pendingReleasedCommands =
            new Queue<WheelVelocityCommand>();
        private long commandSequence;

        private readonly struct TimedPredictionCommand
        {
            public TimedPredictionCommand(
                WheelVelocityCommand command,
                double releaseTimestampSeconds,
                long sequence)
            {
                Command = command;
                ReleaseTimestampSeconds = releaseTimestampSeconds;
                Sequence = sequence;
            }

            public WheelVelocityCommand Command { get; }
            public double ReleaseTimestampSeconds { get; }
            public long Sequence { get; }
        }

        protected override void OnInitialize()
        {
            if (!uplink)
            {
                Debug.LogError(
                    $"{GetType().Name} requires Uplink Communication as its " +
                    "declared release-history dependency.",
                    this);
                enabled = false;
                return;
            }
            uplink.PackageProduced += ReceiveUplinkRelease;
        }

        private void OnDestroy()
        {
            if (uplink)
                uplink.PackageProduced -= ReceiveUplinkRelease;
        }

        private void ReceiveUplinkRelease(Package package)
        {
            // Buffer the released command instead of reading the ambient Agent clock
            // from inside an event handler. It is stamped with the authoritative tick
            // timestamp when drained below, which runs in the same fixed step as the
            // release (uplink and this module step in one FixedUpdate), so the recorded
            // release time is identical to the previous behaviour.
            if (package?.Payload is WheelVelocityCommand command)
                pendingReleasedCommands.Enqueue(command);
        }

        private void DrainReleasedCommands(double timestampSeconds)
        {
            while (pendingReleasedCommands.Count > 0)
                releasedCommandHistory.Add(new TimedPredictionCommand(
                    pendingReleasedCommands.Dequeue(),
                    timestampSeconds,
                    commandSequence++));
        }

        protected override void UpdateAssistance(double timestampSeconds)
        {
            // Safety-net drain for ticks in which ProcessPackage does not run; keeps
            // release timestamps on the tick the command was released.
            DrainReleasedCommands(timestampSeconds);
        }

        protected override void ProcessPackage(Package package, double timestamp)
        {
            // Fold in any commands released this tick before the prediction reads the
            // release history below.
            DrainReleasedCommands(timestamp);
            if (!(package.Payload is ReconstructedMotionState state))
                return;

            pending.Clear();
            if (uplink) uplink.CopyPendingTransmissions(EveryMoveStreams.Command, pending);
            BuildPredictionCommandSequence(state.SampledAt);

            PredictedPose[] centre = Integrate(state, 1f, 1f, 0f);
            object overlay = BuildOverlayPayload(state, centre);
            if (overlay != null)
                PublishPackage(new Package(
                    overlay, package.SourceTimestampSeconds, EveryMoveStreams.Assistance));
        }

        /// <summary>
        /// Builds this condition's overlay payload from the integrated centre trace and the
        /// prediction command sequence (available via <see cref="Integrate"/> and
        /// <see cref="Offset"/>). Return null to publish no overlay.
        /// </summary>
        protected abstract object BuildOverlayPayload(ReconstructedMotionState state, PredictedPose[] centre);

        private void BuildPredictionCommandSequence(double stateSampledAt)
        {
            predictionCommands.Clear();
            int obsoleteCount = 0;
            for (int index = 0; index < releasedCommandHistory.Count; index++)
            {
                TimedPredictionCommand released = releasedCommandHistory[index];
                if (released.ReleaseTimestampSeconds <= stateSampledAt + 0.000001d)
                {
                    obsoleteCount = index + 1;
                    continue;
                }
                predictionCommands.Add(released);
            }
            if (obsoleteCount > 0)
                releasedCommandHistory.RemoveRange(0, obsoleteCount);

            for (int index = 0; index < pending.Count; index++)
            {
                UplinkPendingPackageSnapshot queued = pending[index];
                if (queued.Package?.Payload is WheelVelocityCommand command)
                {
                    predictionCommands.Add(new TimedPredictionCommand(
                        command,
                        queued.ReleaseTimestampSeconds,
                        commandSequence++));
                }
            }

            predictionCommands.Sort((left, right) =>
            {
                int byTime = left.ReleaseTimestampSeconds.CompareTo(
                    right.ReleaseTimestampSeconds);
                return byTime != 0 ? byTime : left.Sequence.CompareTo(right.Sequence);
            });
        }

        protected PredictedPose[] Integrate(
            ReconstructedMotionState start,
            float leftMultiplier,
            float rightMultiplier,
            float lateralStartOffset)
        {
            var output = new PredictedPose[predictionCommands.Count];
            float theta = start.Heading;
            // Traces emanate from the raw kinematic origin (the point the differential-drive
            // math integrates around); the apex effect comes from offsetting the visible mesh,
            // not the overlays. See TurtleBot3WafflePiEnhanced.UpdateVehicleState.
            float x = start.Position.x + Mathf.Sin(theta) * lateralStartOffset;
            float z = start.Position.z + Mathf.Cos(theta) * lateralStartOffset;
            float vl = start.LeftVelocity, vr = start.RightVelocity;
            int outputIndex = 0;
            for (int i = 0; i < predictionCommands.Count; i++)
            {
                WheelVelocityCommand command = predictionCommands[i].Command;
                float dt = command.DeltaTime;
                vl = Mathf.MoveTowards(vl, command.RequestedLeft, vehicleConfiguration.maxLinearAcceleration * dt) * leftMultiplier;
                vr = Mathf.MoveTowards(vr, command.RequestedRight, vehicleConfiguration.maxLinearAcceleration * dt) * rightMultiplier;
                float diff = Mathf.Clamp(vr - vl, -vehicleConfiguration.maxWheelVelocityDifference, vehicleConfiguration.maxWheelVelocityDifference);
                float v = (vl + vr) * .5f; x += v * Mathf.Cos(theta) * dt; z -= v * Mathf.Sin(theta) * dt;
                theta += diff / vehicleConfiguration.wheelbase * dt;
                output[outputIndex++] = new PredictedPose
                {
                    position = new Vector3(x, start.Position.y, z),
                    heading = theta,
                    leftVelocity = vl,
                    rightVelocity = vr
                };
            }
            return output;
        }

        protected static PredictedPose[] Offset(PredictedPose[] source, float offset)
        {
            var result = (PredictedPose[])source.Clone();
            for (int i = 0; i < result.Length; i++)
                result[i].position += new Vector3(Mathf.Sin(result[i].heading), 0f, Mathf.Cos(result[i].heading)) * offset;
            return result;
        }
    }

    // Baseline: no assistance output; presentation still receives reconstructed video.
    [EveryMoveCondition("condition-baseline")]
    public sealed partial class NoAssistance : EveryMoveAssistanceBase { }

    // Network: keypress timeline.
    [EveryMoveCondition("condition-network")]
    public sealed partial class CommandTimelineAssistance : CommandTimelineAssistanceBase { }

    // Path: centre trace plus the two wheel-offset traces.
    [EveryMoveCondition("condition-path")]
    public sealed partial class IdealTrajectoryAssistance : TrajectoryAssistanceBase
    {
        protected override object BuildOverlayPayload(ReconstructedMotionState state, PredictedPose[] centre)
        {
            PredictedPose[] left = Offset(centre, -vehicleConfiguration.wheelbase * .5f);
            PredictedPose[] right = Offset(centre, vehicleConfiguration.wheelbase * .5f);
            return new PathAssistancePackage(centre, left, right);
        }
    }

    // Envelope: uncertainty-widened side boundaries plus the two centre-started extrema.
    [EveryMoveCondition("condition-envelope")]
    public sealed partial class WorstCaseEnvelopeAssistance : TrajectoryAssistanceBase
    {
        protected override object BuildOverlayPayload(ReconstructedMotionState state, PredictedPose[] centre)
        {
            float uncertainty = Mathf.Clamp01(vehicleConfiguration.wheelSlipFactor * vehicleConfiguration.terrainRoughness +
                vehicleConfiguration.motorResponseVariation + vehicleConfiguration.wheelRadiusVariation +
                vehicleConfiguration.encoderNoise + vehicleConfiguration.vibrationIntensity);
            float wheelOffset = vehicleConfiguration.wheelbase * 0.5f;

            // The study envelope is not a strip between two centre-started paths. Its
            // side boundaries begin at the left and right wheel offsets. Two additional,
            // centre-started trajectories control the curved end cap.
            PredictedPose[] left = Integrate(state, 1f - uncertainty, 1f, -wheelOffset);
            PredictedPose[] right = Integrate(state, 1f, 1f - uncertainty, wheelOffset);
            PredictedPose[] extremaLeft = Integrate(state, 1f - uncertainty * 0.5f, 1f, 0f);
            PredictedPose[] extremaRight = Integrate(state, 1f, 1f - uncertainty * 0.5f, 0f);
            return new EnvelopeAssistancePackage(centre, left, right, extremaLeft, extremaRight);
        }
    }
}
