using System;
using System.Collections.Generic;
using Unite.Core;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Routes local-to-remote command packages to independently configured
    /// uplink channels.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UplinkCommunicationModule
        : TeleroboticsModule, IPackageSource, IPackageConsumer
    {
        [SerializeField]
        private List<UplinkCommunicationChannel> channels =
            new List<UplinkCommunicationChannel>();

        private readonly Dictionary<string, UplinkCommunicationChannel>
            channelsByStreamId =
                new Dictionary<string, UplinkCommunicationChannel>(
                    StringComparer.Ordinal);

        public event Action<Package> PackageProduced;

        internal override int ExecutionOrder => 400;

        public void CopyPendingTransmissions(
            string streamId,
            List<UplinkPendingPackageSnapshot> destination)
        {
            if (destination == null)
            {
                return;
            }

            string normalized = string.IsNullOrWhiteSpace(streamId)
                ? string.Empty
                : streamId.Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!channelsByStreamId.TryGetValue(
                    normalized,
                    out UplinkCommunicationChannel channel))
            {
                return;
            }

            channel.CopyPendingSnapshots(destination);
        }

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
                    out UplinkCommunicationChannel channel))
            {
                Debug.LogError(
                    $"{GetType().Name} has no channel for command stream " +
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
                UplinkCommunicationChannel channel = channels[index];

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
