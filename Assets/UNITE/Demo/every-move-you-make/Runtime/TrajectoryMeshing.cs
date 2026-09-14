using System.Collections.Generic;
using UnityEngine;

namespace Unite.Demo.EveryMoveYouMake
{
    /// <summary>
    /// Ports the reference study's ProjectionUtils and PathMeshBuilder so predicted
    /// trajectories are projected onto the Moon surface and drawn as terrain-following
    /// ribbon and region meshes, exactly as the original DifferentialDriveAgent rendered
    /// them (RenderPath / RenderRegion). Predicted lines therefore lie on the environment
    /// rather than floating above it.
    /// </summary>
    internal static class TrajectoryMeshing
    {
        // Reference used a small lift above the surface so the ribbon does not z-fight.
        private const float SurfaceLift = 0.01f;
        private const float FallbackY = 0.011f;
        private const int EndCapSteps = 20;

        // Unity calls this code on the main thread. Reusing these buffers removes the large
        // amount of short-lived mesh garbage that the first port produced at every tick.
        private static readonly List<Vector3> sampledBuffer = new List<Vector3>();
        private static readonly List<Vector3> vertexBuffer = new List<Vector3>();
        private static readonly List<Vector3> normalBuffer = new List<Vector3>();
        private static readonly List<Vector2> uvBuffer = new List<Vector2>();
        private static readonly List<int> triangleBuffer = new List<int>();
        private static readonly List<int> polygonIndices = new List<int>();
        private static readonly List<Vector3> leftProjectionBuffer = new List<Vector3>();
        private static readonly List<Vector3> rightProjectionBuffer = new List<Vector3>();
        private static readonly List<Vector3> boundaryBuffer = new List<Vector3>();

        public static Vector3 ProjectPointToMoon(Vector3 flatPosition, MeshCollider moonCollider)
        {
            Ray ray = new Ray(new Vector3(flatPosition.x, 1000f, flatPosition.z), Vector3.down);
            if (moonCollider != null && moonCollider.Raycast(ray, out RaycastHit hit, 2000f))
                return hit.point + new Vector3(0f, SurfaceLift, 0f);
            return new Vector3(flatPosition.x, FallbackY, flatPosition.z);
        }

        public static void ProjectPoses(PredictedPose[] poses, MeshCollider moonCollider, List<Vector3> output)
        {
            output.Clear();
            if (poses == null) return;
            for (int i = 0; i < poses.Length; i++)
                output.Add(ProjectPointToMoon(poses[i].position, moonCollider));
        }

