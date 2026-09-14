#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unite.Kernel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unite.Demo.EveryMoveYouMake.Editor
{
    /// <summary>
    /// Creates four byte-for-byte-equivalent scene configurations from the configured open
    /// baseline. The assistance component and its TeleroboticsAgent reference are the only edits.
    /// </summary>
    public static class EveryMoveConditionSceneBuilder
    {
        private const string OutputFolder =
            "Assets/UNITE/Demo/every-move-you-make/Scenes";
        private const string ConfigurationFolder =
            "Assets/UNITE/Demo/every-move-you-make/Configurations";

        private struct Condition
        {
            public string Name;
            public Type AssistanceType;
            public Condition(string name, Type assistanceType)
            { Name = name; AssistanceType = assistanceType; }
        }

        // Every concrete EveryMoveAssistanceBase subclass becomes a condition scene, so a
        // new visualisation is added by writing a decorated subclass — this list is never
        // edited. The scene name comes from [EveryMoveCondition], defaulting to the type name.
        private static List<Condition> DiscoverConditions()
        {
            var conditions = new List<Condition>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<EveryMoveAssistanceBase>())
            {
                if (type.IsAbstract)
                    continue;
                var attribute = (EveryMoveConditionAttribute)Attribute.GetCustomAttribute(
                    type, typeof(EveryMoveConditionAttribute));
                string name = attribute != null
                    ? attribute.SceneName
                    : "condition-" + type.Name;
                conditions.Add(new Condition(name, type));
            }
            return conditions;
        }

        [MenuItem("UNITE/Every Move You Make/Generate Four Condition Scenes")]
        public static void Generate()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.path))
                throw new InvalidOperationException("Save the configured baseline scene before generating conditions.");

            EnsureFolder(OutputFolder);
            string baselinePath = $"{OutputFolder}/condition-baseline.unity";

            var agent = UnityEngine.Object.FindFirstObjectByType<TeleroboticsAgent>();
            if (!agent)
                throw new InvalidOperationException("The open scene requires one TeleroboticsAgent.");

            ResolveSharedReferences(
                out UnityEngine.Object uplink,
                out UnityEngine.Object vehicle,
                out UnityEngine.Object communication);
            EnsureCompletePipeline(
                agent,
                (UplinkCommunicationModule)uplink,
                (EveryMoveVehicleConfiguration)vehicle,
                (EveryMoveCommunicationConfiguration)communication);
            ConfigureAssistance(
                activeScene,
                typeof(NoAssistance),
                vehicle,
                communication);
            EditorSceneManager.SaveScene(
                activeScene,
                baselinePath,
                !string.Equals(activeScene.path, baselinePath, StringComparison.Ordinal));

            // Baseline (NoAssistance) is the sole source of truth. Each other discovered
            // condition starts as an exact serialized copy; only its downlink assistance
            // component is replaced.
            List<Condition> conditions = DiscoverConditions();
            foreach (Condition condition in conditions)
            {
                if (condition.AssistanceType == typeof(NoAssistance))
                    continue;
                Scene baseline = EditorSceneManager.OpenScene(baselinePath, OpenSceneMode.Single);
                string target = $"{OutputFolder}/{condition.Name}.unity";
                EditorSceneManager.SaveScene(baseline, target, true);
                Scene working = EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
                ConfigureAssistance(working, condition.AssistanceType, vehicle, communication);
                EditorSceneManager.SaveScene(working);
            }

            EditorSceneManager.OpenScene(baselinePath, OpenSceneMode.Single);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"Generated {conditions.Count} Every Move You Make condition scenes. " +
                "Only the assistance implementation differs.");
        }

        private static void ResolveSharedReferences(
            out UnityEngine.Object uplink,
            out UnityEngine.Object vehicle,
            out UnityEngine.Object communication)
        {
            TeleroboticsAgent agent =
                UnityEngine.Object.FindFirstObjectByType<TeleroboticsAgent>();
            if (!agent)
                throw new InvalidOperationException(
                    "The configured source scene requires a TeleroboticsAgent.");

            uplink = null;
            vehicle = null;
            communication = null;
            EveryMoveAssistanceBase seed =
                UnityEngine.Object.FindFirstObjectByType<EveryMoveAssistanceBase>();
            if (seed)
            {
                var seedSerialized = new SerializedObject(seed);
                uplink = seedSerialized.FindProperty("uplink").objectReferenceValue;
                vehicle = seedSerialized.FindProperty("vehicleConfiguration").objectReferenceValue;
                communication = seedSerialized.FindProperty("communicationConfiguration").objectReferenceValue;
            }

            if (!uplink)
            {
                uplink = UnityEngine.Object.FindFirstObjectByType<UplinkCommunicationModule>();
                if (!uplink)
                    uplink = agent.gameObject.AddComponent<UplinkCommunicationModule>();
            }

            TurtleBot3WafflePiEnhanced vehicleModel =
                UnityEngine.Object.FindFirstObjectByType<TurtleBot3WafflePiEnhanced>();
            if (!vehicleModel)
                vehicleModel = agent.gameObject.AddComponent<TurtleBot3WafflePiEnhanced>();

            if (!vehicle)
            {
                var vehicleSerialized = new SerializedObject(vehicleModel);
                vehicle = vehicleSerialized.FindProperty("configuration").objectReferenceValue;
                if (!vehicle)
                {
                    vehicle = GetOrCreateConfigurationAsset<EveryMoveVehicleConfiguration>(
                        "TurtleBot3-WafflePi-Moon");
                    vehicleSerialized.FindProperty("configuration").objectReferenceValue = vehicle;
                    vehicleSerialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            if (!communication)
                communication = FindSingleConfigurationAsset<EveryMoveCommunicationConfiguration>();
            if (!communication)
                communication = GetOrCreateConfigurationAsset<EveryMoveCommunicationConfiguration>(
                    "Fixed-Uplink-2560ms");

            EditorUtility.SetDirty(agent);
            EditorUtility.SetDirty(vehicleModel);
            EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
        }

        private static void EnsureCompletePipeline(
            TeleroboticsAgent agent,
            UplinkCommunicationModule uplink,
            EveryMoveVehicleConfiguration vehicleConfiguration,
            EveryMoveCommunicationConfiguration communicationConfiguration)
        {
            GameObject host = agent.gameObject;
            GameObject robotObject = FindSceneObject("TurtleBot3");
            GameObject terrainObject = FindSceneObject("MoonSurfaceTest");
            MeshCollider terrain = terrainObject
                ? terrainObject.GetComponentInChildren<MeshCollider>(true)
                : null;
            if (!robotObject || !terrainObject || !terrain)
                throw new InvalidOperationException(
                    "The source scene requires TurtleBot3 and MoonSurfaceTest, with a MeshCollider on MoonSurfaceTest.");

            Camera robotCamera = robotObject.GetComponentInChildren<Camera>(true);
            if (!robotCamera)
                throw new InvalidOperationException(
                    "TurtleBot3 requires the study camera used for the operator view.");
            ConfigureCameraRoles(robotCamera);

            ConfigureStudyLighting();

            KeyboardArrowProvider input = GetOrAdd<KeyboardArrowProvider>(host);
            ArrowToWheelVelocityMapping mapping = GetOrAdd<ArrowToWheelVelocityMapping>(host);
            SlopeBoundaryGuard guard = GetOrAdd<SlopeBoundaryGuard>(host);
            TurtleBot3WafflePiEnhanced vehicle = GetOrAdd<TurtleBot3WafflePiEnhanced>(host);
            RemoteObservationAndStateCapture observation = GetOrAdd<RemoteObservationAndStateCapture>(host);
            DownlinkCommunicationKernel downlink = GetOrAdd<DownlinkCommunicationKernel>(host);
            EveryMoveStateReconstruction reconstruction =
                GetOrAdd<EveryMoveStateReconstruction>(host);
            RoverViewPresentation presentation = GetOrAdd<RoverViewPresentation>(host);
            TaskGoalAndTerminationModule task = GetOrAdd<TaskGoalAndTerminationModule>(host);
            DataCaptureAndLoggingModule data = GetOrAdd<DataCaptureAndLoggingModule>(host);

            SetField(input, "declaredPollRateHz", 50);
            SetField(mapping, "outputStreamId", EveryMoveStreams.Command);
            SetField(guard, "terrain", terrain);
            SetField(guard, "vehicle", robotObject.transform);
            SetField(guard, "maximumSlopeDegrees", 21.80141f);
            SetField(vehicle, "configuration", vehicleConfiguration);
            SetField(vehicle, "robot", robotObject.transform);
            SetField(vehicle, "cameraSensor", robotCamera);
            SetField(vehicle, "cameraCaptureWidth", 960);
            SetField(vehicle, "cameraCaptureHeight", 540);
            SetField(vehicle, "cameraCaptureRateHz", 20f);
            SetField(vehicle, "terrain", terrain);

            ConfigureUplinkChannels(uplink, communicationConfiguration);
            ConfigureDownlinkChannels(downlink, communicationConfiguration);

            PoseSource poseSource = GetOrAdd<PoseSource>(host);
            RobotStateSource stateSource = GetOrAdd<RobotStateSource>(host);
            RobotViewSource viewSource = GetOrAdd<RobotViewSource>(host);
            SetField(poseSource, "streamId", EveryMoveStreams.Pose);
            SetField(poseSource, "vehicle", vehicle);
            SetField(stateSource, "streamId", EveryMoveStreams.State);
            SetField(stateSource, "vehicle", vehicle);
            SetField(stateSource, "publishToDownlink", false);
            SetField(viewSource, "streamId", EveryMoveStreams.View);
            SetField(viewSource, "vehicle", vehicle);
            SetField(observation, "sources", new RemoteObservationSource[] { poseSource, stateSource, viewSource });

            Material centreMaterial = LoadStudyMaterial("center.mat");
            Material wheelMaterial = LoadStudyMaterial("wheel line.mat");
            Material regionMaterial = LoadStudyMaterial("region.mat");
            MeshFilter centre = EnsureMeshTrace(host.transform, "CentreTrace", centreMaterial);
            MeshFilter left = EnsureMeshTrace(host.transform, "LeftTrace", wheelMaterial);
            MeshFilter right = EnsureMeshTrace(host.transform, "RightTrace", wheelMaterial);
            MeshFilter region = EnsureMeshTrace(host.transform, "UncertaintyRegion", regionMaterial);
            int predictionLayer = LayerMask.NameToLayer("OperatorPrediction");
            robotCamera.cullingMask &= ~(1 << predictionLayer);

            // The timeline is authored with the study coordinates and moved to a scaled
            // screen-space overlay by RoverViewPresentation at runtime.
            GameObject timeline = BuildNetworkTimeline(robotObject.transform);
            Transform staleTimeline = host.transform.Find("NetworkTimeline");
            if (staleTimeline && staleTimeline.gameObject != timeline)
                UnityEngine.Object.DestroyImmediate(staleTimeline.gameObject);

            // Arrow labels sit immediately before the four pipeline locations serialized in
            // the study scene. RoverViewPresentation creates the corresponding tracks and
            // start/end anchors at runtime.
            Sprite upInactive = LoadStudySprite("input-arrow.png");
            Sprite downInactive = LoadStudySprite("input-arrow-down.png");
            Sprite leftInactive = LoadStudySprite("input-arrow-left.png");
            Sprite rightInactive = LoadStudySprite("input-arrow-right.png");
            Sprite endpointSprite = LoadStudySprite("key-arrow.png");
            Image arrowUp = EnsureArrowImage(timeline.transform, "ArrowUp", upInactive, new Vector2(-110f, -112f));
            Image arrowDown = EnsureArrowImage(timeline.transform, "ArrowDown", downInactive, new Vector2(-110f, -144f));
            Image arrowLeft = EnsureArrowImage(timeline.transform, "ArrowLeft", leftInactive, new Vector2(-292f, -128f));
            Image arrowRight = EnsureArrowImage(timeline.transform, "ArrowRight", rightInactive, new Vector2(72f, -128f));
            timeline.SetActive(false);

            // Each visualisation is a standalone presentation source. The host routes
            // packages to them by payload type, so all three live in every scene and only
            // the one matching the scene's assistance module ever renders. Adding a new
            // visualisation means adding a new source here (and its assistance subclass);
            // the host and the other visualisations are untouched.
            PathRibbonVisualisation pathViz = GetOrAdd<PathRibbonVisualisation>(host);
            EnvelopeRegionVisualisation envelopeViz = GetOrAdd<EnvelopeRegionVisualisation>(host);
            NetworkTimelineVisualisation networkViz = GetOrAdd<NetworkTimelineVisualisation>(host);

            SetField(pathViz, "sourceId", "path-ribbon");
            SetField(pathViz, "centreTrace", centre);
            SetField(pathViz, "leftTrace", left);
            SetField(pathViz, "rightTrace", right);
            SetField(pathViz, "terrain", terrain);
            SetField(pathViz, "trajectoryRefreshRateHz", 20f);

            SetField(envelopeViz, "sourceId", "envelope-region");
            SetField(envelopeViz, "uncertaintyRegion", region);
            SetField(envelopeViz, "terrain", terrain);
            SetField(envelopeViz, "trajectoryRefreshRateHz", 20f);

            SetField(networkViz, "sourceId", "network-timeline");
            SetField(networkViz, "networkTimelineRoot", timeline);
            SetField(networkViz, "arrowImages",
                new Image[] { arrowUp, arrowDown, arrowLeft, arrowRight });
            SetField(networkViz, "timelineEndpointSprite", endpointSprite);
            SetField(networkViz, "commandDelaySeconds",
                (communicationConfiguration.uplinkDelayMilliseconds +
                 communicationConfiguration.downlinkDelayMilliseconds) / 1000f);
            SetField(networkViz, "maximumTimelineNodes", 600);

            SetField(presentation, "stateReconstruction", reconstruction);
            SetField(presentation, "sources", new OperatorPresentationSource[]
            {
                pathViz, envelopeViz, networkViz
            });

            EveryMoveTaskConfiguration taskConfiguration =
                GetOrCreateConfigurationAsset<EveryMoveTaskConfiguration>("Lunar-Target-300s");
            // Visible goal is authored condition-scene content. Regenerate the scenes after
            // editing its configuration; the task criterion does not own or mutate this visual.
            Material targetMaterial = GetOrCreateTargetMaterial(regionMaterial);
            BuildTargetMarker(host.transform, taskConfiguration, terrain, targetMaterial);
            TargetRegionEntry target = GetOrAdd<TargetRegionEntry>(host);
            ElapsedTimeLimit timeout = GetOrAdd<ElapsedTimeLimit>(host);
            SetField(target, "authoritativeRobot", robotObject.transform);
            SetField(target, "configuration", taskConfiguration);
            SetField(target, "detectionTolerance", 0.15f);
            SetField(timeout, "configuration", taskConfiguration);
            SetField(task, "criteria", new TaskTerminationCriterion[] { target, timeout });

            TrialLogger logger = GetOrAdd<TrialLogger>(host);
            SetField(logger, "inputProvider", input);
            SetField(logger, "commandMapping", mapping);
            SetField(logger, "uplink", uplink);
            SetField(logger, "downlink", downlink);
            SetField(logger, "vehicle", vehicle);
            SetField(logger, "communication", communicationConfiguration);
            SetField(logger, "vehicleConfiguration", vehicleConfiguration);
            SetField(logger, "taskConfiguration", taskConfiguration);
            SetField(data, "captureImplementations", new DataCaptureImplementation[] { logger });
            SetField(data, "metricDefinitions", EveryMoveMetricDefinitions.Create());

            var agentSerialized = new SerializedObject(agent);
            agentSerialized.FindProperty("updateRate").intValue = 50;
            agentSerialized.FindProperty("inputProvider").objectReferenceValue = input;
            agentSerialized.FindProperty("commandMappingAndEncoding").objectReferenceValue = mapping;
            agentSerialized.FindProperty("operatorSideAssistance").objectReferenceValue = null;
            agentSerialized.FindProperty("uplinkCommunication").objectReferenceValue = uplink;
            agentSerialized.FindProperty("uplinkRemoteSideAssistance").objectReferenceValue = guard;
            agentSerialized.FindProperty("vehicleRobotModel").objectReferenceValue = vehicle;
            agentSerialized.FindProperty("remoteObservationAndStateCapture").objectReferenceValue = observation;
            agentSerialized.FindProperty("downlinkCommunication").objectReferenceValue = downlink;
            agentSerialized.FindProperty("operatorSideStateReconstruction").objectReferenceValue = reconstruction;
            agentSerialized.FindProperty("operatorPresentation").objectReferenceValue = presentation;
            agentSerialized.FindProperty("taskGoalAndTermination").objectReferenceValue = task;
            agentSerialized.FindProperty("dataCaptureAndLogging").objectReferenceValue = data;
            agentSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(agent);
            EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
        }

        private static void ConfigureCameraRoles(Camera studyCamera)
        {
            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Camera outputCamera = null;
            foreach (Camera camera in cameras)
            {
                if (!camera || camera == studyCamera) continue;
                if (camera.CompareTag("MainCamera"))
                {
                    outputCamera = camera;
                    break;
                }
            }

            foreach (Camera camera in cameras)
            {
                if (!camera || camera == studyCamera) continue;
                bool isOutputCamera = camera == outputCamera;
                camera.enabled = isOutputCamera;
                if (isOutputCamera)
                {
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.cullingMask = 0;
                    camera.targetTexture = null;
                    camera.targetDisplay = 0;
                    camera.depth = -100f;
                    camera.allowHDR = false;
                    camera.allowMSAA = false;
                }
                AudioListener listener = camera.GetComponent<AudioListener>();
                if (listener) listener.enabled = false;
                EditorUtility.SetDirty(camera);
                if (listener) EditorUtility.SetDirty(listener);
            }

            studyCamera.enabled = true;
            AudioListener studyListener = studyCamera.GetComponent<AudioListener>();
            if (studyListener) studyListener.enabled = true;
            EditorUtility.SetDirty(studyCamera);
            if (studyListener) EditorUtility.SetDirty(studyListener);
        }

        private static void ConfigureStudyLighting()
        {
            GameObject lightObject = GameObject.Find("Light");
            if (!lightObject)
                lightObject = GameObject.Find("Directional Light");
            if (!lightObject)
                lightObject = new GameObject("Light");
            lightObject.name = "Light";

            Transform transform = lightObject.transform;
            transform.position = new Vector3(11.170045f, 20.060133f, -14.355915f);
            transform.rotation = new Quaternion(
                -0.22319584f,
                0.32387382f,
                -0.88775796f,
                -0.2391135f);
            transform.localScale = new Vector3(100.000046f, 100.00003f, 100f);

            Light studyLight = GetOrAdd<Light>(lightObject);
            studyLight.type = LightType.Directional;
            studyLight.lightmapBakeType = LightmapBakeType.Realtime;
            studyLight.color = new Color(0.6037736f, 0.6037736f, 0.6037736f, 1f);
            studyLight.intensity = 0.5f;
            studyLight.bounceIntensity = 0.2f;
            studyLight.shadows = LightShadows.Soft;
            studyLight.shadowStrength = 0.2f;
            studyLight.shadowBias = 0.05f;
            studyLight.shadowNormalBias = 0.4f;
            studyLight.shadowNearPlane = 0.2f;
            studyLight.cullingMask = ~0;
            studyLight.renderingLayerMask = 1;

            Material studySkybox = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/UNITE/Demo/every-move-you-make/Art/Materials/Skybox.mat");
            if (!studySkybox)
                throw new InvalidOperationException(
                    "The original study Skybox.mat is missing from the Every Move assets.");
            RenderSettings.skybox = studySkybox;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientSkyColor = new Color(0.212f, 0.227f, 0.259f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.114f, 0.125f, 0.133f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.047f, 0.043f, 0.035f, 1f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.subtractiveShadowColor = new Color(
                0.129361f, 0.13813922f, 0.16037738f, 1f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.reflectionBounces = 1;
            RenderSettings.sun = null;
            foreach (Light other in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (other != studyLight && other.type == LightType.Directional)
                {
                    other.enabled = false;
                    EditorUtility.SetDirty(other);
                }
            }
            EditorUtility.SetDirty(lightObject);
            EditorUtility.SetDirty(studyLight);
        }

        private static void ConfigureUplinkChannels(
            UplinkCommunicationModule module,
            EveryMoveCommunicationConfiguration configuration)
        {
            var condition = new EveryMoveFixedDelayCondition();
            SetField(condition, "configuration", configuration);
            SetField(condition, "useConfiguredDownlinkDelay", false);
            SetField(condition, "channelDelayOverrideMilliseconds", -1f);
            var channel = new UplinkCommunicationChannel();
            SetField(channel, "streamId", EveryMoveStreams.Command);
            SetField(channel, "condition", condition);
            SetField(module, "channels", new List<UplinkCommunicationChannel> { channel });
        }

        private static void ConfigureDownlinkChannels(
            DownlinkCommunicationKernel module,
            EveryMoveCommunicationConfiguration configuration)
        {
            var channels = new List<DownlinkCommunicationChannel>();
            foreach (string stream in new[] { EveryMoveStreams.Pose, EveryMoveStreams.View })
            {
                var condition = new EveryMoveFixedDelayCondition();
                SetField(condition, "configuration", configuration);
                SetField(condition, "useConfiguredDownlinkDelay", true);
                SetField(condition, "channelDelayOverrideMilliseconds", -1f);
                var channel = new DownlinkCommunicationChannel();
                SetField(channel, "streamId", stream);
                SetField(channel, "condition", condition);
                channels.Add(channel);
            }
            SetField(module, "channels", channels);
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component ? component : gameObject.AddComponent<T>();
        }

        private static GameObject FindSceneObject(string objectName)
        {
            Scene active = SceneManager.GetActiveScene();
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate.scene == active && candidate.name == objectName &&
                    (candidate.hideFlags & HideFlags.HideAndDontSave) == 0)
                    return candidate;
            }
            return null;
        }

        private static GameObject EnsureChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing) return existing.gameObject;
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static MeshFilter EnsureMeshTrace(Transform parent, string name, Material material)
        {
            GameObject child = EnsureChild(parent, name);
            int predictionLayer = LayerMask.NameToLayer("OperatorPrediction");
            if (predictionLayer < 0)
                throw new InvalidOperationException(
                    "ProjectSettings requires the OperatorPrediction Unity layer.");
            child.layer = predictionLayer;
            child.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            child.transform.localScale = Vector3.one;
            // Earlier reconstructions drew traces with a LineRenderer; remove any stale one so
            // it cannot render a stray line beside the new terrain-projected mesh.
            LineRenderer staleLine = child.GetComponent<LineRenderer>();
            if (staleLine)
                UnityEngine.Object.DestroyImmediate(staleLine);
            MeshFilter filter = GetOrAdd<MeshFilter>(child);
            MeshRenderer renderer = GetOrAdd<MeshRenderer>(child);
            if (material)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return filter;
        }

        private static GameObject BuildNetworkTimeline(Transform parent)
        {
            GameObject timeline = EnsureChild(parent, "NetworkTimeline");
            Canvas canvas = GetOrAdd<Canvas>(timeline);
            canvas.renderMode = RenderMode.WorldSpace;
            GetOrAdd<CanvasScaler>(timeline);
            RectTransform rect = timeline.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1920f, 1080f);
            rect.localScale = Vector3.one * 0.00026544597f;

            Camera robotCamera = parent.GetComponentInChildren<Camera>(true);
            if (robotCamera)
            {
                rect.localRotation = Quaternion.Inverse(parent.rotation) *
                                     robotCamera.transform.rotation;
                rect.localPosition = parent.InverseTransformPoint(
                    robotCamera.transform.position + robotCamera.transform.forward * 0.4f);
                canvas.worldCamera = robotCamera;
                canvas.planeDistance = 0.4f;
                canvas.pixelPerfect = true;
            }
            return timeline;
        }

        private static Image EnsureArrowImage(
            Transform parent, string name, Sprite sprite, Vector2 anchoredPosition)
        {
            GameObject child = EnsureChild(parent, name);
            Image image = GetOrAdd<Image>(child);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(24f, 24f);
            rect.anchoredPosition = anchoredPosition;
            return image;
        }

        private static Sprite LoadStudySprite(string fileName)
        {
            string path = $"Assets/UNITE/Demo/every-move-you-make/Art/{fileName}";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sprite)
                throw new InvalidOperationException(
                    $"The original study arrow sprite '{path}' is missing or not imported as a Sprite.");
            return sprite;
        }

        private static Material LoadStudyMaterial(string fileName)
        {
            string path = $"Assets/UNITE/Demo/every-move-you-make/Art/Materials/{fileName}";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
                throw new InvalidOperationException(
                    $"The original study trace material '{path}' is missing.");
            return material;
        }

        // A translucent green goal material, derived from the study region material so it uses
        // the same working URP transparent setup, distinct from the tan uncertainty region.
        private static Material GetOrCreateTargetMaterial(Material regionMaterial)
        {
            const string path =
                "Assets/UNITE/Demo/every-move-you-make/Art/Materials/Target.mat";
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Color goalColour = new Color(0.2971015f, 1f, 0f, 1f);
            if (existing)
            {
                if (unlit) existing.shader = unlit;
                if (existing.HasProperty("_BaseColor")) existing.SetColor("_BaseColor", goalColour);
                if (existing.HasProperty("_Color")) existing.SetColor("_Color", goalColour);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Material target = unlit
                ? new Material(unlit) { name = "Target" }
                : new Material(regionMaterial) { name = "Target" };
            if (target.HasProperty("_BaseColor")) target.SetColor("_BaseColor", goalColour);
            if (target.HasProperty("_Color")) target.SetColor("_Color", goalColour);
            AssetDatabase.CreateAsset(target, path);
            AssetDatabase.SaveAssets();
            return target;
        }

        private static MeshFilter BuildTargetMarker(
            Transform parent,
            EveryMoveTaskConfiguration taskConfiguration,
            MeshCollider terrain,
            Material material)
        {
            GameObject marker = EnsureChild(parent, "TargetMarker");
            marker.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            marker.transform.localScale = Vector3.one;
            MeshFilter filter = GetOrAdd<MeshFilter>(marker);
            MeshRenderer renderer = GetOrAdd<MeshRenderer>(marker);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Mesh mesh = filter.sharedMesh;
            if (!mesh)
            {
                mesh = new Mesh { name = "TargetDisc" };
                filter.sharedMesh = mesh;
            }
            BuildTerrainTargetDisc(
                taskConfiguration ? taskConfiguration.targetCenter : Vector3.zero,
                taskConfiguration ? taskConfiguration.targetRadius : 1f,
                terrain,
                mesh);
            return filter;
        }

        private static void BuildTerrainTargetDisc(
            Vector3 center,
            float radius,
            MeshCollider terrain,
            Mesh mesh)
        {
            mesh.Clear();
            const int rings = 8;
            const int segments = 64;
            var vertices = new List<Vector3>(1 + rings * segments)
            {
                ProjectTargetPoint(center, terrain)
            };
            var triangles = new List<int>(segments * 3 + (rings - 1) * segments * 6);

            for (int ring = 1; ring <= rings; ring++)
            {
                float ringRadius = radius * ring / rings;
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = 2f * Mathf.PI * segment / segments;
                    Vector3 point = new Vector3(
                    center.x + Mathf.Cos(angle) * ringRadius,
                    center.y,
                    center.z + Mathf.Sin(angle) * ringRadius);
                    vertices.Add(ProjectTargetPoint(point, terrain));
                }
            }

            for (int segment = 0; segment < segments; segment++)
            {
                int current = 1 + segment;
                int next = 1 + (segment + 1) % segments;
                triangles.Add(0);
                triangles.Add(next);
                triangles.Add(current);
            }

            for (int ring = 2; ring <= rings; ring++)
            {
                int previousStart = 1 + (ring - 2) * segments;
                int currentStart = 1 + (ring - 1) * segments;
                for (int segment = 0; segment < segments; segment++)
                {
                    int nextSegment = (segment + 1) % segments;
                    int previous = previousStart + segment;
                    int previousNext = previousStart + nextSegment;
                    int current = currentStart + segment;
                    int currentNext = currentStart + nextSegment;
                    triangles.Add(previous);
                    triangles.Add(currentNext);
                    triangles.Add(current);
                    triangles.Add(previous);
                    triangles.Add(previousNext);
                    triangles.Add(currentNext);
                }
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static Vector3 ProjectTargetPoint(Vector3 point, MeshCollider terrain)
        {
            if (terrain && terrain.Raycast(
                new Ray(new Vector3(point.x, 1000f, point.z), Vector3.down),
                out RaycastHit hit,
                2000f))
                return hit.point + Vector3.up * 0.025f;
            return point + Vector3.up * 0.025f;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    if (target is UnityEngine.Object unityObject)
                        EditorUtility.SetDirty(unityObject);
                    return;
                }
                type = type.BaseType;
            }
            throw new MissingFieldException(target.GetType().FullName, fieldName);
        }

        private static T GetOrCreateConfigurationAsset<T>(string assetName)
            where T : ScriptableObject
        {
            EnsureFolder(ConfigurationFolder);
            string path = $"{ConfigurationFolder}/{assetName}.asset";
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing)
                return existing;

            T created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssets();
            return created;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static T FindSingleConfigurationAsset<T>() where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets(
                $"t:{typeof(T).Name}",
                new[] { "Assets/UNITE/Demo/every-move-you-make" });
            if (guids.Length == 0)
                return null;
            if (guids.Length > 1)
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) == "Fixed-Uplink-2560ms")
                        return AssetDatabase.LoadAssetAtPath<T>(path);
                }
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void ConfigureAssistance(Scene scene, Type assistanceType,
            UnityEngine.Object vehicle, UnityEngine.Object communication)
        {
            var agent = UnityEngine.Object.FindFirstObjectByType<TeleroboticsAgent>();
            UplinkCommunicationModule sceneUplink =
                UnityEngine.Object.FindFirstObjectByType<UplinkCommunicationModule>();
            if (!sceneUplink)
                throw new InvalidOperationException(
                    $"Scene '{scene.name}' is missing its shared UplinkCommunicationModule.");
            EveryMoveAssistanceBase[] existing =
                UnityEngine.Object.FindObjectsByType<EveryMoveAssistanceBase>(FindObjectsSortMode.None);
            foreach (EveryMoveAssistanceBase item in existing)
                UnityEngine.Object.DestroyImmediate(item);

            var assistance = (EveryMoveAssistanceBase)agent.gameObject.AddComponent(assistanceType);
            var assistanceSerialized = new SerializedObject(assistance);
            assistanceSerialized.FindProperty("uplink").objectReferenceValue = sceneUplink;
            assistanceSerialized.FindProperty("commandMapping").objectReferenceValue =
                UnityEngine.Object.FindFirstObjectByType<ArrowToWheelVelocityMapping>();
            assistanceSerialized.FindProperty("vehicleConfiguration").objectReferenceValue = vehicle;
            assistanceSerialized.FindProperty("communicationConfiguration").objectReferenceValue = communication;
            assistanceSerialized.ApplyModifiedPropertiesWithoutUndo();

            var agentSerialized = new SerializedObject(agent);
            agentSerialized.FindProperty("downlinkOperatorSideAssistance").objectReferenceValue = assistance;
            agentSerialized.ApplyModifiedPropertiesWithoutUndo();
            ValidateCompleteAgent(agent, scene.name);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void ValidateCompleteAgent(TeleroboticsAgent agent, string sceneName)
        {
            var serialized = new SerializedObject(agent);
            string[] required =
            {
                "inputProvider", "commandMappingAndEncoding", "uplinkCommunication",
                "uplinkRemoteSideAssistance", "vehicleRobotModel",
                "remoteObservationAndStateCapture", "downlinkCommunication",
                "operatorSideStateReconstruction",
                "downlinkOperatorSideAssistance", "operatorPresentation",
                "taskGoalAndTermination", "dataCaptureAndLogging"
            };
            foreach (string propertyName in required)
            {
                if (!serialized.FindProperty(propertyName).objectReferenceValue)
                    throw new InvalidOperationException(
                        $"Generated scene '{sceneName}' is incomplete: TeleroboticsAgent.{propertyName} is empty.");
            }

            UplinkCommunicationModule uplink =
                UnityEngine.Object.FindFirstObjectByType<UplinkCommunicationModule>();
            DownlinkCommunicationKernel downlink =
                UnityEngine.Object.FindFirstObjectByType<DownlinkCommunicationKernel>();
            if (new SerializedObject(uplink).FindProperty("channels").arraySize != 1)
                throw new InvalidOperationException(
                    $"Generated scene '{sceneName}' must contain exactly one uplink channel.");
            if (new SerializedObject(downlink).FindProperty("channels").arraySize != 2)
                throw new InvalidOperationException(
                    $"Generated scene '{sceneName}' must contain exactly two downlink channels.");
        }
    }
}
#endif
