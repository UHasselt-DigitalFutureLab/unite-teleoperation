using System;
using System.Reflection;
using NUnit.Framework;
using Unite.Core;
using Unite.Kernel;

namespace Unite.Tests
{
    // These tests use the real package/channel code without a Unity scene.
    public class PackageLifetimeTests
    {
        public sealed class Resource : IDisposable
        {
            public int Releases;
            public void Dispose() { Releases++; }
        }

        private sealed class Condition : CommunicationCondition
        {
            public bool Deliver = true;
            public override bool TryGetTransmissionDelay(Package package, double time, out double delay)
            { delay = 1; return Deliver; }
        }

        [Test]
        public void PayloadIsReleasedOnceAfterLastOwner()
        {
            var resource = new Resource();
            var package = new Package(resource, 0, "video");
            IDisposable local = package.RetainPayload();
            IDisposable display = package.RetainPayload();
            package.Dispose();
            package.Dispose();
            local.Dispose();
            local.Dispose();
            Assert.That(resource.Releases, Is.Zero);
            display.Dispose();
            display.Dispose();
            Assert.That(resource.Releases, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => package.RetainPayload());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DroppedPackageReleasesOnlyAfterLocalObservationIsDone(bool retainLocal)
        {
            var resource = new Resource();
            var package = new Package(resource, 0, "video");
            IDisposable local = retainLocal ? package.RetainPayload() : null;
            DownlinkCommunicationChannel channel = Channel(false);
            Call(channel, "ReceivePackage", package, 0d, null);
            int delivered = 0;
            Call(channel, "ReleaseDuePackages", 2d, new Action<Package>(_ => delivered++));
            Assert.That(delivered, Is.Zero);
            Assert.That(resource.Releases, Is.EqualTo(retainLocal ? 0 : 1));
            local?.Dispose();
            Assert.That(resource.Releases, Is.EqualTo(1));
        }

        [Test]
        public void DeliveryTransfersOwnershipWithoutReleasingQueuedOrDisplayedPayload()
        {
            var resource = new Resource();
            var package = new Package(resource, 0, "video");
            DownlinkCommunicationChannel channel = Channel(true);
            Call(channel, "ReceivePackage", package, 0d, null);
            Package received = null;
            Action<Package> receive = p => received = p;
            Call(channel, "ReleaseDuePackages", .5d, receive);
            Assert.That(received, Is.Null);
            Assert.That(resource.Releases, Is.Zero);
            Call(channel, "ReleaseDuePackages", 1d, receive);
            Assert.That(received, Is.SameAs(package));
            Call(channel, "Clear");
            Assert.That(resource.Releases, Is.Zero);
            IDisposable display = received.RetainPayload();
            received.Dispose();
            Assert.That(resource.Releases, Is.Zero);
            display.Dispose();
            Assert.That(resource.Releases, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClearingOrReinitializingReleasesUndeliveredPackages(bool reset)
        {
            var resource = new Resource();
            DownlinkCommunicationChannel channel = Channel(true);
            Call(channel, "ReceivePackage", new Package(resource, 0, "video"), 0d, null);
            if (reset) Call(channel, "TryInitialize", new object[] { null });
            else Call(channel, "Clear");
            Call(channel, "Clear");
            Assert.That(resource.Releases, Is.EqualTo(1));
        }

        [Test]
        public void ReleaseWithoutAReceiverDisposesPayload()
        {
            var resource = new Resource();
            DownlinkCommunicationChannel channel = Channel(true);
            Call(channel, "ReceivePackage", new Package(resource, 0, "video"), 0d, null);
            Call(channel, "ReleaseDuePackages", 1d, null);
            Assert.That(resource.Releases, Is.EqualTo(1));
        }

        private static DownlinkCommunicationChannel Channel(bool deliver)
        {
            var channel = new DownlinkCommunicationChannel();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(DownlinkCommunicationChannel).GetField("streamId", flags).SetValue(channel, "video");
            typeof(DownlinkCommunicationChannel).GetField("condition", flags).SetValue(channel,
                new Condition { Deliver = deliver });
            Assert.That(Call(channel, "TryInitialize", new object[] { null }), Is.True);
            return channel;
        }

        private static object Call(object target, string method, params object[] args)
        {
            return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, args);
        }
    }
}
