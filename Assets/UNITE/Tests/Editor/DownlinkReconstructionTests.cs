using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unite.Core;
using Unite.Kernel;
using Unite.Demo.EveryMoveYouMake;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Unite.Tests
{
    public class DownlinkReconstructionTests
    {
        private GameObject host;
        private readonly List<Object> resources = new List<Object>();
        private TeleroboticsAgent agent;
        private DownlinkCommunicationKernel downlink;
        private UplinkCommunicationModule uplink;
        private EveryMoveStateReconstruction reconstruction;
        private TestPresentation presentation;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Downlink test");
            host.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            // These edit-mode fixtures initialize an inactive host explicitly;
            // invoke cleanup explicitly too (Unity may omit its destroy message).
            if (agent) DestroyHook(agent);
            if (reconstruction) DestroyHook(reconstruction);
            Object.DestroyImmediate(host);
            foreach (Object resource in resources)
                if (resource) Object.DestroyImmediate(resource);
            resources.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PresentationReceivesReconstructionEvenWhenAssistanceProducesNothing(bool assisted)
        {
            ConfigureAgent(assisted);
            downlink.ReceivePackage(new Package(new PosePackage(State(1), 1), 1, EveryMoveStreams.Pose));
            Step(downlink, .49);
            Step(reconstruction, .49);
            Step(presentation, .49);
            Assert.That(presentation.Received, Is.Empty);

            Step(downlink, .5);
            Step(reconstruction, .5);
            if (assisted) Step(host.GetComponent<TestAssistance>(), .5);
            Step(presentation, .5);
            Assert.That(presentation.Received.Count, Is.EqualTo(1));
            Assert.That(presentation.Received[0].Payload, Is.TypeOf<ReconstructedPose>());
            Assert.That(presentation.Received[0].SourceTimestampSeconds, Is.EqualTo(1));

            // An unknown transport payload must not reach either downstream stage.
            downlink.ReceivePackage(new Package(new byte[] { 1, 2 }, 2, EveryMoveStreams.Pose));
            Step(downlink, 1);
            Step(reconstruction, 1);
            if (assisted) Step(host.GetComponent<TestAssistance>(), 1);
            Step(presentation, 1);
            Assert.That(presentation.Received.Count, Is.EqualTo(1));
            if (assisted) Assert.That(host.GetComponent<TestAssistance>().Count, Is.EqualTo(1));

            // Destroying the orchestrator must remove both presentation subscriptions.
            DestroyHook(agent);
            Object.DestroyImmediate(agent);
            reconstruction.ReceivePackage(new Package(new PosePackage(State(2), 2), 2, EveryMoveStreams.Pose));
            Step(reconstruction, 2);
            Step(presentation, 2);
            Assert.That(presentation.Received.Count, Is.EqualTo(1));
        }

        [Test]
        public void PresentationCombinesBaseAndAssistanceWithoutDuplicatingBase()
        {
            ConfigureAgent(true);
            TestAssistance assistance = host.GetComponent<TestAssistance>();
            assistance.EmitOverlay = true;
            reconstruction.ReceivePackage(new Package(new PosePackage(State(1), 1), 1, EveryMoveStreams.Pose));
            Step(reconstruction, 2);
            Step(assistance, 2);
            Step(presentation, 2);
            Assert.That(presentation.Received.Count, Is.EqualTo(2));
            Assert.That(presentation.Received[0].Payload, Is.TypeOf<ReconstructedPose>());
            Assert.That(presentation.Received[1].Payload, Is.EqualTo("overlay"));
        }

        [Test]
        public void AssistanceRejectsUnreconstructedPayloads()
        {
            TestAssistance assistance = host.AddComponent<TestAssistance>();
            LogAssert.Expect(LogType.Error, "TestAssistance requires a reconstructed feedback package.");
            assistance.ReceivePackage(new Package(new byte[] { 1 }, 1, "video"));
            Step(assistance, 2);
            Assert.That(assistance.Count, Is.Zero);
        }

        [Test]
        public void VideoReconstructionSeparatesMotionValuesAndReleasesReplacedFrames()
        {
            reconstruction = host.AddComponent<EveryMoveStateReconstruction>();
            var output = new List<Package>();
            reconstruction.PackageProduced += output.Add;
            int releases = 0;
            ViewFramePackage first = Frame(1, 10, _ => releases++);
            ViewFramePackage second = Frame(2, 11, _ => releases++);
            reconstruction.ReceivePackage(new Package(first, 3, EveryMoveStreams.View));
            reconstruction.ReceivePackage(new Package(second, 4, EveryMoveStreams.View));
            Step(reconstruction, 5);
            Assert.That(output.Count, Is.EqualTo(2));
            Assert.That(releases, Is.EqualTo(1));
            Assert.That(reconstruction.LatestViewFrame.Frame, Is.SameAs(second.Frame));
            Assert.That(reconstruction.LatestViewFrame.Sequence, Is.EqualTo(11));
            Assert.That(reconstruction.LatestViewFrame.SampledAt, Is.EqualTo(2));
            Assert.That(reconstruction.LatestViewFrame.WorldToCameraMatrix, Is.EqualTo(second.WorldToCameraMatrix));
            var motion = (ReconstructedMotionState)output[1].Payload;
            Assert.That(motion.SampledAt, Is.EqualTo(2));
            Assert.That(motion.ViewSequence, Is.EqualTo(11));
            Assert.That(motion.LeftVelocity, Is.EqualTo(.1f));
            Assert.That(output[1].SourceTimestampSeconds, Is.EqualTo(4));
            Assert.That(output[1].StreamId, Is.EqualTo(EveryMoveStreams.ReconstructedViewState));
            Assert.That(((ReconstructedMotionState)output[0].Payload).SampledAt, Is.EqualTo(1));
            DestroyHook(reconstruction);
            Object.DestroyImmediate(reconstruction);
            Assert.That(releases, Is.EqualTo(2));
        }

        [Test]
        public void InvalidFrameProducesNoOutputOrBufferUpdate()
        {
            reconstruction = host.AddComponent<EveryMoveStateReconstruction>();
            int output = 0, releases = 0;
            reconstruction.PackageProduced += _ => output++;
            var invalid = new ViewFramePackage(null, 1, 1, Matrix4x4.identity,
                Matrix4x4.identity, State(1), _ => releases++);
            reconstruction.ReceivePackage(new Package(invalid, 1, EveryMoveStreams.View));
            Step(reconstruction, 2);
            Assert.That(output, Is.Zero);
            Assert.That(releases, Is.EqualTo(1));
            Assert.That(reconstruction.LatestViewFrame, Is.Null);
        }

        [TestCase(typeof(NoAssistance), 0)]
        [TestCase(typeof(IdealTrajectoryAssistance), 1)]
        [TestCase(typeof(WorstCaseEnvelopeAssistance), 1)]
        public void DemoAssistanceUsesMotionStateWithoutVideo(Type type, int expectedOutputs)
        {
            ConfigureAgent(false);
            var assistance = (DownlinkOperatorSideAssistanceModule)host.AddComponent(type);
            var config = ScriptableObject.CreateInstance<EveryMoveVehicleConfiguration>();
            resources.Add(config);
            Set(assistance, "vehicleConfiguration", config);
            Set(assistance, "uplink", uplink);
            var input = new ArrowInputPackage(1, .02f, true, false, false, false);
            uplink.ReceivePackage(new Package(new WheelVelocityCommand(input, 1, .2f, .2f),
                1, EveryMoveStreams.Command));
            var output = new List<Package>();
            assistance.PackageProduced += output.Add;
            assistance.ReceivePackage(new Package(new ReconstructedMotionState(
                Vector3.zero, 0, .1f, .1f, 1, 1), 1, EveryMoveStreams.ReconstructedViewState));
            Step(assistance, 2);
            Assert.That(output.Count, Is.EqualTo(expectedOutputs));
            if (expectedOutputs == 1)
            {
                Assert.That(output[0].StreamId, Is.EqualTo(EveryMoveStreams.Assistance));
                PredictedPose[] centre = output[0].Payload is PathAssistancePackage path
                    ? path.Centre : ((EnvelopeAssistancePackage)output[0].Payload).Centre;
                Assert.That(centre.Length, Is.EqualTo(1));
                Assert.That(centre[0].position.x, Is.GreaterThan(0));
            }
        }

        [Test]
        public void NetworkTimelineCanPublishWithoutAnyReconstructedInput()
        {
            ConfigureAgent(false);
            var assistance = host.AddComponent<CommandTimelineAssistance>();
            var input = new ArrowInputPackage(1, .02f, true, false, false, false);
            var output = new List<Package>();
            assistance.PackageProduced += output.Add;
            typeof(CommandTimelineAssistanceBase).GetMethod("ReceiveMappedCommand",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(assistance,
                new object[] { new WheelVelocityCommand(input, 1, .2f, .2f) });
            Step(assistance, 1);
            Assert.That(output.Count, Is.EqualTo(1));
            Assert.That(output[0].Payload, Is.TypeOf<NetworkTimelinePackage>());
        }

        private void ConfigureAgent(bool assisted)
        {
            agent = host.AddComponent<TeleroboticsAgent>();
            Set(agent, "inputProvider", host.AddComponent<TestInput>());
            Set(agent, "commandMappingAndEncoding", host.AddComponent<TestMapping>());
            Set(agent, "vehicleRobotModel", host.AddComponent<TestVehicle>());
            uplink = host.AddComponent<UplinkCommunicationModule>();
            var up = new UplinkCommunicationChannel();
            Set(up, "streamId", EveryMoveStreams.Command);
            Set(up, "condition", new TestDelay());
            Set(uplink, "channels", new List<UplinkCommunicationChannel> { up });
            Set(agent, "uplinkCommunication", uplink);
            downlink = host.AddComponent<DownlinkCommunicationKernel>();
            var down = new DownlinkCommunicationChannel();
            Set(down, "streamId", EveryMoveStreams.Pose);
            Set(down, "condition", new TestDelay());
            Set(downlink, "channels", new List<DownlinkCommunicationChannel> { down });
            Set(agent, "downlinkCommunication", downlink);
            var capture = host.AddComponent<RemoteObservationAndStateCapture>();
            Set(capture, "sources", new RemoteObservationSource[] { host.AddComponent<TestObservation>() });
            Set(agent, "remoteObservationAndStateCapture", capture);
            reconstruction = host.AddComponent<EveryMoveStateReconstruction>();
            Set(agent, "operatorSideStateReconstruction", reconstruction);
            presentation = host.AddComponent<TestPresentation>();
            Set(agent, "operatorPresentation", presentation);
            if (assisted) Set(agent, "downlinkOperatorSideAssistance", host.AddComponent<TestAssistance>());
            typeof(TeleroboticsAgent).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(agent, null);
        }

        private ViewFramePackage Frame(double time, long sequence, Action<RenderTexture> release)
        {
            var texture = new RenderTexture(2, 2, 0);
            resources.Add(texture);
            return new ViewFramePackage(texture, time, sequence,
                Matrix4x4.Translate(Vector3.one), Matrix4x4.identity, State(time), release);
        }

        private static TurtleBotState State(double time) =>
            new TurtleBotState(time, Vector3.zero, 0, .1f, .2f, null);

        private static void Step(TeleroboticsModule module, double time) =>
            typeof(TeleroboticsModule).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(module, new object[] { time });

        private static void DestroyHook(Object target) =>
            target.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);

        private static void Set(object target, string name, object value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }
    }

    public sealed class TestPresentation : OperatorPresentationModule
    {
        public readonly List<Package> Received = new List<Package>();
        protected override void PresentPackage(Package package, double time) => Received.Add(package);
    }

    public sealed class TestAssistance : DownlinkOperatorSideAssistanceModule
    {
        public int Count;
        public bool EmitOverlay;
        protected override void ProcessPackage(Package package, double time)
        {
            Count++;
            if (EmitOverlay) PublishPackage(new Package("overlay", time, "assistance"));
        }
    }

    public sealed class TestInput : InputProvider<ArrowInputPackage>
    {
        protected override bool TryCaptureInput(double time, out ArrowInputPackage output)
        { output = null; return false; }
    }

    public sealed class TestMapping : CommandMappingAndEncoding<ArrowInputPackage, WheelVelocityCommand>
    {
        protected override bool TryMapAndEncode(ArrowInputPackage input, double time, out WheelVelocityCommand output)
        { output = null; return false; }
    }

    public sealed class TestVehicle : VehicleRobotModel<WheelVelocityCommand, TurtleBotState>
    {
        protected override void ApplyCommand(WheelVelocityCommand command, double time) { }
        protected override void UpdateVehicleState(double time) { }
    }

    public sealed class TestObservation : RemoteObservationSource<PosePackage>
    {
        protected override bool TryCapture(double time, out PosePackage observation)
        { observation = null; return false; }
    }

    [Serializable]
    public sealed class TestDelay : CommunicationCondition
    {
        public override bool TryGetTransmissionDelay(Package package, double time, out double delay)
        { delay = .5; return true; }
    }
}
