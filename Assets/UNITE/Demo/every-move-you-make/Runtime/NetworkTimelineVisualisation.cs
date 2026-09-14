using System.Collections.Generic;
using Unite.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unite.Demo.EveryMoveYouMake
{
    /// <summary>
    /// Network visualisation: a screen-space keypress timeline that animates a node from a
    /// start anchor to an end anchor over the command's round-trip delay. Consumes
    /// <see cref="NetworkTimelinePackage"/>.
    /// </summary>
    public sealed class NetworkTimelineVisualisation : EveryMoveVisualisation
    {
        [SerializeField] private GameObject networkTimelineRoot;
        // The four arrow images are retained as the input-state indicators
        // (order: up, down, left, right).
        [SerializeField] private Image[] arrowImages;
        [SerializeField] private Sprite timelineEndpointSprite;
        [SerializeField] private float commandDelaySeconds = 2.56f;
        [SerializeField] private RectTransform[] timelineStarts = System.Array.Empty<RectTransform>();
        [SerializeField] private RectTransform[] timelineEnds = System.Array.Empty<RectTransform>();
        [SerializeField, Min(32)] private int maximumTimelineNodes = 600;

        private sealed class TimelineNode
        {
            public RectTransform rect;
            public Vector2 start;
            public Vector2 end;
            public double startTime;
            public float duration;
        }

        private readonly List<TimelineNode> timelineNodes = new List<TimelineNode>();
        private readonly Queue<TimelineNode> timelineNodePool = new Queue<TimelineNode>();
        private int timelineNodeCount;
        private const float StudyOverlayCoordinateScale = 2.15f;
        private const float StudyOverlayVerticalOffset = 30f;

        private static readonly Vector2[] StudyTimelineStarts =
        {
            new Vector2(-80f, -112f),
            new Vector2(-80f, -144f),
            new Vector2(-262f, -128f),
            new Vector2(102f, -128f)
        };

        private static readonly Vector2[] StudyTimelineEnds =
        {
            new Vector2(80f, -112f),
            new Vector2(80f, -144f),
            new Vector2(-102f, -128f),
            new Vector2(262f, -128f)
        };

        private void Awake()
        {
            ConfigureNetworkTimeline();
        }

        public override bool TryPresent(Package package, double timestampSeconds)
        {
            if (!(package.Payload is NetworkTimelinePackage timeline) || timeline.Directions == null)
                return false;

            if (networkTimelineRoot && !networkTimelineRoot.activeSelf)
                networkTimelineRoot.SetActive(true);

            for (int index = 0; index < timeline.Directions.Length; index++)
            {
                int row = (int)timeline.Directions[index];
                SpawnTimelineNode(
                    row,
                    timeline.CommandTimestampSeconds,
                    timeline.RoundTripDelaySeconds);
            }
            return true;
        }

        public override void UpdateVisual(double timestampSeconds)
        {
            if (!networkTimelineRoot || !networkTimelineRoot.activeSelf)
            {
                ClearTimelineNodes();
                return;
            }
            UpdateTimelineNodes(timestampSeconds);
        }

        private void UpdateTimelineNodes(double now)
        {
            for (int i = timelineNodes.Count - 1; i >= 0; i--)
            {
                TimelineNode node = timelineNodes[i];
                float duration = node.duration > 0f
                    ? node.duration
                    : Mathf.Max(0.01f, commandDelaySeconds);
                float t = Mathf.Clamp01((float)(now - node.startTime) / duration);
                if (node.rect)
                    node.rect.anchoredPosition = Vector2.Lerp(node.start, node.end, t);
                if (t >= 1f)
                {
                    timelineNodes.RemoveAt(i);
                    ReleaseTimelineNode(node);
                }
            }
        }

        private void ClearTimelineNodes()
        {
            for (int i = timelineNodes.Count - 1; i >= 0; i--)
                ReleaseTimelineNode(timelineNodes[i]);
            timelineNodes.Clear();
        }

        private void ReleaseTimelineNode(TimelineNode node)
        {
            if (node == null || !node.rect) return;
            node.rect.gameObject.SetActive(false);
            timelineNodePool.Enqueue(node);
        }

        private void SpawnTimelineNode(int row, double startTime, float duration)
        {
            if (timelineStarts == null || timelineEnds == null ||
                row >= timelineStarts.Length || row >= timelineEnds.Length ||
                row < 0 ||
                !timelineStarts[row] || !timelineEnds[row])
                return;

            TimelineNode node = AcquireTimelineNode();
            if (node == null) return;
            node.start = timelineStarts[row].anchoredPosition;
            node.end = timelineEnds[row].anchoredPosition;
            node.startTime = startTime;
            node.duration = Mathf.Max(0.01f, duration);
            node.rect.anchoredPosition = node.start;
            node.rect.gameObject.SetActive(true);
            timelineNodes.Add(node);
        }

        private TimelineNode AcquireTimelineNode()
        {
            if (timelineNodePool.Count > 0)
                return timelineNodePool.Dequeue();
            if (timelineNodeCount >= maximumTimelineNodes) return null;

            var go = new GameObject(
                "timeline-node",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(networkTimelineRoot.transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(
                6f * StudyOverlayCoordinateScale,
                10f * StudyOverlayCoordinateScale);
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.5965f, 0.9906f, 0.5f, 1f);
            image.raycastTarget = false;
            timelineNodeCount++;
            return new TimelineNode
            {
                rect = rect
            };
        }

        private void ConfigureNetworkTimeline()
        {
            if (!networkTimelineRoot) return;

            RectTransform root = networkTimelineRoot.GetComponent<RectTransform>();
            if (!root) return;
            GraphicRaycaster raycaster = networkTimelineRoot.GetComponent<GraphicRaycaster>();
            if (raycaster) raycaster.enabled = false;

            // Keep the delayed-command key in screen space. A world-space canvas can be
            // clipped by the Moon mesh and changes apparent size with the camera/terrain
            // distance, which is exactly the failure visible in the reconstructed scene.
            root.SetParent(null, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.localScale = Vector3.one;
            root.localRotation = Quaternion.identity;

            Canvas canvas = networkTimelineRoot.GetComponent<Canvas>();
            if (canvas)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                canvas.pixelPerfect = true;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 1000;
            }

            CanvasScaler scaler = networkTimelineRoot.GetComponent<CanvasScaler>();
            if (scaler)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1158f, 650f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            timelineStarts = EnsureTimelineAnchors("Start", StudyTimelineStarts);
            timelineEnds = EnsureTimelineAnchors("End", StudyTimelineEnds);

            for (int row = 0; row < StudyTimelineStarts.Length; row++)
                EnsureTimelineTrack(row, StudyTimelineStarts[row], StudyTimelineEnds[row]);

            // The reference canvas contains eight blue endpoint arrows. Its four larger input
            // images are not row labels, so leave them hidden here; the timeline nodes still
            // use their array order to map keyboard input to rows.
            if (arrowImages == null) return;
            for (int row = 0; row < arrowImages.Length; row++)
            {
                if (!arrowImages[row]) continue;
                arrowImages[row].raycastTarget = false;
                arrowImages[row].gameObject.SetActive(false);
            }
        }

        private RectTransform[] EnsureTimelineAnchors(string suffix, Vector2[] positions)
        {
            var result = new RectTransform[positions.Length];
            for (int row = 0; row < positions.Length; row++)
            {
                string name = $"Timeline{suffix}{row}";
                Transform existing = networkTimelineRoot.transform.Find(name);
                RectTransform rect;
                if (existing)
                {
                    rect = existing as RectTransform;
                }
                else
                {
                    var go = new GameObject(name, typeof(RectTransform));
                    rect = (RectTransform)go.transform;
                    rect.SetParent(networkTimelineRoot.transform, false);
                }

                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = Vector2.one * (8f * StudyOverlayCoordinateScale);
                rect.anchoredPosition = ScaleStudyPosition(positions[row]);
                rect.localRotation = Quaternion.Euler(0f, 0f, TimelineArrowRotation(row));

                Image image = rect.GetComponent<Image>();
                if (!image) image = rect.gameObject.AddComponent<Image>();
                image.sprite = timelineEndpointSprite;
                image.color = Color.white;
                image.preserveAspect = false;
                image.raycastTarget = false;
                result[row] = rect;
            }
            return result;
        }

        private static float TimelineArrowRotation(int row)
        {
            switch (row)
            {
                case 1: return 180f;
                case 2: return 90f;
                case 3: return -90f;
                default: return 0f;
            }
        }

        private static Vector2 ScaleStudyPosition(Vector2 position)
        {
            return position * StudyOverlayCoordinateScale +
                   Vector2.up * StudyOverlayVerticalOffset;
        }

        private void EnsureTimelineTrack(int row, Vector2 start, Vector2 end)
        {
            string name = $"TimelineTrack{row}";
            Transform existing = networkTimelineRoot.transform.Find(name);
            GameObject go = existing
                ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (!existing) go.transform.SetParent(networkTimelineRoot.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = ScaleStudyPosition((start + end) * 0.5f);
            rect.sizeDelta = new Vector2(
                Vector2.Distance(start, end) * StudyOverlayCoordinateScale,
                2f);
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.035f, 0.61f, 0.91f, 1f);
            image.raycastTarget = false;
            rect.SetAsFirstSibling();
        }
    }
}
