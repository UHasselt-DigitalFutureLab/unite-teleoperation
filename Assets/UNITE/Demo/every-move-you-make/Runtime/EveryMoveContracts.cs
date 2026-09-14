using System;
using System.Collections.Generic;
using Unite.Core;
using Unite.Kernel;
using UnityEngine;

namespace Unite.Demo.EveryMoveYouMake
{
    /// <summary>
    /// Declares the generated condition-scene name for an assistance module. The scene
    /// builder discovers every <c>EveryMoveAssistanceBase</c> subclass and generates one
    /// scene per class, so a new visualisation is added by writing a decorated subclass —
    /// no shared list to edit. Subclasses without this attribute fall back to their type name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EveryMoveConditionAttribute : Attribute
    {
        public string SceneName { get; }
        public EveryMoveConditionAttribute(string sceneName) { SceneName = sceneName; }
    }

    public static class EveryMoveStreams
    {
        public const string Command = "wheel-velocity-command";
        public const string Pose = "robot-pose";
        public const string State = "robot-state";
        public const string View = "robot-view";
        public const string ReconstructedViewState = "reconstructed-view-state";
        public const string Assistance = "operator-assistance";
        public const string TrialEnded = "trial-ended";
    }

    public sealed class ArrowInputPackage : InputProviderOutputContract
    {
        public bool Up { get; }
        public bool Down { get; }
        public bool Left { get; }
        public bool Right { get; }
        public float DeltaTime { get; }

        // The contract carries only the discrete per-tick key state and dt. Press
        // duration is not encoded here: it is reconstructed downstream by integrating
        // the sequence of these snapshots as they flow through the delayed uplink queue
        // (vehicle model and predictors), matching the reference study.
        public ArrowInputPackage(double timestamp, float dt, bool up, bool down, bool left,
            bool right)
            : base(timestamp)
        {
            DeltaTime = dt; Up = up; Down = down; Left = left; Right = right;
        }
    }

    public sealed class WheelVelocityCommand : EncodedCommandContract
    {
        public float RequestedLeft { get; }
        public float RequestedRight { get; }
        public float DeltaTime { get; }
        public ArrowInputPackage Input { get; }

        public WheelVelocityCommand(ArrowInputPackage input, double encodedAt, float left, float right)
            : base(input.TimestampSeconds, encodedAt)
        {
            Input = input; DeltaTime = input.DeltaTime; RequestedLeft = left; RequestedRight = right;
        }
    }

    public sealed class TurtleBotSnapshot
    {
        public VehicleKinematics Kinematics { get; }
        public VehicleActuation Actuation { get; }
        public VehicleSensors Sensors { get; }

        public TurtleBotSnapshot(
            VehicleKinematics kinematics,
            VehicleActuation actuation,
            VehicleSensors sensors)
        {
            Kinematics = kinematics ?? throw new ArgumentNullException(nameof(kinematics));
            Actuation = actuation ?? throw new ArgumentNullException(nameof(actuation));
            Sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
        }
    }

    public sealed class VehicleKinematics
    {
        public Vector3 Position { get; }
        public Quaternion Orientation { get; }
        public Vector3 Velocity { get; }
        public float Heading { get; }

        public VehicleKinematics(
            Vector3 position,
            Quaternion orientation,
            Vector3 velocity,
            float heading)
        {
            Position = position;
            Orientation = orientation;
            Velocity = velocity;
            Heading = heading;
        }
    }

    public sealed class VehicleActuation
    {
        public float LeftVelocity { get; }
        public float RightVelocity { get; }
        public float SteeringAngle { get; }
        public ArrowInputPackage Input { get; }

        public VehicleActuation(
            float leftVelocity,
            float rightVelocity,
            ArrowInputPackage input)
            : this(leftVelocity, rightVelocity, 0f, input)
        {
        }

        public VehicleActuation(
            float leftVelocity,
            float rightVelocity,
            float steeringAngle,
            ArrowInputPackage input)
        {
            LeftVelocity = leftVelocity;
            RightVelocity = rightVelocity;
            SteeringAngle = steeringAngle;
            Input = input;
        }
    }

    public sealed class VehicleSensors
    {
        // The vehicle model owns the camera sensor and publishes its latest
        // captured frame handle alongside the camera pose metadata.
        public CameraSensorSnapshot Camera { get; }
        public IReadOnlyList<DistanceSensorSnapshot> DistanceSensors { get; }

        public VehicleSensors(CameraSensorSnapshot camera)
            : this(camera, Array.Empty<DistanceSensorSnapshot>())
        {
        }

        public VehicleSensors(
            CameraSensorSnapshot camera,
            IReadOnlyList<DistanceSensorSnapshot> distanceSensors)
        {
            Camera = camera;
            DistanceSensors = distanceSensors ?? Array.Empty<DistanceSensorSnapshot>();
        }
    }