        /// <summary>
        /// Verbatim reconstruction of PathMeshBuilder.CreateStableRibbonMesh, writing into an
        /// existing mesh so the ribbon can be rebuilt every step without allocating a Mesh.
        /// </summary>
        public static void BuildRibbonMesh(List<Vector3> path, float width, int step, Mesh mesh)
        {
            if (mesh == null) return;
            mesh.Clear(false);
            if (path == null || path.Count < 2) return;

            sampledBuffer.Clear();
            for (int i = 0; i < path.Count; i += step)
                sampledBuffer.Add(path[i]);
            if (sampledBuffer[sampledBuffer.Count - 1] != path[path.Count - 1])
                sampledBuffer.Add(path[path.Count - 1]);
            if (sampledBuffer.Count < 2) return;

            vertexBuffer.Clear();
            triangleBuffer.Clear();
            uvBuffer.Clear();
            normalBuffer.Clear();

            for (int i = 0; i < sampledBuffer.Count - 1; i++)
            {
                Vector3 p0 = sampledBuffer[i];
                Vector3 p1 = sampledBuffer[i + 1];
                Vector3 dir = (p1 - p0).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized * (width * 0.5f);

                int idx = vertexBuffer.Count;
                vertexBuffer.Add(p0 - right);
                vertexBuffer.Add(p0 + right);
                vertexBuffer.Add(p1 - right);
                vertexBuffer.Add(p1 + right);
                uvBuffer.Add(new Vector2(0f, 0f));
                uvBuffer.Add(new Vector2(1f, 0f));
                uvBuffer.Add(new Vector2(0f, 1f));
                uvBuffer.Add(new Vector2(1f, 1f));
                normalBuffer.Add(Vector3.up);
                normalBuffer.Add(Vector3.up);
                normalBuffer.Add(Vector3.up);
                normalBuffer.Add(Vector3.up);
                triangleBuffer.Add(idx + 0);
                triangleBuffer.Add(idx + 2);
                triangleBuffer.Add(idx + 1);
                triangleBuffer.Add(idx + 1);
                triangleBuffer.Add(idx + 2);
                triangleBuffer.Add(idx + 3);
            }

            mesh.SetVertices(vertexBuffer);
            mesh.SetTriangles(triangleBuffer, 0);
            mesh.SetUVs(0, uvBuffer);
            mesh.SetNormals(normalBuffer);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Reconstructs the study's RegionBuilder contour: left offset path, curved extrema
        /// cap through the middle end point, then the right offset path in reverse. This is
        /// intentionally not a strip between matching left/right samples.
        /// </summary>
        public static void BuildStudyEnvelopeMesh(
            PredictedPose[] middle,
            PredictedPose[] left,
            PredictedPose[] right,
            PredictedPose[] extremaLeft,
            PredictedPose[] extremaRight,
            MeshCollider moonCollider,
            Mesh mesh)
        {
            if (mesh == null) return;
            mesh.Clear(false);
            if (middle == null || left == null || right == null ||
                extremaLeft == null || extremaRight == null ||
                middle.Length == 0 || left.Length == 0 || right.Length == 0 ||
                extremaLeft.Length == 0 || extremaRight.Length == 0)
                return;

            ProjectPoses(left, moonCollider, leftProjectionBuffer);
            ProjectPoses(right, moonCollider, rightProjectionBuffer);

            boundaryBuffer.Clear();
            boundaryBuffer.AddRange(leftProjectionBuffer);

            Vector3 leftEnd = leftProjectionBuffer[leftProjectionBuffer.Count - 1];
            Vector3 middleEnd = ProjectPointToMoon(
                middle[middle.Length - 1].position,
                moonCollider);
            Vector3 rightEnd = rightProjectionBuffer[rightProjectionBuffer.Count - 1];
            Vector3 leftExtrema = ProjectPointToMoon(
                extremaLeft[extremaLeft.Length - 1].position,
                moonCollider);
            Vector3 rightExtrema = ProjectPointToMoon(
                extremaRight[extremaRight.Length - 1].position,
                moonCollider);

            AppendQuadraticBezier(leftEnd, leftExtrema, middleEnd, boundaryBuffer);
            AppendQuadraticBezier(middleEnd, rightExtrema, rightEnd, boundaryBuffer);

            for (int i = rightProjectionBuffer.Count - 1; i >= 0; i--)
                boundaryBuffer.Add(rightProjectionBuffer[i]);

            BuildPolygonMesh(boundaryBuffer, mesh);
        }

        private static void AppendQuadraticBezier(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            List<Vector3> output)
        {
            // Start at one: the contour already contains the previous segment's end.
            for (int i = 1; i <= EndCapSteps; i++)
            {
                float t = i / (float)EndCapSteps;
                float u = 1f - t;
                output.Add(u * u * start + 2f * u * t * control + t * t * end);
            }
        }

        private static void BuildPolygonMesh(List<Vector3> contour, Mesh mesh)
        {
            vertexBuffer.Clear();
            triangleBuffer.Clear();
            normalBuffer.Clear();
            polygonIndices.Clear();

            for (int i = 0; i < contour.Count; i++)
            {
                Vector3 point = contour[i];
                if (vertexBuffer.Count == 0 ||
                    (point - vertexBuffer[vertexBuffer.Count - 1]).sqrMagnitude > 0.00000001f)
                    vertexBuffer.Add(point);
            }

            if (vertexBuffer.Count > 2 &&
                (vertexBuffer[0] - vertexBuffer[vertexBuffer.Count - 1]).sqrMagnitude <=
                0.00000001f)
                vertexBuffer.RemoveAt(vertexBuffer.Count - 1);

            RemoveCollinearVerticesXZ(vertexBuffer);

            if (vertexBuffer.Count < 3) return;

            float signedArea = SignedAreaXZ(vertexBuffer);
            float orientation = signedArea >= 0f ? 1f : -1f;
            for (int i = 0; i < vertexBuffer.Count; i++)
            {
                polygonIndices.Add(i);
                normalBuffer.Add(Vector3.up);
            }

            int safety = vertexBuffer.Count * vertexBuffer.Count;
            while (polygonIndices.Count > 3 && safety-- > 0)
            {
                bool clippedEar = false;
                for (int i = 0; i < polygonIndices.Count; i++)
                {
                    int previous = polygonIndices[(i - 1 + polygonIndices.Count) % polygonIndices.Count];
                    int current = polygonIndices[i];
                    int next = polygonIndices[(i + 1) % polygonIndices.Count];

                    if (!IsConvexXZ(
                            vertexBuffer[previous],
                            vertexBuffer[current],
                            vertexBuffer[next],
                            orientation))
                        continue;

                    bool containsPoint = false;
                    for (int candidateIndex = 0;
                         candidateIndex < polygonIndices.Count;
                         candidateIndex++)
                    {
                        int candidate = polygonIndices[candidateIndex];
                        if (candidate == previous || candidate == current || candidate == next)
                            continue;
                        if (PointInTriangleXZ(
                                vertexBuffer[candidate],
                                vertexBuffer[previous],
                                vertexBuffer[current],
                                vertexBuffer[next]))
                        {
                            containsPoint = true;
                            break;
                        }
                    }

                    if (containsPoint) continue;
                    AddUpwardTriangle(previous, current, next);
                    polygonIndices.RemoveAt(i);
                    clippedEar = true;
                    break;
                }

                if (!clippedEar) break;
            }

            if (polygonIndices.Count == 3)
                AddUpwardTriangle(
                    polygonIndices[0],
                    polygonIndices[1],
                    polygonIndices[2]);

            if (triangleBuffer.Count == 0) return;
            mesh.SetVertices(vertexBuffer);
            mesh.SetTriangles(triangleBuffer, 0);
            mesh.SetNormals(normalBuffer);
            mesh.RecalculateBounds();
        }

        private static void RemoveCollinearVerticesXZ(List<Vector3> points)
        {
            bool removed;
            do
            {
                removed = false;
                for (int i = points.Count - 1; i >= 0 && points.Count > 3; i--)
                {
                    Vector3 previous = points[(i - 1 + points.Count) % points.Count];
                    Vector3 current = points[i];
                    Vector3 next = points[(i + 1) % points.Count];
                    if (Mathf.Abs(CrossXZ(previous, current, next)) > 0.0000001f)
                        continue;
                    points.RemoveAt(i);
                    removed = true;
                }
            }
            while (removed && points.Count > 3);
        }

        private static float SignedAreaXZ(List<Vector3> points)
        {
            float area = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[i];
                Vector3 b = points[(i + 1) % points.Count];
                area += a.x * b.z - b.x * a.z;
            }
            return area * 0.5f;
        }

