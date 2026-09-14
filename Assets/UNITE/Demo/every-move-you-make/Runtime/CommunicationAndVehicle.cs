using System;
using System.Collections.Generic;
using Unite.Core;
using Unite.Kernel;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unite.Demo.EveryMoveYouMake
{
    [Serializable]
    public sealed partial class EveryMoveFixedDelayCondition : CommunicationCondition
    {
        [SerializeField] private EveryMoveCommunicationConfiguration configuration;
        [SerializeField, Min(0f)] private float fallbackDelayMilliseconds = 2560f;
        [SerializeField] private bool useConfiguredDownlinkDelay;
        [SerializeField] private float channelDelayOverrideMilliseconds = -1f;
        public override bool TryGetTransmissionDelay(
            Package package,
            double ingressTimestampSeconds,
            out double delaySeconds)
        {
            float delay = fallbackDelayMilliseconds;
            if (channelDelayOverrideMilliseconds >= 0f)
            {
                delay = channelDelayOverrideMilliseconds;
            }
            else if (configuration)
            {
                delay = useConfiguredDownlinkDelay
                    ? configuration.downlinkDelayMilliseconds
                    : configuration.uplinkDelayMilliseconds;
            }
            delaySeconds = delay / 1000d;
            return true;
        }
    }

    public sealed partial class SlopeBoundaryGuard : UplinkRemoteSideAssistanceModule
    {
        [SerializeField] private MeshCollider terrain;
        [SerializeField] private Transform vehicle;
        [SerializeField, Range(0f, 90f)] private float maximumSlopeDegrees = 21.80141f;

        // Reference BoundaryDetection thresholds: a rise of more than 8 cm over a 20 cm
        // look-ahead is treated as a wall rather than a navigable bump.
        private const float CheckDistance = 0.2f;

        public float MaximumSlopeDegrees => maximumSlopeDegrees;

        protected override bool TryProcessPackage(Package package, double timestamp, out Package output)
        {
            output = package;
            if (!terrain || !vehicle) return true;
            if (!(package.Payload is WheelVelocityCommand command)) return true;

            // Only translational commands can drive into a wall; ignore pure rotation.
            float commanded = (command.RequestedLeft + command.RequestedRight) * 0.5f;
            if (Mathf.Abs(commanded) < 0.001f) return true;

            if (!TryGetObservedPose(
                    out float px,
                    out float pz,
                    out float heading))
            {
                return true;
            }
            float relativeAngle = commanded > 0f ? 0f : Mathf.PI;
            if (!IsWallInDirection(px, pz, heading, relativeAngle)) return true;

            // Reference behaviour on wall contact: reverse away from the obstacle instead of
            // climbing it.
            output = new Package(
                new WheelVelocityCommand(command.Input, timestamp, -command.RequestedLeft, -command.RequestedRight),
                package.SourceTimestampSeconds, package.StreamId);
            return true;
        }

        private bool TryGetObservedPose(
            out float x,
            out float z,
            out float heading)
        {
            if (TryGetLatestObservation<PosePackage>(
                    EveryMoveStreams.Pose,
                    out PosePackage observation) &&
                observation != null)
            {
                x = observation.Position.x;
                z = observation.Position.z;
                heading = observation.Heading;
                return true;
            }

            // The first control tick can precede the first published vehicle pose.
            // Retain the scene transform as a startup fallback only; subsequent
            // assistance decisions use the locally captured observation package.
            if (vehicle)
            {
                x = vehicle.position.x;
                z = vehicle.position.z;
                heading = vehicle.eulerAngles.y * Mathf.Deg2Rad;
                return true;
            }

            x = 0f;
            z = 0f;
            heading = 0f;
            return false;
        }

        private bool IsWallInDirection(float x, float z, float heading, float relativeAngle)
        {
            if (!TryTerrainHeight(x, z, out float currentHeight)) return false;
            float absoluteAngle = heading + relativeAngle;
            float checkX = x + Mathf.Cos(absoluteAngle) * CheckDistance;
            float checkZ = z - Mathf.Sin(absoluteAngle) * CheckDistance;
            if (!TryTerrainHeight(checkX, checkZ, out float checkHeight)) return false;
            float slopeDegrees = Mathf.Atan2(
                checkHeight - currentHeight,
                CheckDistance) * Mathf.Rad2Deg;
            return slopeDegrees > maximumSlopeDegrees;
        }

        private bool TryTerrainHeight(float x, float z, out float height)
        {
            if (terrain.Raycast(new Ray(new Vector3(x, 1000f, z), Vector3.down), out RaycastHit hit, 2000f))
            {
                height = hit.point.y;
                return true;
            }
            height = 0f;
            return false;
        }
    }

    public sealed partial class TurtleBot3WafflePiEnhanced
        : VehicleRobotModel<WheelVelocityCommand, TurtleBotState>
    {
        [SerializeField] private EveryMoveVehicleConfiguration configuration;
        [SerializeField] private Transform robot;
        [SerializeField] private Transform robotMesh;
        [SerializeField] private Transform robotCamera;
        [SerializeField] private Camera cameraSensor;
        [SerializeField] private MeshCollider terrain;
        [SerializeField, Min(160)] private int cameraCaptureWidth = 960;
        [SerializeField, Min(90)] private int cameraCaptureHeight = 540;
        [SerializeField, Min(1f)] private float cameraCaptureRateHz = 20f;
        private float x, z, theta, vL, vR, heightVelocity;
        private Vector3 robotMeshLocal, robotCameraLocal;
        private ArrowInputPackage latestInput;
        private RenderTexture cameraRenderTarget;
        private RenderTexture previousCameraTarget;
        private readonly Queue<RenderTexture> cameraFramePool = new Queue<RenderTexture>();
        private RenderTexture latestCameraFrame;
        private double nextCameraCaptureAt;
        private long cameraFrameSequence;
        private bool cameraCaptureSubscribed;
        private TurtleBotState latestStateAtCameraCapture;

        public Camera CameraSensor => cameraSensor;
        public int CameraCaptureWidth => Mathf.Max(160, cameraCaptureWidth);
        public int CameraCaptureHeight => Mathf.Max(90, cameraCaptureHeight);
        public float CameraCaptureRateHz => Mathf.Max(1f, cameraCaptureRateHz);
        public bool IsCameraRigidToChassis =>
            cameraSensor && robot && cameraSensor.transform.IsChildOf(robot);

        protected override void OnInitialize()
        {
            if (!configuration || !robot) { Debug.LogError("Vehicle configuration and robot are required.", this); enabled = false; return; }
            x = robot.position.x; z = robot.position.z; theta = robot.eulerAngles.y * Mathf.Deg2Rad;
            if (!robotMesh)
            {
                // The visible body is the child carrying the BoxCollider (the reference's
                // "turtlebot-3 v48").
                BoxCollider body = robot.GetComponentInChildren<BoxCollider>(true);
                if (body) robotMesh = body.transform;
            }
            if (!robotCamera)
            {
                // The onboard camera is mounted on the body, so it shifts with it.
                Camera onboard = robot.GetComponentInChildren<Camera>(true);
                if (onboard) robotCamera = onboard.transform;
            }
            if (!cameraSensor && robotCamera)
                cameraSensor = robotCamera.GetComponent<Camera>();
            // Capture the authored mounts so the apex shift is re-derived from them each step
            // (preserving the camera's mount) rather than accumulating.
            if (robotMesh) robotMeshLocal = robotMesh.localPosition;
            if (robotCamera) robotCameraLocal = robotCamera.localPosition;

            EnsureCameraRenderTarget();
            if (cameraSensor)
            {
                RenderPipelineManager.endCameraRendering += CaptureRenderedCameraFrame;
                cameraCaptureSubscribed = true;
            }
        }

        protected override void ApplyCommand(WheelVelocityCommand command, double timestamp)
        {
            latestInput = command.Input;
            float dt = command.DeltaTime;
            vL = Mathf.MoveTowards(vL, command.RequestedLeft, configuration.maxLinearAcceleration * dt);
            vR = Mathf.MoveTowards(vR, command.RequestedRight, configuration.maxLinearAcceleration * dt);
            ApplyDisturbances(ref vL, ref vR, dt);
            float difference = Mathf.Clamp(vR - vL, -configuration.maxWheelVelocityDifference,
                configuration.maxWheelVelocityDifference);
            float velocity = (vL + vR) * 0.5f;
            x += velocity * Mathf.Cos(theta) * dt;
            z -= velocity * Mathf.Sin(theta) * dt;
            theta += difference / configuration.wheelbase * dt;
        }

        // Faithful port of EnhancedDifferentialDrive.ApplyPhysicsEffects (Unity built-in
        // branch). Roughness and slope are derived from the surface exactly as
        // GetTerrainPropertiesAt did, so the configured roughness is not used here. The
        // effects are deterministic (seed + Time.fixedTime), matching the reference.
        private void ApplyDisturbances(ref float left, ref float right, float dt)
        {
            GetTerrainProperties(x, z, out float roughness, out float slopeAngle);

            int seed = configuration.deterministicSeed;
            float t = seed * 0.001f + Time.fixedTime;

            // 1. Wheel slip (multiplicative, scales with speed).
            float speedFactor = (Mathf.Abs(left) + Mathf.Abs(right)) * 0.5f;
            float slip = configuration.wheelSlipFactor * roughness * speedFactor * 0.4f;
            left *= 1f - slip;
            right *= 1f - slip;

            // 2. Motor response variation.
            float motorL = configuration.motorResponseVariation * (Mathf.Sin(t * 3.7f) + Mathf.Sin(t * 1.3f)) * 0.5f;
            float motorR = configuration.motorResponseVariation * (Mathf.Sin(t * 4.1f) + Mathf.Sin(t * 1.7f)) * 0.5f;
            left *= 1f + motorL;
            right *= 1f + motorR;

            // 3. Manufacturing wheel-radius difference.
            float wheelDifference = configuration.wheelRadiusVariation * 2f;
            left *= 1f + wheelDifference * Mathf.Sin(t * 0.1f);
            right *= 1f - wheelDifference * Mathf.Sin(t * 0.1f);

            // 4. Terrain-induced vibration.
            float vibeFrequency = configuration.vibrationFrequency;
            float vibeIntensity = configuration.vibrationIntensity * roughness * 2.5f;
            float vibeL = Mathf.PerlinNoise(x * vibeFrequency * 0.1f + t, z * vibeFrequency * 0.1f) * 2f - 1f;
            float vibeR = Mathf.PerlinNoise(x * vibeFrequency * 0.1f + t + 100f, z * vibeFrequency * 0.1f + 100f) * 2f - 1f;
            vibeL += (Mathf.PerlinNoise(x * vibeFrequency * 0.3f + t * 2f, z * vibeFrequency * 0.3f) * 2f - 1f) * 0.6f;
            vibeR += (Mathf.PerlinNoise(x * vibeFrequency * 0.3f + t * 2f + 200f, z * vibeFrequency * 0.3f + 200f) * 2f - 1f) * 0.6f;
            if (roughness > 0.6f)
            {
                vibeL += (Mathf.PerlinNoise(x * vibeFrequency * 0.8f + t * 4f, z * vibeFrequency * 0.8f) * 2f - 1f) * 0.3f * roughness;
                vibeR += (Mathf.PerlinNoise(x * vibeFrequency * 0.8f + t * 4f + 300f, z * vibeFrequency * 0.8f + 300f) * 2f - 1f) * 0.3f * roughness;
            }
            left *= 1f + vibeL * vibeIntensity;
            right *= 1f + vibeR * vibeIntensity;

            // 5. Encoder measurement noise (multiplicative).
            float noiseL = Mathf.PerlinNoise(x * 7.3f + seed * 0.01f, z * 7.3f + t * 0.5f) * 2f - 1f;
            float noiseR = Mathf.PerlinNoise(x * 7.3f + seed * 0.01f + 500f, z * 7.3f + t * 0.5f + 500f) * 2f - 1f;
            left *= 1f + noiseL * configuration.encoderNoise * 3f;
            right *= 1f + noiseR * configuration.encoderNoise * 3f;

            // 6. Slope bias. Additive, so it moves the rover downhill even with zero commanded
            // velocity: this is why the reference rover drifts downhill when left idle.
            if (slopeAngle > 5f)
            {
                Vector3 gradient = SlopeGradient(x, z);
                float slopeEffect = Vector3.Dot(gradient, new Vector3(Mathf.Cos(theta), 0f, -Mathf.Sin(theta)));
                float slopeBias = slopeEffect * slopeAngle * 0.02f;
                left -= slopeBias * (1f + 0.5f * Mathf.Sin(theta));
                right -= slopeBias * (1f - 0.5f * Mathf.Sin(theta));
            }

            if (float.IsNaN(left) || float.IsInfinity(left)) left = 0f;
            if (float.IsNaN(right) || float.IsInfinity(right)) right = 0f;
            left = Mathf.Clamp(left, -configuration.maxLinearVelocity, configuration.maxLinearVelocity);
            right = Mathf.Clamp(right, -configuration.maxLinearVelocity, configuration.maxLinearVelocity);
        }

        private void GetTerrainProperties(float px, float pz, out float roughness, out float slopeAngle)
        {
            if (!terrain)
            {
                roughness = 0.3f;
                slopeAngle = 0f;
                return;
            }
            float heightCentre = SampleTerrainHeight(px, pz);
            float heightForward = SampleTerrainHeight(px, pz + 0.1f);
            float heightRight = SampleTerrainHeight(px + 0.1f, pz);
            float slopeForward = Mathf.Abs(heightForward - heightCentre) / 0.1f;
            float slopeRight = Mathf.Abs(heightRight - heightCentre) / 0.1f;
            float averageSlope = (slopeForward + slopeRight) * 0.5f;
            slopeAngle = Mathf.Atan(averageSlope) * Mathf.Rad2Deg;
            roughness = Mathf.Lerp(0.4f, 0.9f, slopeAngle / 30f);
        }

        private Vector3 SlopeGradient(float px, float pz)
        {
            if (!terrain) return Vector3.zero;
            float centre = SampleTerrainHeight(px, pz);
            float right = SampleTerrainHeight(px + 0.1f, pz);
            float forward = SampleTerrainHeight(px, pz + 0.1f);
            return new Vector3((right - centre) / 0.1f, 0f, (forward - centre) / 0.1f);
        }

        private float SampleTerrainHeight(float px, float pz)
        {
            if (terrain.Raycast(new Ray(new Vector3(px, 1000f, pz), Vector3.down), out RaycastHit hit, 2000f))
                return hit.point.y;
            return 0f;
        }

        protected override void UpdateVehicleState(double timestamp)
        {
            Vector3 position = new Vector3(x, robot.position.y, z);
            Vector3 normal = Vector3.up;
            if (terrain && terrain.Raycast(new Ray(new Vector3(x, 1000f, z), Vector3.down), out RaycastHit hit, 2000f))
            {
                float targetY = hit.point.y + configuration.surfaceOffset;
                position.y = Mathf.SmoothDamp(robot.position.y, targetY, ref heightVelocity, configuration.heightSmoothTime);
                normal = hit.normal;
            }
            robot.position = position;
            Quaternion heading = Quaternion.Euler(0f, theta * Mathf.Rad2Deg, 0f);
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, normal) * heading;
            robot.rotation = Quaternion.Slerp(robot.rotation, tilt, configuration.tiltResponsiveness * Time.deltaTime);

            // Shift the visible body AND the onboard camera mounted on it backward along
            // heading by meshForwardOffset (reference: -0.0765 m), as one rigid unit. The
            // published state — which drives the prediction overlays and pose feedback — stays
            // at the kinematic origin, so the overlays appear to spring from the robot's apex
            // while the video feed keeps its correct viewpoint relative to the body. Each
            // transform is re-derived from its captured mount (parent.TransformPoint) so the
            // camera keeps its mount and the shift never accumulates. The root itself stays at
            // the kinematic origin, so task detection and the slope guard use the true point.
            // Forward in XZ is (cos theta, -sin theta).
            Vector3 apexShift = new Vector3(
                Mathf.Cos(theta) * configuration.meshForwardOffset, 0f,
                -Mathf.Sin(theta) * configuration.meshForwardOffset);
            if (robotMesh && robotMesh.parent)
                robotMesh.position = robotMesh.parent.TransformPoint(robotMeshLocal) + apexShift;
            if (robotCamera && robotCamera.parent)
                robotCamera.position = robotCamera.parent.TransformPoint(robotCameraLocal) + apexShift;

            Vector3 velocity = new Vector3(
                (vL + vR) * 0.5f * Mathf.Cos(theta),
                0f,
                -(vL + vR) * 0.5f * Mathf.Sin(theta));
            CameraSensorSnapshot camera = robotCamera
                ? new CameraSensorSnapshot(
                    robotCamera.position,
                    robotCamera.rotation,
                    latestCameraFrame
                        ? new CameraFrameSnapshot(
                            latestCameraFrame,
                            latestCameraFrameTimestamp,
                            latestCameraFrameSequence,
                            latestCameraWorldToCameraMatrix,
                            latestCameraProjectionMatrix)
                        : null)
                : null;
            TurtleBotState state = new TurtleBotState(
                timestamp,
                new TurtleBotSnapshot(
                    new VehicleKinematics(
                        position,
                        Quaternion.Euler(0f, theta * Mathf.Rad2Deg, 0f),
                        velocity,
                        theta),
                    new VehicleActuation(vL, vR, latestInput),
                    new VehicleSensors(camera)));
            PublishState(state);
        }

        private double latestCameraFrameTimestamp;
        private long latestCameraFrameSequence = -1;
        private Matrix4x4 latestCameraWorldToCameraMatrix;
        private Matrix4x4 latestCameraProjectionMatrix;

        public bool TryCopyCameraFrame(
            CameraFrameSnapshot cameraFrame,
            out RenderTexture frame,
            out TurtleBotState stateAtCapture)
        {
            frame = null;
            stateAtCapture = latestStateAtCameraCapture;
            if (cameraFrame == null || !cameraFrame.Frame || !cameraRenderTarget)
                return false;

            frame = AcquireCameraFrame();
            frame.name = $"RemoteFrame-{cameraFrame.Sequence}";
            frame.DiscardContents();
            Graphics.Blit(cameraFrame.Frame, frame);
            return true;
        }

        private void CaptureRenderedCameraFrame(
            ScriptableRenderContext context,
            Camera renderedCamera)
        {
            if (renderedCamera != cameraSensor || !cameraRenderTarget || !cameraCaptureSubscribed)
                return;
            double sampledAt = Time.realtimeSinceStartupAsDouble;
            if (sampledAt + 0.000001d < nextCameraCaptureAt)
                return;
            nextCameraCaptureAt = sampledAt + 1d / CameraCaptureRateHz;

            RecycleCameraFrame(latestCameraFrame);
            latestCameraFrame = AcquireCameraFrame();
            latestCameraFrame.name = "LatestVehicleCameraSensorFrame";
            latestCameraFrame.DiscardContents();
            Graphics.Blit(cameraRenderTarget, latestCameraFrame);
            latestCameraFrameTimestamp = sampledAt;
            latestCameraFrameSequence++;
            latestCameraWorldToCameraMatrix = cameraSensor.worldToCameraMatrix;
            latestCameraProjectionMatrix = cameraSensor.projectionMatrix;
            latestStateAtCameraCapture = LatestState as TurtleBotState;
        }

        private bool EnsureCameraRenderTarget()
        {
            if (!cameraSensor)
                return false;
            int width = CameraCaptureWidth;
            int height = CameraCaptureHeight;
            if (cameraRenderTarget &&
                cameraRenderTarget.width == width &&
                cameraRenderTarget.height == height)
            {
                if (cameraSensor.targetTexture != cameraRenderTarget)
                    cameraSensor.targetTexture = cameraRenderTarget;
                return true;
            }

            ReleaseCameraRenderTarget();
            previousCameraTarget = cameraSensor.targetTexture;
            cameraRenderTarget = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "EveryMoveVehicleCameraSensor",
                useMipMap = false,
                autoGenerateMips = false
            };
            cameraRenderTarget.Create();
            cameraSensor.targetTexture = cameraRenderTarget;
            cameraSensor.enabled = true;
            return true;
        }

        private RenderTexture AcquireCameraFrame()
        {
            while (cameraFramePool.Count > 0)
            {
                RenderTexture frame = cameraFramePool.Dequeue();
                if (frame && cameraRenderTarget &&
                    frame.width == cameraRenderTarget.width &&
                    frame.height == cameraRenderTarget.height)
                    return frame;
                if (frame)
                {
                    frame.Release();
                    Destroy(frame);
                }
            }

            RenderTexture created = new RenderTexture(
                cameraRenderTarget.width,
                cameraRenderTarget.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                autoGenerateMips = false
            };
            created.Create();
            return created;
        }

        public void RecycleCameraFrame(RenderTexture frame)
        {
            if (!frame) return;
            if (!cameraRenderTarget || frame.width != cameraRenderTarget.width ||
                frame.height != cameraRenderTarget.height)
            {
                frame.Release();
                Destroy(frame);
                return;
            }
            cameraFramePool.Enqueue(frame);
        }

        private void ReleaseCameraRenderTarget()
        {
            if (!cameraRenderTarget)
                return;
            if (cameraSensor && cameraSensor.targetTexture == cameraRenderTarget)
                cameraSensor.targetTexture = previousCameraTarget;
            cameraRenderTarget.Release();
            Destroy(cameraRenderTarget);
            cameraRenderTarget = null;
        }

        private void OnDestroy()
        {
            if (cameraCaptureSubscribed)
            {
                RenderPipelineManager.endCameraRendering -= CaptureRenderedCameraFrame;
                cameraCaptureSubscribed = false;
            }
            if (latestCameraFrame)
            {
                latestCameraFrame.Release();
                Destroy(latestCameraFrame);
                latestCameraFrame = null;
            }
            ReleaseCameraRenderTarget();
            while (cameraFramePool.Count > 0)
            {
                RenderTexture frame = cameraFramePool.Dequeue();
                if (!frame) continue;
                frame.Release();
                Destroy(frame);
            }
        }
    }
}
