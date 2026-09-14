using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unite.Core;
using Unite.Kernel;
using UnityEngine;
using UnityEngine.UI;

namespace Unite.Demo.EveryMoveYouMake
{
    public sealed partial class RoverViewPresentation : OperatorPresentationModule
    {
        // Predicted trajectories are drawn as terrain-projected meshes, as the reference
        // RenderPath (centre + wheel ribbons) and RenderRegion (filled corridor) did.
        // The host owns the always-on reconstructed-video surface and the prediction-overlay
        // camera. Each per-condition visualisation is a separate EveryMoveVisualisation
        // presentation source; this module routes released packages to them (see
        // PresentPackage) and never draws a condition-specific overlay itself.
        [SerializeField] private EveryMoveStateReconstruction stateReconstruction;

        private GameObject operatorFeedbackCanvas;
        private RawImage operatorVideoImage;
        private RawImage operatorPredictionImage;
        private Camera operatorOutputCamera;
        private Camera predictionCamera;
        private RenderTexture predictionTexture;
        private int predictionTextureWidth;
        private int predictionTextureHeight;
        private int predictionLayer;
        private long presentedViewSequence = -1;

        protected override void InitializePresentationSources()
        {
            if (!stateReconstruction)
                stateReconstruction = GetComponent<EveryMoveStateReconstruction>();
            ConfigurePredictionLayer();
            EnsureOperatorFeedbackSurface();
        }

        // Route each released feedback package to the first visualisation source that
        // recognises its payload. The host never interprets a condition-specific overlay,
        // so adding a visualisation needs no change here.
        protected override void PresentPackage(Package package, double timestamp)
        {
            foreach (OperatorPresentationSource source in Sources)
            {
                if (source is EveryMoveVisualisation visualisation &&
                    visualisation.TryPresent(package, timestamp))
                    return;
            }
        }

        protected override void UpdatePresentation(double timestamp)
        {
            PresentReconstructedVideo();
            foreach (OperatorPresentationSource source in Sources)
            {
                if (source is EveryMoveVisualisation visualisation)
                    visualisation.UpdateVisual(timestamp);
            }
        }

        private void ConfigurePredictionLayer()
        {
            predictionLayer = LayerMask.NameToLayer("OperatorPrediction");
            if (predictionLayer < 0)
            {
                Debug.LogError(
                    "The OperatorPrediction Unity layer is required for local predictive overlays.",
                    this);
                enabled = false;
                return;
            }

            // The remote camera must not render the local prediction overlay; each
            // visualisation places its own meshes on this layer.
            TurtleBot3WafflePiEnhanced vehicle =
                GetComponent<TurtleBot3WafflePiEnhanced>();
            Camera remoteCamera = vehicle ? vehicle.CameraSensor : null;
            if (remoteCamera)
                remoteCamera.cullingMask &= ~(1 << predictionLayer);
        }