        private static bool IsConvexXZ(Vector3 a, Vector3 b, Vector3 c, float orientation)
        {
            return CrossXZ(a, b, c) * orientation > 0.0000001f;
        }

        private static float CrossXZ(Vector3 a, Vector3 b, Vector3 c)
        {
            return (b.x - a.x) * (c.z - a.z) -
                   (b.z - a.z) * (c.x - a.x);
        }

        private static bool PointInTriangleXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = CrossXZ(a, b, p);
            float d2 = CrossXZ(b, c, p);
            float d3 = CrossXZ(c, a, p);
            bool hasNegative = d1 < -0.0000001f || d2 < -0.0000001f || d3 < -0.0000001f;
            bool hasPositive = d1 > 0.0000001f || d2 > 0.0000001f || d3 > 0.0000001f;
            return !(hasNegative && hasPositive);
        }

        private static void AddUpwardTriangle(int a, int b, int c)
        {
            Vector3 normal = Vector3.Cross(vertexBuffer[b] - vertexBuffer[a],
                vertexBuffer[c] - vertexBuffer[a]);
            triangleBuffer.Add(a);
            if (normal.y >= 0f)
            {
                triangleBuffer.Add(b);
                triangleBuffer.Add(c);
            }
            else
            {
                triangleBuffer.Add(c);
                triangleBuffer.Add(b);
            }
        }
    }
}