    public sealed class DistanceSensorSnapshot
    {
        public string SensorId { get; }
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public float Distance { get; }
        public float MaximumDistance { get; }
        public bool HasHit { get; }

        public DistanceSensorSnapshot(
            string sensorId,
            Vector3 origin,
            Vector3 direction,
            float distance,
            float maximumDistance,
            bool hasHit)
        {
            SensorId = string.IsNullOrWhiteSpace(sensorId) ? "distance" : sensorId.Trim();
            Origin = origin;
            Direction = direction.normalized;
            Distance = Mathf.Clamp(distance, 0f, Mathf.Max(0f, maximumDistance));
            MaximumDistance = Mathf.Max(0f, maximumDistance);
            HasHit = hasHit;
        }
    }

    public sealed class CameraSensorSnapshot
    {
        public Vector3 Position { get; }
        public Quaternion Orientation { get; }
        public CameraFrameSnapshot CapturedFrame { get; }
        public long FrameSequence => CapturedFrame != null ? CapturedFrame.Sequence : -1;

        public CameraSensorSnapshot(
            Vector3 position,
            Quaternion orientation,
            CameraFrameSnapshot capturedFrame = null)
        {
            Position = position;
            Orientation = orientation;
            CapturedFrame = capturedFrame;
        }
    }

    /// <summary>
    /// The vehicle model's latest camera exposure. The model owns the pooled
    /// texture; observation sources request a package copy when a stream is
    /// captured. This keeps the complete sensor in the vehicle snapshot without
    /// copying pixels into the state object.
    /// </summary>
    public sealed class CameraFrameSnapshot
    {
        public RenderTexture Frame { get; }
        public double SampledAt { get; }
        public long Sequence { get; }
        public Matrix4x4 WorldToCameraMatrix { get; }
        public Matrix4x4 ProjectionMatrix { get; }

        public CameraFrameSnapshot(
            RenderTexture frame,
            double sampledAt,
            long sequence,
            Matrix4x4 worldToCameraMatrix,
            Matrix4x4 projectionMatrix)
        {
            Frame = frame;
            SampledAt = sampledAt;
            Sequence = sequence;
            WorldToCameraMatrix = worldToCameraMatrix;
            ProjectionMatrix = projectionMatrix;
        }
    }

    public sealed class TurtleBotState : VehicleStateContract
    {
        public TurtleBotSnapshot Snapshot { get; }
        public Vector3 Position => Snapshot.Kinematics.Position;
        public float Heading => Snapshot.Kinematics.Heading;
        public float LeftVelocity => Snapshot.Actuation.LeftVelocity;
        public float RightVelocity => Snapshot.Actuation.RightVelocity;
        public ArrowInputPackage Input => Snapshot.Actuation.Input;

        public TurtleBotState(double timestamp, TurtleBotSnapshot snapshot) : base(timestamp)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public TurtleBotState(double timestamp, Vector3 position, float heading, float leftVelocity,
            float rightVelocity, ArrowInputPackage input) : base(timestamp)
        {
            Snapshot = new TurtleBotSnapshot(
                new VehicleKinematics(
                    position,
                    Quaternion.Euler(0f, heading * Mathf.Rad2Deg, 0f),
                    new Vector3(
                        (leftVelocity + rightVelocity) * 0.5f * Mathf.Cos(heading),
                        0f,
                        -(leftVelocity + rightVelocity) * 0.5f * Mathf.Sin(heading)),
                    heading),
                new VehicleActuation(leftVelocity, rightVelocity, input),
                new VehicleSensors(null));
        }
    }

    public sealed class PosePackage
    {
        public Vector3 Position { get; }
        public float Heading { get; }
        public double SampledAt { get; }
        public PosePackage(TurtleBotState state, double sampledAt)
        {
            Position = state.Position;
            Heading = state.Heading;
            SampledAt = sampledAt;
        }
    }

    public sealed class RobotStatePackage
    {
        public Vector3 Position { get; }
        public float Heading { get; }
        public float LeftVelocity { get; }
        public float RightVelocity { get; }
        public double SampledAt { get; }

        public RobotStatePackage(TurtleBotState state, double sampledAt)
        {
            Position = state.Snapshot.Kinematics.Position;
            Heading = state.Snapshot.Kinematics.Heading;
            LeftVelocity = state.Snapshot.Actuation.LeftVelocity;
            RightVelocity = state.Snapshot.Actuation.RightVelocity;
            SampledAt = sampledAt;
        }
    }

    /// <summary>
    /// Immutable-at-publication camera frame. The texture is a GPU-side copy of
    /// the remote render target, not a reference to the live Camera.
    /// </summary>
    public sealed class ViewFramePackage : IDisposable
    {
        private readonly Action<RenderTexture> releaseFrame;
        private bool released;