        private void EnsureOperatorFeedbackSurface()
        {
            if (operatorFeedbackCanvas)
                return;

            operatorFeedbackCanvas = new GameObject(
                "OperatorFeedbackCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            Canvas canvas = operatorFeedbackCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 0;
            CanvasScaler scaler = operatorFeedbackCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1158f, 650f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            operatorVideoImage = CreateFullScreenImage("DelayedRemoteVideo", 0);
            operatorVideoImage.color = Color.black;
            operatorPredictionImage = CreateFullScreenImage("LocalPredictionOverlay", 1);

            // Unity's Game View requires one Camera to target Display 1 even when
            // the operator image itself is composed by screen-space UI. This camera
            // renders no scene layers; it only provides the display backbuffer below
            // the reconstructed-video and local-prediction surfaces.
            Camera[] sceneCameras = FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (Camera sceneCamera in sceneCameras)
            {
                if (sceneCamera &&
                    sceneCamera.enabled &&
                    sceneCamera.targetTexture == null &&
                    sceneCamera.targetDisplay == 0 &&
                    sceneCamera.cullingMask == 0)
                {
                    operatorOutputCamera = sceneCamera;
                    break;
                }
            }

            if (!operatorOutputCamera)
            {
                var outputCameraObject = new GameObject("OperatorOutputCamera", typeof(Camera));
                outputCameraObject.transform.SetParent(operatorFeedbackCanvas.transform, false);
                operatorOutputCamera = outputCameraObject.GetComponent<Camera>();
            }

            operatorOutputCamera.clearFlags = CameraClearFlags.SolidColor;
            operatorOutputCamera.backgroundColor = Color.black;
            operatorOutputCamera.cullingMask = 0;
            operatorOutputCamera.targetTexture = null;
            operatorOutputCamera.targetDisplay = 0;
            operatorOutputCamera.depth = -100f;
            operatorOutputCamera.allowHDR = false;
            operatorOutputCamera.allowMSAA = false;
            operatorOutputCamera.enabled = true;

            var cameraObject = new GameObject("OperatorPredictionCamera", typeof(Camera));
            cameraObject.transform.SetParent(operatorFeedbackCanvas.transform, false);
            predictionCamera = cameraObject.GetComponent<Camera>();
            predictionCamera.clearFlags = CameraClearFlags.SolidColor;
            predictionCamera.backgroundColor = Color.clear;
            predictionCamera.cullingMask = 1 << predictionLayer;
            predictionCamera.allowHDR = false;
            predictionCamera.allowMSAA = false;
            predictionCamera.enabled = false;
            EnsurePredictionTexture();
        }

        private RawImage CreateFullScreenImage(string name, int siblingIndex)
        {
            var imageObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            RectTransform rect = (RectTransform)imageObject.transform;
            rect.SetParent(operatorFeedbackCanvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetSiblingIndex(siblingIndex);
            RawImage image = imageObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;
            return image;
        }

        private void EnsurePredictionTexture()
        {
            int width = Mathf.Max(160, Screen.width);
            int height = Mathf.Max(90, Screen.height);
            if (predictionTexture &&
                predictionTextureWidth == width &&
                predictionTextureHeight == height)
                return;

            if (predictionTexture)
            {
                predictionTexture.Release();
                Destroy(predictionTexture);
            }

            predictionTextureWidth = width;
            predictionTextureHeight = height;
            predictionTexture = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "EveryMoveLocalPrediction",
                useMipMap = false,
                autoGenerateMips = false
            };
            predictionTexture.Create();
            if (predictionCamera)
                predictionCamera.targetTexture = predictionTexture;
            if (operatorPredictionImage)
                operatorPredictionImage.texture = predictionTexture;
        }

        private void PresentReconstructedVideo()
        {
            if (!stateReconstruction || !operatorVideoImage || !predictionCamera)
                return;
            EnsurePredictionTexture();

            ReconstructedVideoFrame view = stateReconstruction.LatestViewFrame;
            if (view == null || !view.Frame)
                return;
            if (view.Sequence == presentedViewSequence)
                return;

            presentedViewSequence = view.Sequence;
            operatorVideoImage.texture = view.Frame;
            operatorVideoImage.color = Color.white;

            // The local overlay uses the camera matrices captured with the released
            // frame. Predictions therefore remain aligned with reconstructed video without
            // consulting the current remote camera or authoritative rover pose.
            predictionCamera.worldToCameraMatrix = view.WorldToCameraMatrix;
            predictionCamera.projectionMatrix = view.ProjectionMatrix;
            predictionCamera.enabled = true;
        }

        private void OnDestroy()
        {
            if (predictionTexture)
            {
                predictionTexture.Release();
                Destroy(predictionTexture);
            }
            if (operatorFeedbackCanvas)
                Destroy(operatorFeedbackCanvas);
        }

    }

