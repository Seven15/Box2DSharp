using System;
using System.Diagnostics;
using System.Numerics;
using Box2DSharp.Collision.Collider;
using Box2DSharp.Collision.Shapes;
using Box2DSharp.Common;

namespace Box2DSharp.Collision
{
    public static partial class CollisionUtils
    {
        /// <summary>
        ///     Compute contact points for edge versus circle.
        ///     This accounts for edge connectivity.
        ///     计算边缘和圆的碰撞点
        /// </summary>
        /// <param name="manifold"></param>
        /// <param name="edgeA"></param>
        /// <param name="xfA"></param>
        /// <param name="circleB"></param>
        /// <param name="xfB"></param>
        public static void CollideEdgeAndCircle(
            ref Manifold manifold,
            EdgeShape    edgeA,
            in Transform xfA,
            CircleShape  circleB,
            in Transform xfB)
        {
            manifold.PointCount = 0;

            // Compute circle in frame of edge
            // 在边缘形状的外框处理圆形
            var Q = MathUtils.MulT(xfA, MathUtils.Mul(xfB, circleB.Position));

            Vector2 A = edgeA.Vertex1, B = edgeA.Vertex2;
            var     e = B - A;

            // Barycentric coordinates
            // 质心坐标
            var u = MathUtils.Dot(e, B - Q);
            var v = MathUtils.Dot(e, Q - A);

            var radius = edgeA.Radius + circleB.Radius;

            var cf = new ContactFeature
            {
                IndexB = 0,
                TypeB  = (byte) ContactFeature.FeatureType.Vertex
            };

            // Region A
            if (v <= 0.0f)
            {
                var P  = A;
                var d  = Q - P;
                var dd = MathUtils.Dot(d, d);
                if (dd > radius * radius)
                {
                    return;
                }

                // Is there an edge connected to A?
                if (edgeA.HasVertex0)
                {
                    var A1 = edgeA.Vertex0;
                    var B1 = A;
                    var e1 = B1 - A1;
                    var u1 = MathUtils.Dot(e1, B1 - Q);

                    // Is the circle in Region AB of the previous edge?
                    if (u1 > 0.0f)
                    {
                        return;
                    }
                }

                cf.IndexA           = 0;
                cf.TypeA            = (byte) ContactFeature.FeatureType.Vertex;
                manifold.PointCount = 1;
                manifold.Type       = ManifoldType.Circles;
                manifold.LocalNormal.SetZero();
                manifold.LocalPoint           = P;
                manifold.Points[0].Id.Key     = 0;
                manifold.Points[0].Id.ContactFeature      = cf;
                manifold.Points[0].LocalPoint = circleB.Position;
                return;
            }

            // Region B
            if (u <= 0.0f)
            {
                var P  = B;
                var d  = Q - P;
                var dd = MathUtils.Dot(d, d);
                if (dd > radius * radius)
                {
                    return;
                }

                // Is there an edge connected to B?
                if (edgeA.HasVertex3)
                {
                    var B2 = edgeA.Vertex3;
                    var A2 = B;
                    var e2 = B2 - A2;
                    var v2 = MathUtils.Dot(e2, Q - A2);

                    // Is the circle in Region AB of the next edge?
                    if (v2 > 0.0f)
                    {
                        return;
                    }
                }

                cf.IndexA           = 1;
                cf.TypeA            = (byte) ContactFeature.FeatureType.Vertex;
                manifold.PointCount = 1;
                manifold.Type       = ManifoldType.Circles;
                manifold.LocalNormal.SetZero();
                manifold.LocalPoint           = P;
                manifold.Points[0].Id.Key     = 0;
                manifold.Points[0].Id.ContactFeature      = cf;
                manifold.Points[0].LocalPoint = circleB.Position;
                return;
            }

            {
                // Region AB
                var den = MathUtils.Dot(e, e);
                Debug.Assert(den > 0.0f);
                var P  = 1.0f / den * (u * A + v * B);
                var d  = Q - P;
                var dd = MathUtils.Dot(d, d);
                if (dd > radius * radius)
                {
                    return;
                }

                var n = new Vector2(-e.Y, e.X);
                if (MathUtils.Dot(n, Q - A) < 0.0f)
                {
                    n.Set(-n.X, -n.Y);
                }

                n.Normalize();

                cf.IndexA                     = 0;
                cf.TypeA                      = (byte) ContactFeature.FeatureType.Face;
                manifold.PointCount           = 1;
                manifold.Type                 = ManifoldType.FaceA;
                manifold.LocalNormal          = n;
                manifold.LocalPoint           = A;
                manifold.Points[0].Id.Key     = 0;
                manifold.Points[0].Id.ContactFeature      = cf;
                manifold.Points[0].LocalPoint = circleB.Position;
            }
        }