        public RenderTexture Frame { get; }
        public double SampledAt { get; }
        public long Sequence { get; }
        public Matrix4x4 WorldToCameraMatrix { get; }
        public Matrix4x4 ProjectionMatrix { get; }
        public TurtleBotState StateAtCapture { get; }

        public ViewFramePackage(
            RenderTexture frame,
            double sampledAt,
            long sequence,
            Matrix4x4 worldToCameraMatrix,
            Matrix4x4 projectionMatrix,
            TurtleBotState stateAtCapture,
            Action<RenderTexture> releaseFrame)
        {
            Frame = frame;
            SampledAt = sampledAt;
            Sequence = sequence;
            WorldToCameraMatrix = worldToCameraMatrix;
            ProjectionMatrix = projectionMatrix;
            StateAtCapture = stateAtCapture;
            this.releaseFrame = releaseFrame;
        }

        public void Release()
        {
            if (released) return;
            released = true;
            releaseFrame?.Invoke(Frame);
        }

        public void Dispose()
        {
            Release();
        }
    }

    public enum NetworkTimelineDirection { Up, Down, Left, Right }

    /// <summary>
    /// Semantic command-history event produced by Network assistance. Presentation
    /// decides where and how each direction is drawn.
    /// </summary>
    public sealed class NetworkTimelinePackage
    {
        public NetworkTimelineDirection[] Directions { get; }
        public double CommandTimestampSeconds { get; }
        public float RoundTripDelaySeconds { get; }

        public NetworkTimelinePackage(
            NetworkTimelineDirection[] directions,
            double commandTimestampSeconds,
            float roundTripDelaySeconds)
        {
            Directions = directions ?? Array.Empty<NetworkTimelineDirection>();
            CommandTimestampSeconds = commandTimestampSeconds;
            RoundTripDelaySeconds = roundTripDelaySeconds;
        }
    }

    [Serializable]
    public struct PredictedPose
    {
        public Vector3 position;
        public float heading;
        public float leftVelocity;
        public float rightVelocity;
    }

    /// <summary>
    /// Predicted-trajectory data for the Path visualisation: a centre trace plus the
    /// two wheel-offset traces. Consumed by <c>PathRibbonVisualisation</c>.
    /// </summary>
    public sealed class PathAssistancePackage
    {
        public PredictedPose[] Centre { get; }
        public PredictedPose[] Left { get; }
        public PredictedPose[] Right { get; }
        public PathAssistancePackage(
            PredictedPose[] centre, PredictedPose[] left, PredictedPose[] right)
        {
            Centre = centre;
            Left = left;
            Right = right;
        }
    }

    /// <summary>
    /// Predicted-trajectory data for the Envelope visualisation: the centre trace, the
    /// uncertainty-widened side boundaries, and the two centre-started extrema traces
    /// that close the end cap. Consumed by <c>EnvelopeRegionVisualisation</c>.
    /// </summary>
    public sealed class EnvelopeAssistancePackage
    {
        public PredictedPose[] Centre { get; }
        public PredictedPose[] Left { get; }
        public PredictedPose[] Right { get; }
        public PredictedPose[] ExtremaLeft { get; }
        public PredictedPose[] ExtremaRight { get; }
        public EnvelopeAssistancePackage(PredictedPose[] centre, PredictedPose[] left,
            PredictedPose[] right, PredictedPose[] extremaLeft, PredictedPose[] extremaRight)
        {
            Centre = centre;
            Left = left;
            Right = right;
            ExtremaLeft = extremaLeft;
            ExtremaRight = extremaRight;
        }
    }

    public static class EveryMoveMetricDefinitions
    {
        public static DataMetricDefinition[] Create()
        {
            return new[]
            {
                new DataMetricDefinition(
                    "completion-time",
                    "s",
                    "Timestamp of target-region-entry minus trial-start timestamp.",
                    "Capped by the 300 s elapsed-time criterion.",
                    "Report per condition and aggregate across participants using the declared analysis plan.",
                    "trial-start",
                    "target-region-entry"),
                new DataMetricDefinition(
                    "pause-count",
                    "count",
                    "Count gaps between non-neutral raw operator-input events greater than or equal to the configured uplink delay.",
                    "Gap threshold equals uplinkDelayMilliseconds exactly.",
                    "Report per trial and aggregate by condition.",
                    "trial-start",
                    "trial-ended"),
                new DataMetricDefinition(
                    "completion-rate",
                    "ratio",
                    "For timeout trials, map final rover position to the nearest of 101 successful-reference-trajectory points and divide its zero-based index by 100.",
                    "Defined only for timeout trials; successful trials equal 1.",
                    "Report per trial and aggregate by condition.",
                    "trial-start",
                    "trial-ended")
            };
        }
    }
}