    public sealed partial class TargetRegionEntry : TaskTerminationCriterion
    {
        [SerializeField] private Transform authoritativeRobot;
        [SerializeField] private EveryMoveTaskConfiguration configuration;
        [SerializeField, Min(0f)] private float detectionTolerance = 0.15f;
        private Renderer[] robotRenderers = Array.Empty<Renderer>();

        private void Start()
        {
            if (authoritativeRobot)
                robotRenderers = authoritativeRobot.GetComponentsInChildren<Renderer>(true);
        }

        protected override bool TryEvaluate(double timestamp, out TrialTerminationResult result)
        {
            result = null;
            if (!authoritativeRobot || !configuration) return false;
            if (!IsRobotOverTarget()) return false;
            result = new TrialTerminationResult(TrialTerminationOutcome.Success, "target-region-entry"); return true;
        }

        private bool IsRobotOverTarget()
        {
            float effectiveRadius = configuration.targetRadius + detectionTolerance;
            Bounds bounds;
            if (!TryGetRobotBounds(out bounds))
            {
                bounds = new Bounds(
                    authoritativeRobot.position,
                    new Vector3(0.35f, 0.14f, 0.22f));
            }

            Vector2 target = new Vector2(
                configuration.targetCenter.x,
                configuration.targetCenter.z);
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            var corners = new[]
            {
                new Vector2(center.x - extents.x, center.z - extents.z),
                new Vector2(center.x + extents.x, center.z - extents.z),
                new Vector2(center.x - extents.x, center.z + extents.z),
                new Vector2(center.x + extents.x, center.z + extents.z)
            };
            for (int index = 0; index < corners.Length; index++)
            {
                if (Vector2.Distance(corners[index], target) > effectiveRadius)
                    return false;
            }
            return true;
        }

        private bool TryGetRobotBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            for (int index = 0; index < robotRenderers.Length; index++)
            {
                Renderer renderer = robotRenderers[index];
                if (!renderer || !renderer.enabled)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }
    }

    public sealed partial class ElapsedTimeLimit : TaskTerminationCriterion
    {
        [SerializeField] private EveryMoveTaskConfiguration configuration;
        private double startedAt = -1d;
        protected override bool TryEvaluate(double timestamp, out TrialTerminationResult result)
        {
            if (startedAt < 0d) startedAt = timestamp; result = null;
            if (!configuration || timestamp - startedAt < configuration.timeLimitSeconds) return false;
            result = new TrialTerminationResult(TrialTerminationOutcome.Timeout, "elapsed-time-limit"); return true;
        }
    }

    public sealed partial class TrialLogger : DataCaptureImplementation
    {
        [SerializeField] private KeyboardArrowProvider inputProvider;
        [SerializeField] private ArrowToWheelVelocityMapping commandMapping;
        [SerializeField] private UplinkCommunicationModule uplink;
        [SerializeField] private DownlinkCommunicationKernel downlink;
        [SerializeField] private VehicleRobotModelModule vehicle;
        [SerializeField] private EveryMoveCommunicationConfiguration communication;
        [SerializeField] private EveryMoveVehicleConfiguration vehicleConfiguration;
        [SerializeField] private EveryMoveTaskConfiguration taskConfiguration;
        [SerializeField] private bool writeCsvOnTrialEnd = true;
        [SerializeField, Min(0.1f)] private float flushIntervalSeconds = 1f;
        private readonly StringBuilder csv = new StringBuilder();
        [SerializeField] private DownlinkOperatorSideAssistanceModule assistance;
        private readonly Queue<PendingPackageEvent> pendingPackageEvents =
            new Queue<PendingPackageEvent>();
        private string outputPath;
        private double nextFlushAt;
        private bool trialStarted;

        private readonly struct PendingPackageEvent
        {
            public PendingPackageEvent(string eventName, Package package)
            {
                EventName = eventName;
                Package = package;
            }

            public string EventName { get; }
            public Package Package { get; }
        }