        /// Compute the collision manifold between an edge and a circle.
        public static void CollideEdgeAndPolygon(
            ref Manifold manifold,
            EdgeShape    edgeA,
            in Transform xfA,
            PolygonShape polygonB,
            in Transform xfB)
        {
            new EPCollider().Collide(
                ref manifold,
                ref edgeA,
                xfA,
                ref polygonB,
                xfB);
        }

        // This structure is used to keep track of the best separating axis.
        public struct EPAxis
        {
            public enum EPAxisType
            {
                Unknown,

                EdgeA,

                EdgeB
            }

            public EPAxisType Type;

            public int Index;

            public float Separation;
        }

        // This holds polygon B expressed in frame A.
        public struct TempPolygon
        {
            public Vector2[] Vertices;

            public Vector2[] Normals;

            public int Count;

            public TempPolygon(int count)
            {
                Count    = count;
                Vertices = new Vector2[Settings.MaxPolygonVertices];
                Normals  = new Vector2[Settings.MaxPolygonVertices];
            }
        }

        // Reference face used for clipping
        public class ReferenceFace
        {
            public int i1, i2;

            public Vector2 normal;

            public Vector2 sideNormal1;

            public Vector2 sideNormal2;

            public float sideOffset1;

            public float sideOffset2;

            public Vector2 v1, v2;
        }

        // This class collides and edge and a polygon, taking into account edge adjacency.
        public class EPCollider
        {
            public enum VertexType
            {
                Isolated,

                Concave,

                Convex
            }

            public Vector2 CentroidB;

            public bool Front;

            public Vector2 LowerLimit, UpperLimit;

            public Vector2 Normal;

            public Vector2 Normal0, Normal1, Normal2;

            public TempPolygon PolygonB;

            public float Radius;

            public VertexType Type1, Type2;

            public Vector2 V0, V1, V2, V3;

            public Transform Transform;

