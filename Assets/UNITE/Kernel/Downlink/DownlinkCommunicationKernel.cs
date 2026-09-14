using System;
using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Routes feedback packages to independently configured downlink channels.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DownlinkCommunicationKernel
        : TeleroboticsModule, IPackageSource, IPackageConsumer
    {
        [SerializeField]
        private List<DownlinkCommunicationChannel> channels =
            new List<DownlinkCommunicationChannel>();

        private readonly Dictionary<string, DownlinkCommunicationChannel>
            channelsByStreamId =
                new Dictionary<string, DownlinkCommunicationChannel>(
                    StringComparer.Ordinal);

        public event Action<Package> PackageProduced;

        internal override int ExecutionOrder => 800;

        public void ReceivePackage(Package package)
        {
            if (package == null)
            {
                Debug.LogError($"{GetType().Name} received a null package.", this);
                return;
            }

            string streamId = string.IsNullOrWhiteSpace(package.StreamId)
                ? string.Empty
                : package.StreamId.Trim();

            if (string.IsNullOrWhiteSpace(streamId))
            {
                Debug.LogError(
                    $"{GetType().Name} received a package without a StreamId.",
                    this);
                return;
            }

            if (!channelsByStreamId.TryGetValue(
                    streamId,
                    out DownlinkCommunicationChannel channel))
            {
                Debug.LogError(
                    $"{GetType().Name} has no channel for feedback stream " +
                    $"'{streamId}'.",
                    this);
                return;
            }

            double ingressTimestampSeconds = Agent.CurrentTimestampSeconds;
            channel.ReceivePackage(package, ingressTimestampSeconds, this);
            channel.ReleaseDuePackages(ingressTimestampSeconds, ReleasePackage);
        }

        internal override void Step(double timestampSeconds)
        {
            for (int index = 0; index < channels.Count; index++)
            {
                channels[index].ReleaseDuePackages(
                    timestampSeconds,
                    ReleasePackage);
            }
        }

        protected override void OnInitialize()
        {
            channelsByStreamId.Clear();

            if (channels == null || channels.Count == 0)
            {
                Debug.LogError(
                    $"{GetType().Name} requires at least one communication channel.",
                    this);
                enabled = false;
                return;
            }

            for (int index = 0; index < channels.Count; index++)
            {
                DownlinkCommunicationChannel channel = channels[index];

                if (channel == null)
                {
                    Debug.LogError(
                        $"{GetType().Name} contains an unassigned channel at index {index}.",
                        this);
                    enabled = false;
                    return;
                }

                if (!channel.TryInitialize(out string error))
                {
                    Debug.LogError($"{GetType().Name} {error}", this);
                    enabled = false;
                    return;
                }

                if (channelsByStreamId.ContainsKey(channel.StreamId))
                {
                    Debug.LogError(
                        $"{GetType().Name} contains duplicate channel stream id " +
                        $"'{channel.StreamId}'.",
                        this);
                    enabled = false;
                    return;
                }

                channelsByStreamId.Add(channel.StreamId, channel);
            }
        }

        private void ReleasePackage(Package package)
        {
            PackageProduced?.Invoke(package);
        }
    }
}