        protected override void OnCaptureInitialized()
        {
            if (!inputProvider) inputProvider = GetComponent<KeyboardArrowProvider>();
            if (!commandMapping) commandMapping = GetComponent<ArrowToWheelVelocityMapping>();
            if (!uplink) uplink = GetComponent<UplinkCommunicationModule>();
            if (!downlink) downlink = GetComponent<DownlinkCommunicationKernel>();
            if (!assistance) assistance = GetComponent<DownlinkOperatorSideAssistanceModule>();

            if (inputProvider) inputProvider.OutputProduced += CaptureRawInput;
            if (commandMapping) commandMapping.OutputProduced += CaptureMappedCommand;
            if (uplink) uplink.PackageProduced += CaptureUplinkRelease;
            if (downlink) downlink.PackageProduced += CaptureDownlinkRelease;
            if (assistance) assistance.PackageProduced += CaptureAssistanceOutput;

            string filename =
                $"every-move-{Sanitize(Agent.ID)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            outputPath = Path.Combine(Application.persistentDataPath, filename);
            csv.AppendLine("timestamp,event,stream,source_timestamp,value,unit");
        }

        protected override void OnCaptureStep(double timestamp)
        {
            EnsureTrialStarted(timestamp);
            DrainPackageEvents(timestamp);
            if (!vehicle || !(vehicle.LatestState is TurtleBotState s)) return;
            Record("authoritative-pose", s, timestamp);
            AppendEvent(
                timestamp,
                "authoritative-state",
                "vehicle-state",
                s.TimestampSeconds,
                FormattableString.Invariant(
                    $"x={s.Position.x:F6};z={s.Position.z:F6};heading={s.Heading:F6};left_velocity={s.LeftVelocity:F6};right_velocity={s.RightVelocity:F6}"),
                "m,rad,m/s");
            if (timestamp >= nextFlushAt)
            {
                FlushCsv();
                nextFlushAt = timestamp + Mathf.Max(0.1f, flushIntervalSeconds);
            }
        }

        protected override void OnTrialEnded(TrialEndedEvent ended)
        {
            EnsureTrialStarted(ended.TimestampSeconds);
            Record(EveryMoveStreams.TrialEnded, ended, ended.TimestampSeconds);
            AppendEvent(
                ended.TimestampSeconds,
                EveryMoveStreams.TrialEnded,
                EveryMoveStreams.TrialEnded,
                ended.TimestampSeconds,
                $"outcome={ended.Outcome};reason={ended.Reason};criterion={ended.CriterionName}",
                string.Empty);
            FlushCsv();
            Unsubscribe();
        }

        private void CaptureRawInput(ArrowInputPackage input)
        {
            EnsureTrialStarted(input.TimestampSeconds);
            Record("raw-operator-input", input, input.TimestampSeconds);
            AppendEvent(
                input.TimestampSeconds,
                "raw-input",
                "arrow-input",
                input.TimestampSeconds,
                $"up={Bool(input.Up)};down={Bool(input.Down)};left={Bool(input.Left)};right={Bool(input.Right)};dt={input.DeltaTime.ToString("F6", CultureInfo.InvariantCulture)}",
                "boolean,s");
        }

        private void CaptureMappedCommand(WheelVelocityCommand command)
        {
            EnsureTrialStarted(command.TimestampSeconds);
            Record("mapped-command", command, command.TimestampSeconds, "m/s");
            AppendEvent(
                command.TimestampSeconds,
                "mapped-command",
                EveryMoveStreams.Command,
                command.SourceTimestampSeconds,
                FormattableString.Invariant(
                    $"requested_left={command.RequestedLeft:F6};requested_right={command.RequestedRight:F6};dt={command.DeltaTime:F6}"),
                "m/s,s");
        }

        private void CaptureUplinkRelease(Package package)
        {
            EnqueuePackageEvent("uplink-release", package);
        }

        private void CaptureDownlinkRelease(Package package)
        {
            EnqueuePackageEvent("downlink-release", package);
        }