            // Algorithm:
            // 1. Classify v1 and v2
            // 2. Classify polygon centroid as front or back
            // 3. Flip normal if necessary
            // 4. Initialize normal range to [-pi, pi] about face normal
            // 5. Adjust normal range according to adjacent edges
            // 6. Visit each separating axes, only accept axes within the range
            // 7. Return if _any_ axis indicates separation
            // 8. Clip
            public void Collide(
                ref Manifold     manifold,
                ref EdgeShape    edgeA,
                in  Transform    xfA,
                ref PolygonShape polygonB,
                in  Transform    xfB)
            {
                Transform = MathUtils.MulT(xfA, xfB);

                CentroidB = MathUtils.Mul(Transform, polygonB.Centroid);

                V0 = edgeA.Vertex0;
                V1 = edgeA.Vertex1;
                V2 = edgeA.Vertex2;
                V3 = edgeA.Vertex3;

                var hasVertex0 = edgeA.HasVertex0;
                var hasVertex3 = edgeA.HasVertex3;

                var edge1 = V2 - V1;
                edge1.Normalize();
                Normal1.Set(edge1.Y, -edge1.X);
                var   offset1 = MathUtils.Dot(Normal1, CentroidB - V1);
                float offset0 = 0.0f,  offset2 = 0.0f;
                bool  convex1 = false, convex2 = false;

                // Is there a preceding edge?
                if (hasVertex0)
                {
                    var edge0 = V1 - V0;
                    edge0.Normalize();
                    Normal0.Set(edge0.Y, -edge0.X);
                    convex1 = MathUtils.Cross(edge0, edge1) >= 0.0f;
                    offset0 = MathUtils.Dot(Normal0, CentroidB - V0);
                }

                // Is there a following edge?
                if (hasVertex3)
                {
                    var edge2 = V3 - V2;
                    edge2.Normalize();
                    Normal2.Set(edge2.Y, -edge2.X);
                    convex2 = MathUtils.Cross(edge1, edge2) > 0.0f;
                    offset2 = MathUtils.Dot(Normal2, CentroidB - V2);
                }

                // Determine front or back  Determine collision normal limits.
                if (hasVertex0 && hasVertex3)
                {
                    if (convex1 && convex2)
                    {
                        Front = offset0 >= 0.0f || offset1 >= 0.0f || offset2 >= 0.0f;
                        if (Front)
                        {
                            Normal     = Normal1;
                            LowerLimit = Normal0;
                            UpperLimit = Normal2;
                        }
                        else
                        {
                            Normal     = -Normal1;
                            LowerLimit = -Normal1;
                            UpperLimit = -Normal1;
                        }
                    }
                    else if (convex1)
                    {
                        Front = offset0 >= 0.0f || offset1 >= 0.0f && offset2 >= 0.0f;
                        if (Front)
                        {
                            Normal     = Normal1;
                            LowerLimit = Normal0;
                            UpperLimit = Normal1;
                        }
                        else
                        {
                            Normal     = -Normal1;
                            LowerLimit = -Normal2;
                            UpperLimit = -Normal1;
                        }
                    }
                    else if (convex2)
                    {
                        Front = offset2 >= 0.0f || offset0 >= 0.0f && offset1 >= 0.0f;
                        if (Front)
                        {
                            Normal     = Normal1;
                            LowerLimit = Normal1;
                            UpperLimit = Normal2;
                        }
                        else
                        {
                            Normal     = -Normal1;
                            LowerLimit = -Normal1;
                            UpperLimit = -Normal0;
                        }
                    }
                    else
                    {
                        Front = offset0 >= 0.0f && offset1 >= 0.0f && offset2 >= 0.0f;
                        if (Front)
                        {
                            Normal     = Normal1;
                            LowerLimit = Normal1;
                            UpperLimit = Normal1;
                        }
                        else
                        {
                            Normal     = -Normal1;
                            LowerLimit = -Normal2;
                            UpperLimit = -Normal0;
                        }
                    }
                }
                else if (hasVertex0)
                {
                    if (convex1)
                    {
                        Front = offset0 >= 0.0f || offset1 >= 0.0f;
                        if (Front)
                        {
                            Normal     = Normal1;
                            LowerLimit = Normal0;
                            UpperLimit = -Normal1;
                        }
                        else
                        {
                            Normal     = -Normal1;
                            LowerLimit = Normal1;
                            UpperLimit = -Normal1;
                        }
                    }
                    else
                    {
                        Front = offset0 >= 0.0f && offset1 >= 0.0f;
                        if (Front)
                        {
                            Normal     = Normal1;
                            LowerLimit = Normal1;
                            UpperLimit = -Normal1;
                        }
                        else
                        {
                            Normal     = -Normal1;
                            LowerLimit = Normal1;
                            UpperLimit = -Normal0;
                        }
                    }
                }
                else if (hasVertex3)
                {
                    if (convex2)
                    {
                        Front = offset1 >= 0.0f || offset2 >= 0.0f;
                        if (Front)
                        {
                            Normal     = Normal1;
                            LowerLimit = -Normal1;
                            UpperLimit = Normal2;
                        }
                        else
                        {
                            Normal     = -Normal1;
                            LowerLimit = -Normal1;
                            UpperLimit = Normal1;
                        }
                    }
                    else
                    {
                        Front = offset1 >= 0.0f && offset2 >= 0.0f;
                        if (Front)
                        {
                            Normal     = Normal1;
                            LowerLimit = -Normal1;
                            UpperLimit = Normal1;
                        }
                        else
                        {
                            Normal     = -Normal1;
                            LowerLimit = -Normal2;
                            UpperLimit = Normal1;
                        }
                    }
                }
                else
                {
                    Front = offset1 >= 0.0f;
                    if (Front)
                    {
                        Normal     = Normal1;
                        LowerLimit = -Normal1;
                        UpperLimit = -Normal1;
                    }
                    else
                    {
                        Normal     = -Normal1;
                        LowerLimit = Normal1;
                        UpperLimit = Normal1;
                    }
                }

                // Get polygonB in frameA
                PolygonB = new TempPolygon(polygonB.Count);
                for (var i = 0; i < polygonB.Count; ++i)
                {
                    PolygonB.Vertices[i] = MathUtils.Mul(Transform, polygonB.Vertices[i]);
                    PolygonB.Normals[i]  = MathUtils.Mul(Transform.Rotation, polygonB.Vertices[i]);
                }

                Radius = polygonB.Radius + edgeA.Radius;

                manifold.PointCount = 0;

                var edgeAxis = ComputeEdgeSeparation();

                // If no valid normal can be found than this edge should not collide.
                if (edgeAxis.Type == EPAxis.EPAxisType.Unknown)
                {
                    return;
                }

                if (edgeAxis.Separation > Radius)
                {
                    return;
                }

                var polygonAxis = ComputePolygonSeparation();
                if (polygonAxis.Type != EPAxis.EPAxisType.Unknown && polygonAxis.Separation > Radius)
                {
                    return;
                }

                // Use hysteresis for jitter reduction.
                const float k_relativeTol = 0.98f;
                const float k_absoluteTol = 0.001f;

                EPAxis primaryAxis;
                if (polygonAxis.Type == EPAxis.EPAxisType.Unknown)
                {
                    primaryAxis = edgeAxis;
                }
                else if (polygonAxis.Separation > k_relativeTol * edgeAxis.Separation + k_absoluteTol)
                {
                    primaryAxis = polygonAxis;
                }
                else
                {
                    primaryAxis = edgeAxis;
                }

                var ie = new ClipVertex[2];
                var rf = new ReferenceFace();
                if (primaryAxis.Type == EPAxis.EPAxisType.EdgeA)
                {
                    manifold.Type = ManifoldType.FaceA;

                    // Search for the polygon normal that is most anti-parallel to the edge normal.
                    var bestIndex = 0;
                    var bestValue = MathUtils.Dot(Normal, PolygonB.Normals[0]);
                    for (var i = 1; i < PolygonB.Count; ++i)
                    {
                        var value = MathUtils.Dot(Normal, PolygonB.Normals[i]);
                        if (value < bestValue)
                        {
                            bestValue = value;
                            bestIndex = i;
                        }
                    }

                    var i1 = bestIndex;
                    var i2 = i1 + 1 < PolygonB.Count ? i1 + 1 : 0;

                    ie[0].Vector            = PolygonB.Vertices[i1];
                    ie[0].Id.ContactFeature.IndexA = 0;
                    ie[0].Id.ContactFeature.IndexB = (byte) i1;
                    ie[0].Id.ContactFeature.TypeA  = (byte) ContactFeature.FeatureType.Face;
                    ie[0].Id.ContactFeature.TypeB  = (byte) ContactFeature.FeatureType.Vertex;

                    ie[1].Vector            = PolygonB.Vertices[i2];
                    ie[1].Id.ContactFeature.IndexA = 0;
                    ie[1].Id.ContactFeature.IndexB = (byte) i2;
                    ie[1].Id.ContactFeature.TypeA  = (byte) ContactFeature.FeatureType.Face;
                    ie[1].Id.ContactFeature.TypeB  = (byte) ContactFeature.FeatureType.Vertex;

                    if (Front)
                    {
                        rf.i1     = 0;
                        rf.i2     = 1;
                        rf.v1     = V1;
                        rf.v2     = V2;
                        rf.normal = Normal1;
                    }
                    else
                    {
                        rf.i1     = 1;
                        rf.i2     = 0;
                        rf.v1     = V2;
                        rf.v2     = V1;
                        rf.normal = -Normal1;
                    }
                }
                else
                {
                    manifold.Type = ManifoldType.FaceB;

                    ie[0].Vector            = V1;
                    ie[0].Id.ContactFeature.IndexA = 0;
                    ie[0].Id.ContactFeature.IndexB = (byte) primaryAxis.Index;
                    ie[0].Id.ContactFeature.TypeA  = (byte) ContactFeature.FeatureType.Vertex;
                    ie[0].Id.ContactFeature.TypeB  = (byte) ContactFeature.FeatureType.Face;

                    ie[1].Vector            = V2;
                    ie[1].Id.ContactFeature.IndexA = 0;
                    ie[1].Id.ContactFeature.IndexB = (byte) primaryAxis.Index;
                    ie[1].Id.ContactFeature.TypeA  = (byte) ContactFeature.FeatureType.Vertex;
                    ie[1].Id.ContactFeature.TypeB  = (byte) ContactFeature.FeatureType.Face;

                    rf.i1     = primaryAxis.Index;
                    rf.i2     = rf.i1 + 1 < PolygonB.Count ? rf.i1 + 1 : 0;
                    rf.v1     = PolygonB.Vertices[rf.i1];
                    rf.v2     = PolygonB.Vertices[rf.i2];
                    rf.normal = PolygonB.Normals[rf.i1];
                }

                rf.sideNormal1.Set(rf.normal.Y, -rf.normal.X);
                rf.sideNormal2 = -rf.sideNormal1;
                rf.sideOffset1 = MathUtils.Dot(rf.sideNormal1, rf.v1);
                rf.sideOffset2 = MathUtils.Dot(rf.sideNormal2, rf.v2);

                // Clip incident edge against extruded edge1 side edges.
                var clipPoints1 = new ClipVertex[2];
                var clipPoints2 = new ClipVertex[2];
                int np;

                // Clip to box side 1
                np = ClipSegmentToLine(
                    ref clipPoints1,
                    ie,
                    rf.sideNormal1,
                    rf.sideOffset1,
                    rf.i1);

                if (np < Settings.MaxManifoldPoints)
                {
                    return;
                }

                // Clip to negative box side 1
                np = ClipSegmentToLine(
                    ref clipPoints2,
                    clipPoints1,
                    rf.sideNormal2,
                    rf.sideOffset2,
                    rf.i2);

                if (np < Settings.MaxManifoldPoints)
                {
                    return;
                }

                // Now clipPoints2 contains the clipped points.
                if (primaryAxis.Type == EPAxis.EPAxisType.EdgeA)
                {
                    manifold.LocalNormal = rf.normal;
                    manifold.LocalPoint  = rf.v1;
                }
                else
                {
                    manifold.LocalNormal = polygonB.Normals[rf.i1];
                    manifold.LocalPoint  = polygonB.Vertices[rf.i1];
                }

                var pointCount = 0;
                for (var i = 0; i < Settings.MaxManifoldPoints; ++i)
                {
                    var separation = MathUtils.Dot(rf.normal, clipPoints2[i].Vector - rf.v1);

                    if (separation <= Radius)
                    {
                        ref var cp = ref manifold.Points[pointCount];

                        if (primaryAxis.Type == EPAxis.EPAxisType.EdgeA)
                        {
                            cp.LocalPoint = MathUtils.MulT(Transform, clipPoints2[i].Vector);
                            cp.Id         = clipPoints2[i].Id;
                        }
                        else
                        {
                            cp.LocalPoint   = clipPoints2[i].Vector;
                            cp.Id.ContactFeature.TypeA  = clipPoints2[i].Id.ContactFeature.TypeB;
                            cp.Id.ContactFeature.TypeB  = clipPoints2[i].Id.ContactFeature.TypeA;
                            cp.Id.ContactFeature.IndexA = clipPoints2[i].Id.ContactFeature.IndexB;
                            cp.Id.ContactFeature.IndexB = clipPoints2[i].Id.ContactFeature.IndexA;
                        }

                        ++pointCount;
                    }
                }

                manifold.PointCount = pointCount;
            }

