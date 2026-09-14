using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Unite.Kernel
{
    /// <summary>
    /// Owns the single fixed-rate runtime loop for all telerobotics modules on
    /// this GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TeleroboticsAgent : MonoBehaviour
    {
        public const string RequiredGameObjectName = "TeleroboticsAgent";

        private static TeleroboticsAgent activeInstance;

        [SerializeField]
        [FormerlySerializedAs("participantID")]
        private string id = "Participant";

        [SerializeField]
        [Min(1)]
        private int updateRate = 50;

        [SerializeField]
        private InputProviderModule inputProvider;

        [SerializeField]
        private CommandMappingAndEncodingModule commandMappingAndEncoding;

        [SerializeField]
        private OperatorSideAssistanceModule operatorSideAssistance;

        [SerializeField]
        private UplinkCommunicationModule uplinkCommunication;

        [SerializeField]
        private UplinkRemoteSideAssistanceModule uplinkRemoteSideAssistance;

        [SerializeField]
        private VehicleRobotModelModule vehicleRobotModel;

        [SerializeField]
        private RemoteObservationAndStateCapture remoteObservationAndStateCapture;

        [SerializeField]
        private DownlinkCommunicationKernel downlinkCommunication;

        [SerializeField]
        private OperatorSideStateReconstructionModule operatorSideStateReconstruction;

        [SerializeField]
        private DownlinkOperatorSideAssistanceModule downlinkOperatorSideAssistance;

        [SerializeField]
        private OperatorPresentationModule operatorPresentation;

        [SerializeField]
        private TaskGoalAndTerminationModule taskGoalAndTermination;

        [SerializeField]
        private DataCaptureAndLoggingModule dataCaptureAndLogging;

        private TeleroboticsModule[] modules;
        private float previousFixedDeltaTime;

        public string ID => id;
        public int RandomSeed => StudyRandom.Seed;

        // Kept as a source-compatible alias for existing study modules.
        public string ParticipantID => ID;
        public int UpdateRate => updateRate;
        public bool IsTrialActive { get; private set; }
        public TrialEndedEvent LastTrialEndedEvent { get; private set; }
        public event Action<TrialEndedEvent> TrialEnded;

        internal InputProviderModule SelectedInputProvider => inputProvider;
        internal CommandMappingAndEncodingModule SelectedCommandMappingAndEncoding =>
            commandMappingAndEncoding;
        internal DownlinkOperatorSideAssistanceModule SelectedDownlinkOperatorSideAssistance =>
            downlinkOperatorSideAssistance;
        internal double CurrentTimestampSeconds { get; private set; }

        private IPackageSource uplinkPackageSource;
        private IPackageSource vehicleModelPackageSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveInstance()
        {
            activeInstance = null;
        }

        private void Reset()
        {
            gameObject.name = RequiredGameObjectName;
        }

        private void OnValidate()
        {
            gameObject.name = RequiredGameObjectName;
            updateRate = Mathf.Max(1, updateRate);

            if (Application.isPlaying && activeInstance == this)
            {
                Time.fixedDeltaTime = 1f / updateRate;
            }
        }

        private void Awake()
        {
            if (activeInstance != null && activeInstance != this)
            {
                Debug.LogError(
                    "Only one active TeleroboticsAgent is allowed. " +
                    $"'{activeInstance.gameObject.name}' already owns the runtime loop.",
                    this);
                enabled = false;
                return;
            }

            activeInstance = this;
            gameObject.name = RequiredGameObjectName;
            updateRate = Mathf.Max(1, updateRate);
            IsTrialActive = true;
            LastTrialEndedEvent = null;
            previousFixedDeltaTime = Time.fixedDeltaTime;

            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogError(
                    "TeleroboticsAgent requires a non-empty ID for deterministic " +
                    "randomization.",
                    this);
                enabled = false;
                return;
            }

            id = id.Trim();
            StudyRandom.Initialize(id);
            Debug.Log(
                $"Study ID '{id}' initialized deterministic seed " +
                $"{StudyRandom.Seed}.",
                this);

            Time.fixedDeltaTime = 1f / updateRate;

            if (inputProvider == null ||
                commandMappingAndEncoding == null ||
                uplinkCommunication == null ||
                vehicleRobotModel == null)
            {
                Debug.LogError(
                    "TeleroboticsAgent requires one Input Provider and one Command " +
                    "Mapping and Encoding module, one Uplink Communication module, " +
                    "and one Vehicle/Robot Model.",
                    this);
                enabled = false;
                return;
            }

            if (inputProvider.gameObject != gameObject ||
                commandMappingAndEncoding.gameObject != gameObject ||
                uplinkCommunication.gameObject != gameObject ||
                vehicleRobotModel.gameObject != gameObject ||
                (operatorSideAssistance != null &&
                 operatorSideAssistance.gameObject != gameObject) ||
                (uplinkRemoteSideAssistance != null &&
                 uplinkRemoteSideAssistance.gameObject != gameObject) ||
                (remoteObservationAndStateCapture != null &&
                 remoteObservationAndStateCapture.gameObject != gameObject) ||
                (downlinkCommunication != null &&
                 downlinkCommunication.gameObject != gameObject) ||
                (operatorSideStateReconstruction != null &&
                 operatorSideStateReconstruction.gameObject != gameObject) ||
                (downlinkOperatorSideAssistance != null &&
                 downlinkOperatorSideAssistance.gameObject != gameObject) ||
                (operatorPresentation != null &&
                 operatorPresentation.gameObject != gameObject) ||
                (taskGoalAndTermination != null &&
                 taskGoalAndTermination.gameObject != gameObject) ||
                (dataCaptureAndLogging != null &&
                 dataCaptureAndLogging.gameObject != gameObject))
            {
                Debug.LogError(
                    "All selected telerobotics modules " +
                    "must be attached to the TeleroboticsAgent GameObject.",
                    this);
                enabled = false;
                return;
            }

            bool hasRemoteFeedbackPipeline =
                remoteObservationAndStateCapture != null ||
                downlinkCommunication != null ||
                operatorSideStateReconstruction != null ||
                downlinkOperatorSideAssistance != null ||
                operatorPresentation != null;

            if (hasRemoteFeedbackPipeline &&
                (remoteObservationAndStateCapture == null ||
                 downlinkCommunication == null ||
                 operatorSideStateReconstruction == null ||
                 operatorPresentation == null))
            {
                Debug.LogError(
                    "Remote Observation and State Capture, Downlink Communication, " +
                    "Operator-side State Reconstruction, and Operator Presentation " +
                    "must either all be assigned or all be absent.",
                    this);
                enabled = false;
                return;
            }

            var selectedModules = new System.Collections.Generic.List<TeleroboticsModule>
            {
                inputProvider,
                commandMappingAndEncoding,
                uplinkCommunication,
                vehicleRobotModel
            };

            if (operatorSideAssistance != null)
            {
                selectedModules.Add(operatorSideAssistance);
            }

            if (uplinkRemoteSideAssistance != null)
            {
                selectedModules.Add(uplinkRemoteSideAssistance);
            }

            if (remoteObservationAndStateCapture != null)
            {
                selectedModules.Add(remoteObservationAndStateCapture);
            }

            if (downlinkCommunication != null)
            {
                selectedModules.Add(downlinkCommunication);
            }

            if (operatorSideStateReconstruction != null)
            {
                selectedModules.Add(operatorSideStateReconstruction);
            }

            if (downlinkOperatorSideAssistance != null)
            {
                selectedModules.Add(downlinkOperatorSideAssistance);
            }

            if (operatorPresentation != null)
            {
                selectedModules.Add(operatorPresentation);
            }

            if (taskGoalAndTermination != null)
            {
                selectedModules.Add(taskGoalAndTermination);
            }

            if (dataCaptureAndLogging != null)
            {
                selectedModules.Add(dataCaptureAndLogging);
            }

            modules = selectedModules.ToArray();
            Array.Sort(
                modules,
                (left, right) => left.ExecutionOrder.CompareTo(right.ExecutionOrder));

            for (int index = 0; index < modules.Length; index++)
            {
                modules[index].Initialize(this);
            }

            IPackageSource mappingSource = commandMappingAndEncoding;

            if (operatorSideAssistance != null)
            {
                mappingSource.PackageProduced +=
                    operatorSideAssistance.ReceivePackage;

                uplinkPackageSource = operatorSideAssistance;
            }
            else
            {
                uplinkPackageSource = mappingSource;
            }

            uplinkPackageSource.PackageProduced += uplinkCommunication.ReceivePackage;

            if (uplinkRemoteSideAssistance != null)
            {
                uplinkCommunication.PackageProduced +=
                    uplinkRemoteSideAssistance.ReceivePackage;

                vehicleModelPackageSource = uplinkRemoteSideAssistance;
            }
            else
            {
                vehicleModelPackageSource = uplinkCommunication;
            }

            vehicleModelPackageSource.PackageProduced +=
                vehicleRobotModel.ReceivePackage;

            if (remoteObservationAndStateCapture != null &&
                uplinkRemoteSideAssistance != null)
            {
                remoteObservationAndStateCapture.LocalObservationProduced +=
                    uplinkRemoteSideAssistance.ReceiveObservation;
            }

            if (remoteObservationAndStateCapture != null &&
                downlinkCommunication != null)
            {
                remoteObservationAndStateCapture.PackageProduced +=
                    downlinkCommunication.ReceivePackage;
            }

            // Received feedback enters 5.9 only. Reconstructed results feed
            // both presentation (5.11) and optional assistance (5.10).
            if (downlinkCommunication != null &&
                operatorSideStateReconstruction != null)
            {
                downlinkCommunication.PackageProduced +=
                    operatorSideStateReconstruction.ReceivePackage;
            }

            if (operatorSideStateReconstruction != null &&
                downlinkOperatorSideAssistance != null)
            {
                operatorSideStateReconstruction.PackageProduced +=
                    downlinkOperatorSideAssistance.ReceivePackage;
            }

            if (operatorSideStateReconstruction != null &&
                operatorPresentation != null)
            {
                operatorSideStateReconstruction.PackageProduced +=
                    operatorPresentation.ReceivePackage;
            }

            if (downlinkOperatorSideAssistance != null &&
                operatorPresentation != null)
            {
                downlinkOperatorSideAssistance.PackageProduced +=
                    operatorPresentation.ReceivePackage;
            }
        }

        private void FixedUpdate()
        {
            if (!IsTrialActive)
            {
                return;
            }

            double timestampSeconds = Time.realtimeSinceStartupAsDouble;
            CurrentTimestampSeconds = timestampSeconds;

            for (int index = 0; index < modules.Length; index++)
            {
                TeleroboticsModule module = modules[index];

                if (module.isActiveAndEnabled)
                {
                    module.Step(timestampSeconds);

                    // Refresh the same observation state after the model update.
                    // This final refresh replaces the pre-control state used by
                    // remote-side assistance and is sent through downlink.
                    if (ReferenceEquals(module, vehicleRobotModel) &&
                        remoteObservationAndStateCapture != null &&
                        remoteObservationAndStateCapture.isActiveAndEnabled)
                    {
                        remoteObservationAndStateCapture.CaptureFinalObservation(
                            timestampSeconds);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (commandMappingAndEncoding != null &&
                operatorSideAssistance != null)
            {
                commandMappingAndEncoding.PackageProduced -=
                    operatorSideAssistance.ReceivePackage;
            }

            if (uplinkPackageSource != null && uplinkCommunication != null)
            {
                uplinkPackageSource.PackageProduced -=
                    uplinkCommunication.ReceivePackage;
            }

            if (uplinkCommunication != null &&
                uplinkRemoteSideAssistance != null)
            {
                uplinkCommunication.PackageProduced -=
                    uplinkRemoteSideAssistance.ReceivePackage;
            }

            if (vehicleModelPackageSource != null && vehicleRobotModel != null)
            {
                vehicleModelPackageSource.PackageProduced -=
                    vehicleRobotModel.ReceivePackage;
            }

            if (remoteObservationAndStateCapture != null &&
                uplinkRemoteSideAssistance != null)
            {
                remoteObservationAndStateCapture.LocalObservationProduced -=
                    uplinkRemoteSideAssistance.ReceiveObservation;
            }

            if (remoteObservationAndStateCapture != null &&
                downlinkCommunication != null)
            {
                remoteObservationAndStateCapture.PackageProduced -=
                    downlinkCommunication.ReceivePackage;
            }

            if (downlinkCommunication != null &&
                operatorSideStateReconstruction != null)
            {
                downlinkCommunication.PackageProduced -=
                    operatorSideStateReconstruction.ReceivePackage;
            }

            if (operatorSideStateReconstruction != null &&
                downlinkOperatorSideAssistance != null)
            {
                operatorSideStateReconstruction.PackageProduced -=
                    downlinkOperatorSideAssistance.ReceivePackage;
            }

            if (operatorSideStateReconstruction != null &&
                operatorPresentation != null)
            {
                operatorSideStateReconstruction.PackageProduced -=
                    operatorPresentation.ReceivePackage;
            }

            if (downlinkOperatorSideAssistance != null &&
                operatorPresentation != null)
            {
                downlinkOperatorSideAssistance.PackageProduced -=
                    operatorPresentation.ReceivePackage;
            }

            if (activeInstance != this)
            {
                return;
            }

            Time.fixedDeltaTime = previousFixedDeltaTime;
            activeInstance = null;
        }

        internal void EndTrial(TrialEndedEvent trialEndedEvent)
        {
            if (!IsTrialActive)
            {
                return;
            }

            IsTrialActive = false;
            LastTrialEndedEvent = trialEndedEvent;
            TrialEnded?.Invoke(trialEndedEvent);
        }

    }
}