        private void CaptureAssistanceOutput(Package package)
        {
            EnqueuePackageEvent("assistance-output", package);
        }

        // Package-release events fire mid-tick from other modules and carry no
        // timestamp. Buffer them and stamp them in OnCaptureStep, which the Kernel
        // calls last (Data Capture is the final module each tick) with the
        // authoritative tick time, rather than reading the ambient Agent clock here.
        private void EnqueuePackageEvent(string eventName, Package package)
        {
            if (package != null)
                pendingPackageEvents.Enqueue(new PendingPackageEvent(eventName, package));
        }

        private void DrainPackageEvents(double timestamp)
        {
            while (pendingPackageEvents.Count > 0)
            {
                PendingPackageEvent queued = pendingPackageEvents.Dequeue();
                AppendPackageEvent(timestamp, queued.EventName, queued.Package);
            }
        }

        private void AppendPackageEvent(double timestamp, string eventName, Package package)
        {
            if (package == null) return;
            EnsureTrialStarted(timestamp);
            Record(eventName, package, timestamp);
            // The payload type name already identifies the visualisation
            // (PathAssistancePackage, EnvelopeAssistancePackage, NetworkTimelinePackage).
            string value = package.Payload?.GetType().Name ?? "null";
            if (package.Payload is ViewFramePackage view)
                value += $";sequence={view.Sequence};sampled_at={view.SampledAt.ToString("F6", CultureInfo.InvariantCulture)}";
            AppendEvent(
                timestamp,
                eventName,
                package.StreamId,
                package.SourceTimestampSeconds,
                value,
                string.Empty);
        }

        private void AppendConfigurationSnapshot(double timestamp)
        {
            AppendEvent(timestamp, "configuration", "runtime", timestamp,
                $"unity={Application.unityVersion};application={Application.version};kernel_hz={Agent.UpdateRate}",
                "version,Hz");
            string communicationValue = communication
                ? FormattableString.Invariant(
                    $"uplink_ms={communication.uplinkDelayMilliseconds:F3};downlink_ms={communication.downlinkDelayMilliseconds:F3};temporal_form={communication.temporalForm};degradation={communication.nonDelayDegradation}")
                : "missing";
            AppendEvent(timestamp, "configuration", "communication", timestamp,
                communicationValue, "ms");

            if (taskConfiguration)
                AppendEvent(timestamp, "configuration", "task", timestamp,
                    FormattableString.Invariant(
                        $"target_x={taskConfiguration.targetCenter.x:F6};target_y={taskConfiguration.targetCenter.y:F6};target_z={taskConfiguration.targetCenter.z:F6};radius={taskConfiguration.targetRadius:F6};limit_s={taskConfiguration.timeLimitSeconds:F3};shape={taskConfiguration.targetShape}"),
                    "m,s");
            if (vehicleConfiguration)
                AppendEvent(timestamp, "configuration", "vehicle", timestamp,
                    FormattableString.Invariant(
                        $"wheelbase={vehicleConfiguration.wheelbase:F6};max_velocity={vehicleConfiguration.maxLinearVelocity:F6};acceleration={vehicleConfiguration.maxLinearAcceleration:F6};max_angular={vehicleConfiguration.maxAngularVelocity:F6};max_wheel_difference={vehicleConfiguration.maxWheelVelocityDifference:F6};roughness={vehicleConfiguration.terrainRoughness:F6};slip={vehicleConfiguration.wheelSlipFactor:F6};motor_variation={vehicleConfiguration.motorResponseVariation:F6};radius_variation={vehicleConfiguration.wheelRadiusVariation:F6};encoder_noise={vehicleConfiguration.encoderNoise:F6};vibration_intensity={vehicleConfiguration.vibrationIntensity:F6};vibration_frequency={vehicleConfiguration.vibrationFrequency:F6};surface_offset={vehicleConfiguration.surfaceOffset:F6};tilt_response={vehicleConfiguration.tiltResponsiveness:F6};height_smoothing={vehicleConfiguration.heightSmoothTime:F6};seed={vehicleConfiguration.deterministicSeed}"),
                    "mixed");
            if (inputProvider)
                AppendEvent(timestamp, "configuration", "input-provider", timestamp,
                    $"type={inputProvider.GetType().Name};declared_poll_hz={inputProvider.DeclaredPollRateHz}",
                    "Hz");
            if (commandMapping)
                AppendEvent(timestamp, "configuration", "command-mapping", timestamp,
                    FormattableString.Invariant(
                        $"type={commandMapping.GetType().Name};forward_velocity={commandMapping.RequestedForwardVelocity:F6};turn_velocity={commandMapping.RequestedTurnWheelVelocity:F6};inner_wheel_scale={commandMapping.InnerWheelScaleWhileDriving:F6}"),
                    "m/s,ratio");
            SlopeBoundaryGuard guard = GetComponent<SlopeBoundaryGuard>();
            if (guard)
                AppendEvent(timestamp, "configuration", "remote-assistance", timestamp,
                    FormattableString.Invariant(
                        $"type={guard.GetType().Name};maximum_slope_degrees={guard.MaximumSlopeDegrees:F6}"),
                    "deg");
            TurtleBot3WafflePiEnhanced turtleBot =
                vehicle as TurtleBot3WafflePiEnhanced;
            if (turtleBot)
                AppendEvent(timestamp, "configuration", "remote-view", timestamp,
                    FormattableString.Invariant(
                        $"width={turtleBot.CameraCaptureWidth};height={turtleBot.CameraCaptureHeight};capture_hz={turtleBot.CameraCaptureRateHz:F3};rigid_to_chassis={Bool(turtleBot.IsCameraRigidToChassis)}"),
                    "px,Hz,boolean");
        }