            public EPAxis ComputeEdgeSeparation()
            {
                EPAxis axis;
                axis.Type       = EPAxis.EPAxisType.EdgeA;
                axis.Index      = Front ? 0 : 1;
                axis.Separation = float.MaxValue;

                for (var i = 0; i < PolygonB.Count; ++i)
                {
                    var s = MathUtils.Dot(Normal, PolygonB.Vertices[i] - V1);
                    if (s < axis.Separation)
                    {
                        axis.Separation = s;
                    }
                }

                return axis;
            }

            public EPAxis ComputePolygonSeparation()
            {
                EPAxis axis;
                axis.Type       = EPAxis.EPAxisType.Unknown;
                axis.Index      = -1;
                axis.Separation = -float.MaxValue;

                var perp = new Vector2(-Normal.Y, Normal.X);

                for (var i = 0; i < PolygonB.Count; ++i)
                {
                    var n = -PolygonB.Normals[i];

                    var s1 = MathUtils.Dot(n, PolygonB.Vertices[i] - V1);
                    var s2 = MathUtils.Dot(n, PolygonB.Vertices[i] - V2);
                    var s  = Math.Min(s1, s2);

                    if (s > Radius)
                    {
                        // No collision
                        axis.Type       = EPAxis.EPAxisType.EdgeB;
                        axis.Index      = i;
                        axis.Separation = s;
                        return axis;
                    }

                    // Adjacency
                    if (MathUtils.Dot(n, perp) >= 0.0f)
                    {
                        if (MathUtils.Dot(n - UpperLimit, Normal) < -Settings.AngularSlop)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (MathUtils.Dot(n - LowerLimit, Normal) < -Settings.AngularSlop)
                        {
                            continue;
                        }
                    }

                    if (s > axis.Separation)
                    {
                        axis.Type       = EPAxis.EPAxisType.EdgeB;
                        axis.Index      = i;
                        axis.Separation = s;
                    }
                }

                return axis;
            }
        }
    }
}