        private void EnsureTrialStarted(double timestamp)
        {
            if (trialStarted) return;
            trialStarted = true;
            Record(
                "trial-start",
                assistance?.GetType().Name ?? "none",
                timestamp);
            AppendEvent(
                timestamp,
                "trial-start",
                string.Empty,
                timestamp,
                $"id={Agent.ID};seed={Agent.RandomSeed};condition={assistance?.GetType().Name ?? "none"}",
                string.Empty);
            AppendConfigurationSnapshot(timestamp);
            FlushCsv();
            nextFlushAt = timestamp + Mathf.Max(0.1f, flushIntervalSeconds);
        }

        private void AppendEvent(
            double timestamp,
            string eventName,
            string stream,
            double sourceTimestamp,
            string value,
            string unit)
        {
            csv.Append(timestamp.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(eventName)).Append(',')
                .Append(Escape(stream)).Append(',')
                .Append(sourceTimestamp.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(value)).Append(',')
                .Append(Escape(unit)).Append('\n');
        }

        private void FlushCsv()
        {
            if (!writeCsvOnTrialEnd || string.IsNullOrEmpty(outputPath) || csv.Length == 0)
                return;
            File.AppendAllText(outputPath, csv.ToString());
            csv.Clear();
        }

        private void OnDestroy()
        {
            FlushCsv();
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (inputProvider) inputProvider.OutputProduced -= CaptureRawInput;
            if (commandMapping) commandMapping.OutputProduced -= CaptureMappedCommand;
            if (uplink) uplink.PackageProduced -= CaptureUplinkRelease;
            if (downlink) downlink.PackageProduced -= CaptureDownlinkRelease;
            if (assistance) assistance.PackageProduced -= CaptureAssistanceOutput;
            assistance = null;
        }

        private static int Bool(bool value) => value ? 1 : 0;

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "participant";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }

        private static string Escape(string value)
        {
            value = value ?? string.Empty;
            return '"' + value.Replace("\"", "\"\"") + '"';
        }
    }
}
