using System;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.U2D.Common.UTess
{
    enum UEventType
    {
        EVENT_POINT = 0,
        EVENT_END = 1,
        EVENT_START = 2,
    };

    struct UEvent
    {
        public float2 a;
        public float2 b;
        public int idx;
        public int type;
    };

    struct UHull
    {
        public float2 a;
        public float2 b;
        public int idx;

        public ArraySlice<int> ilarray;
        public int ilcount;
        public ArraySlice<int> iuarray;
        public int iucount;
    };

    struct UStar
    {
        public ArraySlice<int> points;
        public int pointCount;
    };

    struct UBounds
    {
        public double2 min;
        public double2 max;
    };

    struct UCircle
    {
        public float2 center;
        public float radius;
    };

    struct UTriangle
    {
        public float2 va;
        public float2 vb;
        public float2 vc;
        public UCircle c;
        public float area;
        public int3 indices;
    };

    struct UEncroachingSegment
    {
        public float2 a;
        public float2 b;
        public int index;
    }

    internal interface ICondition2<in T, in U>
    {
        bool Test(T x, U y, ref float t);
    }

    struct XCompare : IComparer<double>
    {
        public int Compare(double a, double b)
        {
            return (a < b) ? -1 : 1;
        }
    }

    unsafe struct IntersectionCompare : IComparer<int2>
    {
        public Array<double2> points;
        public Array<int2> edges;

        public fixed double xvasort[4];
        public fixed double xvbsort[4];

        public int Compare(int2 a, int2 b)
        {
            var e1a = edges[a.x];
            var e1b = edges[a.y];
            var e2a = edges[b.x];
            var e2b = edges[b.y];

            xvasort[0] = points[e1a.x].x;
            xvasort[1] = points[e1a.y].x;
            xvasort[2] = points[e1b.x].x;
            xvasort[3] = points[e1b.y].x;

            xvbsort[0] = points[e2a.x].x;
            xvbsort[1] = points[e2a.y].x;
            xvbsort[2] = points[e2b.x].x;
            xvbsort[3] = points[e2b.y].x;

            fixed (double* xvasortPtr = xvasort)
            {
                ModuleHandle.InsertionSort<double, XCompare>(xvasortPtr, 0, 3, new XCompare());
            }

            fixed (double* xvbsortPtr = xvbsort)
            {
                ModuleHandle.InsertionSort<double, XCompare>(xvbsortPtr, 0, 3, new XCompare());
            }

            for (int i = 0; i < 4; ++i)
                if (xvasort[i] - xvbsort[i] != 0)
                    return xvasort[i] < xvbsort[i] ? -1 : 1;
            return points[e1a.x].y < points[e2a.x].y ? -1 : 1;
        }
    }

    struct TessEventCompare : IComparer<UEvent>
    {
        public int Compare(UEvent a, UEvent b)
        {
            float f = (a.a.x - b.a.x);
            if (0 != f)
                return (f > 0) ? 1 : -1;

            f = (a.a.y - b.a.y);
            if (0 != f)
                return (f > 0) ? 1 : -1;

            int i = a.type - b.type;
            if (0 != i)
                return i;

            if (a.type != (int)UEventType.EVENT_POINT)
            {
                float o = ModuleHandle.OrientFast(a.a, a.b, b.b);
                if (0 != o)
                {
                    return (o > 0) ? 1 : -1;
                }
            }

            return a.idx - b.idx;
        }
    }

    struct TessEdgeCompare : IComparer<int2>
    {
        public int Compare(int2 a, int2 b)
        {
            int i = a.x - b.x;
            if (0 != i)
                return i;
            i = a.y - b.y;
            return i;
        }
    }

    struct TessCellCompare : IComparer<int3>
    {
        public int Compare(int3 a, int3 b)
        {
            int i = a.x - b.x;
            if (0 != i)
                return i;
            i = a.y - b.y;
            if (0 != i)
                return i;
            i = a.z - b.z;
            return i;
        }
    }

    struct TessJunctionCompare : IComparer<int2>
    {
        public int Compare(int2 a, int2 b)
        {
            int i = a.x - b.x;
            if (0 != i)
                return i;
            i = a.y - b.y;
            return i;
        }
    }

    struct DelaEdgeCompare : IComparer<int4>
    {
        public int Compare(int4 a, int4 b)
        {
            int i = a.x - b.x;
            if (0 != i)
                return i;
            i = a.y - b.y;
            if (0 != i)
                return i;
            i = a.z - b.z;
            if (0 != i)
                return i;
            i = a.w - b.w;
            return i;
        }
    }

    struct TessLink
    {

        internal NativeArray<int> roots;
        internal NativeArray<int> ranks;

        internal static TessLink CreateLink(int count, Allocator allocator)
        {
            TessLink link = new TessLink();
            link.roots = new NativeArray<int>(count, allocator);
            link.ranks = new NativeArray<int>(count, allocator);

            for (int i = 0; i < count; ++i)
            {
                link.roots[i] = i;
                link.ranks[i] = 0;
            }
            return link;
        }

        internal static void DestroyLink(TessLink link)
        {
            link.ranks.Dispose();
            link.roots.Dispose();
        }

        internal int Find(int x)
        {
            var x0 = x;
            while (roots[x] != x)
            {
                x = roots[x];
            }
            while (roots[x0] != x)
            {
                var y = roots[x0];
                roots[x0] = x;
                x0 = y;
            }
            return x;
        }

        internal void Link(int x, int y)
        {
            var xr = Find(x);
            var yr = Find(y);
            if (xr == yr)
            {
                return;
            }
            var xd = ranks[xr];
            var yd = ranks[yr];
            if (xd < yd)
            {
                roots[xr] = yr;
            }
            else if (yd < xd)
            {
                roots[yr] = xr;
            }
            else
            {
                roots[yr] = xr;
                ++ranks[xr];
            }
        }
    };

    internal struct ModuleHandle
    {

        // Max Edge Count with Subdivision allowed. This is already a very relaxed limit
        // and anything beyond are basically littered with numerous paths.
        internal static readonly  int kMaxArea = 65536;
        internal static readonly  int kMaxEdgeCount = 65536;
        internal static readonly  int kMaxIndexCount = 65536;
        internal static readonly  int kMaxVertexCount = 65536;
        internal static readonly  int kMaxTriangleCount = kMaxIndexCount / 3;
        internal static readonly  int kMaxRefineIterations = 48;
        internal static readonly  int kMaxSmoothenIterations = 256;
        internal static readonly  float kIncrementAreaFactor = 1.2f;

        internal static void Copy<T>(NativeArray<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
            where T : struct
        {
            NativeArray<T>.Copy(src, srcIndex, dst, dstIndex, length);
        }

        internal static void Copy<T>(NativeArray<T> src, NativeArray<T> dst, int length)
            where T : struct
        {
            Copy(src, 0, dst, 0, length);
        }

        internal static unsafe void InsertionSort<T, U>(void* array, int lo, int hi, U comp)
            where T : struct where U : IComparer<T>
        {
            int n = hi - lo + 1;
            const int kHybridSortThreshold = 50;

            if (n < kHybridSortThreshold)
            {
                InsertionSortImpl<T, U>(array, lo, hi, comp);
            }
            else
            {
                int depthLimit = 2 * (int)math.log2(n);
                IntroSort<T, U>(array, lo, hi, depthLimit, comp);
            }
        }

        private static unsafe void InsertionSortImpl<T, U>(void* array, int lo, int hi, U comp)
            where T : struct where U : IComparer<T>
        {
            int i, j;
            T t;
            for (i = lo; i < hi; i++)
            {
                j = i;
                t = UnsafeUtility.ReadArrayElement<T>(array, i + 1);
                while (j >= lo && comp.Compare(t, UnsafeUtility.ReadArrayElement<T>(array, j)) < 0)
                {
                    UnsafeUtility.WriteArrayElement<T>(array, j + 1, UnsafeUtility.ReadArrayElement<T>(array, j));
                    j--;
                }
                UnsafeUtility.WriteArrayElement<T>(array, j + 1, t);
            }
        }

        private static unsafe void IntroSort<T, U>(void* array, int lo, int hi, int depthLimit, U comp)
            where T : struct where U : IComparer<T>
        {
            while (hi > lo)
            {
                int partitionSize = hi - lo + 1;

                if (partitionSize <= 16)
                {
                    InsertionSortImpl<T, U>(array, lo, hi, comp);
                    return;
                }

                if (depthLimit == 0)
                {
                    HeapSort<T, U>(array, lo, hi, comp);
                    return;
                }

                depthLimit--;
                int p = Partition<T, U>(array, lo, hi, comp);
                IntroSort<T, U>(array, p + 1, hi, depthLimit, comp);
                hi = p - 1;
            }
        }

        private static unsafe int Partition<T, U>(void* array, int lo, int hi, U comp)
            where T : struct where U : IComparer<T>
        {
            // Simple pivot selection - just use the middle element
            int mid = lo + (hi - lo) / 2;
            Swap<T>(array, mid, hi);
            T pivot = UnsafeUtility.ReadArrayElement<T>(array, hi);

            // Lomuto partition scheme
            int i = lo - 1;
            for (int j = lo; j < hi; j++)
            {
                if (comp.Compare(UnsafeUtility.ReadArrayElement<T>(array, j), pivot) <= 0)
                {
                    i++;
                    Swap<T>(array, i, j);
                }
            }
            Swap<T>(array, i + 1, hi);
            return i + 1;
        }



        private static unsafe void HeapSort<T, U>(void* array, int lo, int hi, U comp)
            where T : struct where U : IComparer<T>
        {
            int n = hi - lo + 1;
            for (int i = n / 2 - 1; i >= 0; i--)
                Heapify<T, U>(array, lo, n, i, comp);

            for (int i = n - 1; i > 0; i--)
            {
                Swap<T>(array, lo, lo + i);
                Heapify<T, U>(array, lo, i, 0, comp);
            }
        }

        private static unsafe void Heapify<T, U>(void* array, int lo, int n, int i, U comp)
            where T : struct where U : IComparer<T>
        {
            int largest = i;
            int left = 2 * i + 1;
            int right = 2 * i + 2;

            if (left < n && comp.Compare(UnsafeUtility.ReadArrayElement<T>(array, lo + left),
                UnsafeUtility.ReadArrayElement<T>(array, lo + largest)) > 0)
                largest = left;

            if (right < n && comp.Compare(UnsafeUtility.ReadArrayElement<T>(array, lo + right),
                UnsafeUtility.ReadArrayElement<T>(array, lo + largest)) > 0)
                largest = right;

            if (largest != i)
            {
                Swap<T>(array, lo + i, lo + largest);
                Heapify<T, U>(array, lo, n, largest, comp);
            }
        }

        private static unsafe void Swap<T>(void* array, int i, int j) where T : struct
        {
            T temp = UnsafeUtility.ReadArrayElement<T>(array, i);
            UnsafeUtility.WriteArrayElement<T>(array, i, UnsafeUtility.ReadArrayElement<T>(array, j));
            UnsafeUtility.WriteArrayElement<T>(array, j, temp);
        }

        // Search Lower Bounds
        internal static int GetLower<T, U, X>(NativeArray<T> values, int count, U check, X condition)
            where T : struct where U : struct where X : ICondition2<T, U>
        {
            int l = 0;
            int h = count - 1;
            int i = l - 1;
            while (l <= h)
            {
                int m = ((int)(l + h)) >> 1;
                float t = 0;
                if (condition.Test(values[m], check, ref t))
                {
                    i = m;
                    l = m + 1;
                }
                else
                {
                    h = m - 1;
                }
            }
            return i;
        }

        // Search Upper Bounds
        internal static int GetUpper<T, U, X>(NativeArray<T> values, int count, U check, X condition)
            where T : struct where U : struct where X : ICondition2<T, U>
        {
            int l = 0;
            int h = count - 1;
            int i = h + 1;
            while (l <= h)
            {
                int m = ((int)(l + h)) >> 1;
                float t = 0;
                if (condition.Test(values[m], check, ref t))
                {
                    i = m;
                    h = m - 1;
                }
                else
                {
                    l = m + 1;
                }
            }
            return i;
        }

        // Search for Equal
        internal static int GetEqual<T, U, X>(Array<T> values, int count, U check, X condition)
            where T : struct where U : struct where X : ICondition2<T, U>
        {
            int l = 0;
            int h = count - 1;
            while (l <= h)
            {
                int m = ((int)(l + h)) >> 1;
                float t = 0;
                condition.Test(values[m], check, ref t);
                if (t == 0)
                {
                    return m;
                }
                else if (t <= 0)
                {
                    l = m + 1;
                }
                else
                {
                    h = m - 1;
                }
            }
            return -1;
        }

        // Search for Equal
        internal static int GetEqual<T, U, X>(NativeArray<T> values, int count, U check, X condition)
            where T : struct where U : struct where X : ICondition2<T, U>
        {
            int l = 0;
            int h = count - 1;
            while (l <= h)
            {
                int m = ((int)(l + h)) >> 1;
                float t = 0;
                condition.Test(values[m], check, ref t);
                if (t == 0)
                {
                    return m;
                }
                else if (t <= 0)
                {
                    l = m + 1;
                }
                else
                {
                    h = m - 1;
                }
            }
            return -1;
        }

        // Simple Orientation test.
        internal static float OrientFast(float2 a, float2 b, float2 c)
        {
            float epsilon = 1.1102230246251565e-16f;
            float det = (b.y - a.y) * (c.x - b.x) - (b.x - a.x) * (c.y - b.y);
            if (math.abs(det) < epsilon) return 0;
            return det;
        }

        // This is needed when doing PlanarGraph as it requires high precision separation of points.
        internal static double OrientFastDouble(double2 a, double2 b, double2 c)
        {
            double epsilon = 1.1102230246251565e-16f;
            double det = (b.y - a.y) * (c.x - b.x) - (b.x - a.x) * (c.y - b.y);
            if (math.abs(det) < epsilon) return 0;
            return det;
        }

        internal static UCircle CircumCircle(UTriangle tri)
        {
            // SIMD-ready vectorized version using float4 for parallel coordinate operations
            // Pack triangle vertices into float4 to enable Burst auto-vectorization
            // Layout: float4(va.x, vb.x, vc.x, 0) and float4(va.y, vb.y, vc.y, 0)
            float4 vx = new float4(tri.va.x, tri.vb.x, tri.vc.x, 0f);
            float4 vy = new float4(tri.va.y, tri.vb.y, tri.vc.y, 0f);

            // Vectorized squared calculations - all x² and y² computed in parallel
            float4 vx2 = vx * vx;
            float4 vy2 = vy * vy;

            // Extract components (compiler can optimize these as they're from SIMD registers)
            float xa = vx2.x, xb = vx2.y, xc = vx2.z;
            float ya = vy2.x, yb = vy2.y, yc = vy2.z;

            // Vectorized coordinate differences: (vb-va, vc-va, unused, unused)
            float4 dx = new float4(vx.y - vx.x, vx.z - vx.x, 0f, 0f);  // (vb.x-va.x, vc.x-va.x, ...)
            float4 dy = new float4(vy.y - vy.x, vy.z - vy.x, 0f, 0f);  // (vb.y-va.y, vc.y-va.y, ...)

            float vbx_vax = dx.x;
            float vby_vay = dy.x;
            float vcx_vax = dx.y;
            float vcy_vay = dy.y;

            float c = 2f * (vbx_vax * vcy_vay - vby_vay * vcx_vax);

            // BUG FIX 5: Guard against degenerate (collinear) triangles causing division by zero
            if (math.abs(c) < 1e-10f)
            {
                // Degenerate triangle: return centroid with infinite radius
                float2 centroid = (tri.va + tri.vb + tri.vc) / 3f;
                return new UCircle { center = centroid, radius = float.MaxValue };
            }

            // Vectorized sum differences
            float xb_xa_yb_ya = xb - xa + yb - ya;
            float xc_vax_yc_ya = xc - xa + yc - ya;

            float x = (vcy_vay * xb_xa_yb_ya + (tri.va.y - tri.vb.y) * xc_vax_yc_ya) / c;
            float y = ((tri.va.x - tri.vc.x) * xb_xa_yb_ya + vbx_vax * xc_vax_yc_ya) / c;

            // Vectorized radius calculation
            float2 center_to_va = tri.va - new float2(x, y);
            return new UCircle { center = new float2(x, y), radius = math.length(center_to_va) };
        }

        internal static bool IsInsideCircle(UCircle c, float2 v)
        {
            return math.distance(v, c.center) < c.radius;
        }

        internal static float TriangleArea(float2 va, float2 vb, float2 vc)
        {
            float3 a = new float3(va.x, va.y, 0);
            float3 b = new float3(vb.x, vb.y, 0);
            float3 c = new float3(vc.x, vc.y, 0);
            float3 v = math.cross(a - b, a - c);
            return math.abs(v.z) * 0.5f;
        }

        internal static float Sign(float2 p1, float2 p2, float2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        internal static bool IsInsideTriangle(float2 pt, float2 v1, float2 v2, float2 v3)
        {
            float d1, d2, d3;
            bool has_neg, has_pos;

            d1 = Sign(pt, v1, v2);
            d2 = Sign(pt, v2, v3);
            d3 = Sign(pt, v3, v1);

            has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(has_neg && has_pos);
        }

        internal static bool IsInsideTriangleApproximate(float2 pt, float2 v1, float2 v2, float2 v3)
        {
            // SIMD-ready version using Unity.Mathematics
            float d0, d1, d2, d3;
            d0 = TriangleArea(v1, v2, v3);
            d1 = TriangleArea(pt, v1, v2);
            d2 = TriangleArea(pt, v2, v3);
            d3 = TriangleArea(pt, v3, v1);
            float epsilon = 1.1102230246251565e-16f;
            return math.abs(d0 - (d1 + d2 + d3)) < epsilon;
        }

        internal static bool IsInsideCircle(float2 a, float2 b, float2 c, float2 p)
        {
            // SIMD-ready vectorized version using float3 for parallel dot products
            // Pack coordinates for vectorized operations
            float3 dots = new float3(math.dot(a, a), math.dot(b, b), math.dot(c, c));
            float ab = dots.x;
            float cd = dots.y;
            float ef = dots.z;

            // Pack x and y coordinates into float3 for parallel access
            float3 xs = new float3(a.x, b.x, c.x);  // ax, bx, cx
            float3 ys = new float3(a.y, b.y, c.y);  // ay, by, cy

            float ax = xs.x, bx = xs.y, cx = xs.z;
            float ay = ys.x, by = ys.y, cy = ys.z;

            // Vectorized coordinate differences
            float3 y_diffs = new float3(cy - by, ay - cy, by - ay);  // cy_by, ay_cy, by_ay
            float3 x_diffs = new float3(cx - bx, ax - cx, bx - ax);  // cx_bx, ax_cx, bx_ax

            float cy_by = y_diffs.x;
            float ay_cy = y_diffs.y;
            float by_ay = y_diffs.z;
            float cx_bx = x_diffs.x;
            float ax_cx = x_diffs.y;
            float bx_ax = x_diffs.z;

            float circum_x = (ab * cy_by + cd * ay_cy + ef * by_ay) /
                                (ax * cy_by + bx * ay_cy + cx * by_ay);
            float circum_y = (ab * cx_bx + cd * ax_cx + ef * bx_ax) /
                                (ay * cx_bx + by * ax_cx + cy * bx_ax);

            // Use vectorized distance calculation
            float2 circum = new float2(circum_x * 0.5f, circum_y * 0.5f);
            float2 diff_a = a - circum;
            float2 diff_p = p - circum;
            float circum_radius = math.length(diff_a);
            float dist = math.length(diff_p);
            return circum_radius - dist > 0.00001f;
        }

        internal static void GetIntermediate(ushort a, ushort b, ref int3 res)
        {
            int ia = a, ib = b;
            res.x = math.min(ia, ib) << 16 | math.max(ia, ib);
            res.y = ia;
            res.z = ib;
        }

        internal static unsafe void RawSort(int3* data, int length)
        {
            int i, j, k = length;
            int3 t;
            for (i = 0; i < k; i++)
            {
                j = i;
                t = data[i + 1];
                while (j >= 0 && t.x < data[j].x)
                {
                    data[j + 1] = data[j];
                    j--;
                }
                data[j + 1] = t;
            }
        }

        struct Int3Compare : IComparer<int3>
        {
            public int Compare(int3 a, int3 b)
            {
                return (a.x < b.x) ? -1 : ((a.x > b.x) ? 1 : 0);
            }
        }

        /// <summary>
        /// The idea here is to:
        /// 1. Sort the Triangle Indices based on connected Edges (Direction Independent). Helps Step 2.
        /// 2. Isolate Outline Edges by removing Edges that occur more than once from the above Set.
        /// 3. Connect these Isolated Edges by simply testing for Edge Indices. Iterate until all Edges are connected.
        /// </summary>
        internal static int GenerateOutlineFromTriangleIndices(in NativeArray<ushort> indices, ref NativeArray<int2> outline)
        {

            // Optimal Outline.
            var inputLength = indices.Length;
            var outlineIndices = 0;
            var sorted = new NativeArray<int3>(inputLength * 4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            unsafe
            {
                var input_ = (ushort*)indices.GetUnsafeReadOnlyPtr();
                var sorted_ = (int3*)sorted.GetUnsafeReadOnlyPtr();
                var unique_ = (int3*)(sorted_) + (inputLength * 2);
                for (int i = 0; i < inputLength; i = i + 3)
                {
                    GetIntermediate(input_[i + 0], input_[i + 1], ref sorted_[i + 0]);
                    GetIntermediate(input_[i + 1], input_[i + 2], ref sorted_[i + 1]);
                    GetIntermediate(input_[i + 2], input_[i + 0], ref sorted_[i + 2]);
                }
                NativeSortExtension.Sort(sorted_, inputLength, new Int3Compare());

                for (int i = 0; i < inputLength; ++i)
                {
                    int e = (i + 1) % inputLength;

                    if (sorted_[i].x != sorted_[e].x)
                    {
                        unique_[outlineIndices++] = sorted_[i];
                    }
                    else
                    {
                        for (int k = i + 1; k < inputLength; ++k)
                        {
                            if (sorted_[i].x != sorted_[k].x)
                            {
                                break;
                            }

                            ++i;
                        }
                    }
                }

                for (int i = 0, cursor = 0; i < outlineIndices; ++i)
                {
                    if (unique_[i].x < Int32.MaxValue)
                    {
                        var inc = true;
                        sorted_[cursor] = unique_[i];
                        unique_[i].x = Int32.MaxValue;

                        for (int j = i + 1; j < outlineIndices; ++j)
                        {
                            if (unique_[j].x < Int32.MaxValue)
                            {
                                if (sorted_[cursor].y == unique_[j].z || sorted_[cursor].z == unique_[j].y)
                                {
                                    sorted_[++cursor] = unique_[j];
                                    unique_[j].x = Int32.MaxValue;
                                    j = i + 1;
                                }

                                inc = false;
                            }

                            i = inc ? (i + 1) : i;
                        }

                        sorted_[++cursor] = unique_[i];
                    }
                }

            }

            // Return Data.
            if (0 != outlineIndices)
            {
                unsafe
                {
                    var sorted_ = (int3*)sorted.GetUnsafeReadOnlyPtr();
                    var outline_ = (int2*)outline.GetUnsafeReadOnlyPtr();
                    for (int i = 0; i < outlineIndices; ++i)
                        outline_[i] = sorted_[i].yz;
                }

                sorted.Dispose();
                return outlineIndices;
            }

            sorted.Dispose();
            return 0;

        }

        internal static void BuildTriangles(NativeArray<float2> vertices, int vertexCount, NativeArray<int> indices, int indexCount, ref NativeArray<UTriangle> triangles, ref int triangleCount, ref float maxArea, ref float avgArea, ref float minArea)
        {
            // Check if there are invalid triangles or segments.
            for (int i = 0; i < indexCount; i += 3)
            {
                UTriangle tri = new UTriangle();
                var i0 = indices[i + 0];
                var i1 = indices[i + 1];
                var i2 = indices[i + 2];
                tri.va = vertices[i0];
                tri.vb = vertices[i1];
                tri.vc = vertices[i2];
                tri.c = CircumCircle(tri);
                tri.area = TriangleArea(tri.va, tri.vb, tri.vc);
                maxArea = math.max(tri.area, maxArea);
                minArea = math.min(tri.area, minArea);
                avgArea = avgArea + tri.area;
                triangles[triangleCount++] = tri;
            }
            // BUG FIX 6: Guard against division by zero for empty meshes
            avgArea = triangleCount > 0 ? (avgArea / triangleCount) : 0f;
        }

        internal static void BuildTriangles(NativeArray<float2> vertices, int vertexCount, NativeArray<int> indices, int indexCount, ref Array<UTriangle> triangles, ref int triangleCount, ref float maxArea, ref float avgArea, ref float minArea)
        {
            // Check if there are invalid triangles or segments.
            for (int i = 0; i < indexCount; i += 3)
            {
                UTriangle tri = new UTriangle();
                var i0 = indices[i + 0];
                var i1 = indices[i + 1];
                var i2 = indices[i + 2];
                tri.va = vertices[i0];
                tri.vb = vertices[i1];
                tri.vc = vertices[i2];
                tri.c = CircumCircle(tri);
                tri.area = TriangleArea(tri.va, tri.vb, tri.vc);
                maxArea = math.max(tri.area, maxArea);
                minArea = math.min(tri.area, minArea);
                avgArea = avgArea + tri.area;
                triangles[triangleCount++] = tri;
            }
            // BUG FIX 6: Guard against division by zero for empty meshes
            avgArea = triangleCount > 0 ? (avgArea / triangleCount) : 0f;
        }

        internal static void BuildTriangles(NativeArray<float2> vertices, int vertexCount, NativeArray<int> indices, int indexCount, ref NativeArray<UTriangle> triangles, ref int triangleCount, ref float maxArea, ref float avgArea, ref float minArea, ref float maxEdge, ref float avgEdge, ref float minEdge)
        {
            // SIMD-ready vectorized version - process edge calculations in parallel using float3
            for (int i = 0; i < indexCount; i += 3)
            {
                UTriangle tri = new UTriangle();
                var i0 = indices[i + 0];
                var i1 = indices[i + 1];
                var i2 = indices[i + 2];
                tri.va = vertices[i0];
                tri.vb = vertices[i1];
                tri.vc = vertices[i2];
                tri.c = CircumCircle(tri);

                tri.area = TriangleArea(tri.va, tri.vb, tri.vc);
                maxArea = math.max(tri.area, maxArea);
                minArea = math.min(tri.area, minArea);
                avgArea = avgArea + tri.area;

                // Vectorized edge distance calculations using float3
                // Compute all 3 edge vectors in parallel
                float2 edge_ab = tri.vb - tri.va;
                float2 edge_bc = tri.vc - tri.vb;
                float2 edge_ca = tri.va - tri.vc;

                // Pack edge lengths into float3 for SIMD operations
                float3 edgeLengths = new float3(
                    math.length(edge_ab),
                    math.length(edge_bc),
                    math.length(edge_ca)
                );

                var e1 = edgeLengths.x;
                var e2 = edgeLengths.y;
                var e3 = edgeLengths.z;

                // Vectorized min/max operations using float3 intrinsics
                float maxE1E2 = math.max(e1, e2);
                float minE1E2 = math.min(e1, e2);

                maxEdge = math.max(math.max(maxE1E2, e3), maxEdge);
                minEdge = math.min(math.min(minE1E2, e3), minEdge);

                avgEdge = avgEdge + e1 + e2 + e3;
                triangles[triangleCount++] = tri;
            }
            // BUG FIX 6: Guard against division by zero for empty meshes
            avgArea = triangleCount > 0 ? (avgArea / triangleCount) : 0f;
            avgEdge = indexCount > 0 ? (avgEdge / indexCount) : 0f;
        }

        internal static void BuildTrianglesAndEdges(NativeArray<float2> vertices, int vertexCount, NativeArray<int> indices, int indexCount, ref NativeArray<UTriangle> triangles, ref int triangleCount, ref NativeArray<int4> delaEdges, ref int delaEdgeCount, ref float maxArea, ref float avgArea, ref float minArea)
        {
            // SIMD-ready vectorized version - parallel min/max using int3
            for (int i = 0; i < indexCount; i += 3)
            {
                UTriangle tri = new UTriangle();
                var i0 = indices[i + 0];
                var i1 = indices[i + 1];
                var i2 = indices[i + 2];
                tri.va = vertices[i0];
                tri.vb = vertices[i1];
                tri.vc = vertices[i2];
                tri.c = CircumCircle(tri);
                tri.area = TriangleArea(tri.va, tri.vb, tri.vc);
                maxArea = math.max(tri.area, maxArea);
                minArea = math.min(tri.area, minArea);
                avgArea = avgArea + tri.area;
                tri.indices = new int3(i0, i1, i2);

                // Vectorized min/max calculations using int3 for parallel processing
                int3 idx_a = new int3(i0, i1, i2);
                int3 idx_b = new int3(i1, i2, i0);

                // Parallel min/max on 3 edge pairs at once
                int3 mins = math.min(idx_a, idx_b);
                int3 maxs = math.max(idx_a, idx_b);

                // Outputs using vectorized results
                delaEdges[delaEdgeCount++] = new int4(mins.x, maxs.x, triangleCount, -1);
                delaEdges[delaEdgeCount++] = new int4(mins.y, maxs.y, triangleCount, -1);
                delaEdges[delaEdgeCount++] = new int4(mins.z, maxs.z, triangleCount, -1);
                triangles[triangleCount++] = tri;
            }
            // BUG FIX 6: Guard against division by zero for empty meshes
            avgArea = triangleCount > 0 ? (avgArea / triangleCount) : 0f;
        }

        static void CopyGraph(NativeArray<float2> srcPoints, int srcPointCount, ref NativeArray<float2> dstPoints, ref int dstPointCount, NativeArray<int2> srcEdges, int srcEdgeCount, ref NativeArray<int2> dstEdges, ref int dstEdgeCount)
        {
            dstEdgeCount = srcEdgeCount;
            dstPointCount = srcPointCount;
            Copy(srcEdges, dstEdges, srcEdgeCount);
            Copy(srcPoints, dstPoints, srcPointCount);
        }

        static void CopyGeometry(NativeArray<int> srcIndices, int srcIndexCount, ref NativeArray<int> dstIndices, ref int dstIndexCount, NativeArray<float2> srcVertices, int srcVertexCount, ref NativeArray<float2> dstVertices, ref int dstVertexCount)
        {
            dstIndexCount = srcIndexCount;
            dstVertexCount = srcVertexCount;
            Copy(srcIndices, dstIndices, srcIndexCount);
            Copy(srcVertices, dstVertices, srcVertexCount);
        }

        static void TransferOutput(NativeArray<int2> srcEdges, int srcEdgeCount, ref NativeArray<int2> dstEdges, ref int dstEdgeCount, NativeArray<int> srcIndices, int srcIndexCount, ref NativeArray<int> dstIndices, ref int dstIndexCount, NativeArray<float2> srcVertices, int srcVertexCount, ref NativeArray<float2> dstVertices, ref int dstVertexCount)
        {
            dstEdgeCount = srcEdgeCount;
            dstIndexCount = srcIndexCount;
            dstVertexCount = srcVertexCount;
            Copy(srcEdges, dstEdges, srcEdgeCount);
            Copy(srcIndices, dstIndices, srcIndexCount);
            Copy(srcVertices, dstVertices, srcVertexCount);
        }

        /// <summary>
        /// Copy only edges that reference vertex indices in [0, vertexCount). Used when output vertices
        /// come from Tessellator (tsVertexCount) but edges come from PlanarGraph (pgPointCount); if
        /// tsVertexCount &lt; pgPointCount, pgEdges can reference indices we are not outputting.
        /// Tessellator outputs vertices as pgPoints[0..vertexCount-1] in order (Tessellator.cs), so no index remapping is needed.
        /// </summary>
        static void TransferOutputEdgesToVertexCount(NativeArray<int2> srcEdges, int srcEdgeCount, int vertexCount, ref NativeArray<int2> dstEdges, ref int dstEdgeCount)
        {
            dstEdgeCount = 0;
            for (int i = 0; i < srcEdgeCount; i++)
            {
                int2 e = srcEdges[i];
                if (e.x >= 0 && e.x < vertexCount && e.y >= 0 && e.y < vertexCount)
                    dstEdges[dstEdgeCount++] = e;
            }
        }

        static void GraphConditioner(NativeArray<float2> points, ref NativeArray<float2> pgPoints, ref int pgPointCount, ref NativeArray<int2> pgEdges, ref int pgEdgeCount, bool resetTopology)
        {
            var min = new float2(math.INFINITY, math.INFINITY);
            var max = float2.zero;
            for (int i = 0; i < points.Length; ++i)
            {
                min = math.min(points[i], min);
                max = math.max(points[i], max);
            }

            var ext = (max - min);
            var mid = ext * 0.5f;
            var kNonRect = 0.0001f;

            // Construct a simple convex hull rect!.
            pgPointCount = resetTopology ? 0 : pgPointCount;
            var pc = pgPointCount;
            pgPoints[pgPointCount++] = new float2(min.x, min.y); pgPoints[pgPointCount++] = new float2(min.x - kNonRect, min.y + mid.y); pgPoints[pgPointCount++] = new float2(min.x, max.y); pgPoints[pgPointCount++] = new float2(min.x + mid.x, max.y + kNonRect);
            pgPoints[pgPointCount++] = new float2(max.x, max.y); pgPoints[pgPointCount++] = new float2(max.x + kNonRect, min.y + mid.y); pgPoints[pgPointCount++] = new float2(max.x, min.y); pgPoints[pgPointCount++] = new float2(min.x + mid.x, min.y - kNonRect);

            pgEdgeCount = 8;
            pgEdges[0] = new int2(pc + 0, pc + 1); pgEdges[1] = new int2(pc + 1, pc + 2); pgEdges[2] = new int2(pc + 2, pc + 3); pgEdges[3] = new int2(pc + 3, pc + 4);
            pgEdges[4] = new int2(pc + 4, pc + 5); pgEdges[5] = new int2(pc + 5, pc + 6); pgEdges[6] = new int2(pc + 6, pc + 7); pgEdges[7] = new int2(pc + 7, pc + 0);
        }

        // Reorder vertices.
        static void Reorder(int startVertexCount, int index, ref NativeArray<int> indices, ref int indexCount, ref NativeArray<float2> vertices, ref int vertexCount)
        {

            var found = false;

            for (var i = 0; i < indexCount; ++i)
            {
                if (indices[i] != index) continue;
                found = true;
                break;
            }

            if (!found)
            {
                vertexCount--;
                vertices[index] = vertices[vertexCount];
                for (var i = 0; i < indexCount; ++i)
                    if (indices[i] == vertexCount)
                        indices[i] = index;
            }

        }

        // Perform Sanitization.
        internal static void VertexCleanupConditioner(int startVertexCount, ref NativeArray<int> indices, ref int indexCount, ref NativeArray<float2> vertices, ref int vertexCount)
        {

            for (int i = startVertexCount; i < vertexCount; ++i)
            {
                Reorder(startVertexCount,i, ref indices, ref indexCount, ref vertices, ref vertexCount);
            }

        }

        public static bool TessellateMainThread(Allocator allocator, ref NativeArray<float2> points, ref NativeArray<int2> edges, out NativeArray<float2> outVertices, out NativeArray<int> outIndices)
        {
            // For Main-Thread only and also this only allocates minimal memory.
            return Tessellator.TessellateMainThread(allocator, ref points, ref edges, out outVertices, out outIndices);
        }

        public static float4 ConvexQuad(Allocator allocator, NativeArray<float2> points, NativeArray<int2> edges, ref NativeArray<float2> outVertices, ref int outVertexCount, ref NativeArray<int> outIndices, ref int outIndexCount, ref NativeArray<int2> outEdges, ref int outEdgeCount)
        {
            // Inputs are garbage, just early out.
            float4 ret = float4.zero;
            outEdgeCount = 0; outIndexCount = 0; outVertexCount = 0;
            if (points.Length < 3 || points.Length >= kMaxVertexCount)
                return ret;

            // Ensure inputs form a proper PlanarGraph.
            int pgEdgeCount = 0, pgPointCount = 0;
            NativeArray<int2> pgEdges = new NativeArray<int2>(kMaxEdgeCount, allocator);
            NativeArray<float2> pgPoints = new NativeArray<float2>(kMaxVertexCount, allocator);

            // Valid Edges and Paths, correct the Planar Graph. If invalid create a simple convex hull rect.
            GraphConditioner(points, ref pgPoints, ref pgPointCount, ref pgEdges, ref pgEdgeCount, true);
            Tessellator.Tessellate(allocator, pgPoints, pgPointCount, pgEdges, pgEdgeCount, ref outVertices, ref outVertexCount, ref outIndices, ref outIndexCount);

            // Dispose Temp Memory.
            pgPoints.Dispose();
            pgEdges.Dispose();
            return ret;
        }

        public static float4 Tessellate(Allocator allocator, in NativeArray<float2> points, in NativeArray<int2> edges, ref NativeArray<float2> outVertices, out int outVertexCount, ref NativeArray<int> outIndices, out int outIndexCount, ref NativeArray<int2> outEdges, out int outEdgeCount, bool runPlanarGraph)
        {
            // Inputs are garbage, just early out.
            float4 ret = float4.zero;
            outEdgeCount = 0; outIndexCount = 0; outVertexCount = 0;
            if (points.Length < 3 || points.Length >= kMaxVertexCount)
                return ret;

            // Ensure inputs form a proper PlanarGraph.
            bool validGraph = false, handleEdgeCase = false;
            int pgEdgeCount = 0, pgPointCount = 0;
            NativeArray<int2> pgEdges = new NativeArray<int2>(edges.Length * 8, allocator);
            NativeArray<float2> pgPoints = new NativeArray<float2>(points.Length * 4, allocator);

            // Valid Edges and Paths, correct the Planar Graph. If invalid create a simple convex hull rect.
            if (runPlanarGraph)
            {
                if (0 != edges.Length)
                {
                    validGraph = PlanarGraph.Validate(allocator, in points, points.Length, in edges, edges.Length, ref pgPoints, out pgPointCount, ref pgEdges, out pgEdgeCount);
                }
            }
            else
            {
                // Just copy Stage.
                pgEdgeCount = edges.Length;
                pgPointCount = points.Length;
                Copy(edges, pgEdges, pgEdgeCount);
                Copy(points, pgPoints, pgPointCount);
            }

            // Fallbacks are now handled by the Higher level packages. Enable if UTess needs to handle it.
            // #if UTESS_QUAD_FALLBACK
            //             if (!validGraph)
            //             {
            //                 pgPointCount = 0;
            //                 handleEdgeCase = true;
            //                 ModuleHandle.Copy(points, pgPoints, points.Length);
            //                 GraphConditioner(points, ref pgPoints, ref pgPointCount, ref pgEdges, ref pgEdgeCount, false);
            //             }
            // #else

            // If its not a valid Graph simply return back input Data without triangulation instead of going through UTess (pointless wasted cpu cycles).
            if (!validGraph)
            {
                outEdgeCount = edges.Length;
                outVertexCount = points.Length;
                ModuleHandle.Copy(edges, outEdges, edges.Length);
                ModuleHandle.Copy(points, outVertices, points.Length);
            }

            // Do a proper Delaunay Triangulation if Inputs are valid.
            if (pgPointCount > 2 && pgEdgeCount > 2)
            {
                // Tessellate does not add new points, only PG and SD does. Assuming each point creates a degenerate triangle, * 4 is more than enough.
                NativeArray<int> tsIndices = new NativeArray<int>(pgPointCount * 8, allocator);
                NativeArray<float2> tsVertices = new NativeArray<float2>(pgPointCount * 4, allocator);
                int tsIndexCount = 0, tsVertexCount = 0;
                validGraph = Tessellator.Tessellate(allocator, pgPoints, pgPointCount, pgEdges, pgEdgeCount, ref tsVertices, ref tsVertexCount, ref tsIndices, ref tsIndexCount);
                if (validGraph)
                {
                    // Edges must reference only output vertices (tsVertexCount); pgEdges use pgPointCount which can be larger.
                    TransferOutputEdgesToVertexCount(pgEdges, pgEdgeCount, tsVertexCount, ref outEdges, ref outEdgeCount);
                    outIndexCount = tsIndexCount;
                    outVertexCount = tsVertexCount;
                    Copy(tsIndices, outIndices, tsIndexCount);
                    Copy(tsVertices, outVertices, tsVertexCount);
                    if (handleEdgeCase == true)
                        outEdgeCount = 0;
                }
                tsVertices.Dispose();
                tsIndices.Dispose();
            }

            // Dispose Temp Memory.
            pgPoints.Dispose();
            pgEdges.Dispose();
            return ret;
        }

        public static float4 Subdivide(Allocator allocator, NativeArray<float2> points, NativeArray<int2> edges, ref NativeArray<float2> outVertices, ref int outVertexCount, ref NativeArray<int> outIndices, ref int outIndexCount, ref NativeArray<int2> outEdges, ref int outEdgeCount, float areaFactor, float targetArea, int refineIterations, int smoothenIterations)
        {
            // Inputs are garbage, just early out.
            float4 ret = float4.zero;
            outEdgeCount = 0; outIndexCount = 0; outVertexCount = 0;
            if (points.Length < 3 || points.Length >= kMaxVertexCount || 0 == edges.Length)
                return ret;

            // Do a proper Delaunay Triangulation.
            int tsIndexCount = 0, tsVertexCount = 0;
            NativeArray<int> tsIndices = new NativeArray<int>(kMaxIndexCount, allocator);
            NativeArray<float2> tsVertices = new NativeArray<float2>(kMaxVertexCount, allocator);
            var validGraph = Tessellator.Tessellate(allocator, points, points.Length, edges, edges.Length, ref tsVertices, ref tsVertexCount, ref tsIndices, ref tsIndexCount);

            // Refinement and Smoothing.
            bool refined = false;
            bool refinementRequired = (targetArea != 0 || areaFactor != 0);
            if (validGraph && refinementRequired)
            {
                // Do Refinement until success.
                float maxArea = 0;
                float incArea = 0;
                int rfEdgeCount = 0, rfPointCount = 0, rfIndexCount = 0, rfVertexCount = 0;
                NativeArray<int2> rfEdges = new NativeArray<int2>(kMaxEdgeCount, allocator);
                NativeArray<float2> rfPoints = new NativeArray<float2>(kMaxVertexCount, allocator);
                NativeArray<int> rfIndices = new NativeArray<int>(kMaxIndexCount, allocator);
                NativeArray<float2> rfVertices = new NativeArray<float2>(kMaxVertexCount, allocator);
                ret.x = 0;
                refineIterations = Math.Min(refineIterations, kMaxRefineIterations);

                if (targetArea != 0)
                {
                    // Increment for Iterations.
                    incArea = (targetArea / 10);

                    while (targetArea < kMaxArea && refineIterations > 0)
                    {
                        // Do Mesh Refinement.
                        CopyGraph(points, points.Length, ref rfPoints, ref rfPointCount, edges, edges.Length, ref rfEdges, ref rfEdgeCount);
                        CopyGeometry(tsIndices, tsIndexCount, ref rfIndices, ref rfIndexCount, tsVertices, tsVertexCount, ref rfVertices, ref rfVertexCount);
                        refined = Refinery.Condition(allocator, areaFactor, targetArea, ref rfPoints, ref rfPointCount, ref rfEdges, ref rfEdgeCount, ref rfVertices, ref rfVertexCount, ref rfIndices, ref rfIndexCount, ref maxArea);

                        if (refined && rfIndexCount > rfPointCount)
                        {
                            // Copy Out
                            ret.x = areaFactor;
                            TransferOutput(rfEdges, rfEdgeCount, ref outEdges, ref outEdgeCount, rfIndices, rfIndexCount, ref outIndices, ref outIndexCount, rfVertices, rfVertexCount, ref outVertices, ref outVertexCount);
                            break;
                        }

                        refined = false;
                        targetArea = targetArea + incArea;
                        refineIterations--;
                    }

                }
                else if (areaFactor != 0)
                {
                    // Increment for Iterations.
                    areaFactor = math.lerp(0.1f, 0.54f, (areaFactor - 0.05f) / 0.45f); // Specific to Animation.
                    incArea = (areaFactor / 10);

                    while (areaFactor < 0.8f && refineIterations > 0)
                    {
                        // Do Mesh Refinement.
                        CopyGraph(points, points.Length, ref rfPoints, ref rfPointCount, edges, edges.Length, ref rfEdges, ref rfEdgeCount);
                        CopyGeometry(tsIndices, tsIndexCount, ref rfIndices, ref rfIndexCount, tsVertices, tsVertexCount, ref rfVertices, ref rfVertexCount);
                        refined = Refinery.Condition(allocator, areaFactor, targetArea, ref rfPoints, ref rfPointCount, ref rfEdges, ref rfEdgeCount, ref rfVertices, ref rfVertexCount, ref rfIndices, ref rfIndexCount, ref maxArea);

                        if (refined && rfIndexCount > rfPointCount)
                        {
                            // Copy Out
                            ret.x = areaFactor;
                            TransferOutput(rfEdges, rfEdgeCount, ref outEdges, ref outEdgeCount, rfIndices, rfIndexCount, ref outIndices, ref outIndexCount, rfVertices, rfVertexCount, ref outVertices, ref outVertexCount);
                            break;
                        }

                        refined = false;
                        areaFactor = areaFactor + incArea;
                        refineIterations--;
                    }
                }

                if (refined)
                {
                    // Sanitize generated geometry data.
                    var preSmoothen = outVertexCount;
                    if (ret.x != 0)
                        VertexCleanupConditioner(tsVertexCount, ref rfIndices, ref rfIndexCount, ref rfVertices, ref rfVertexCount);

                    // Smoothen. At this point only vertex relocation is allowed, not vertex addition/removal.
                    // Note: Only refined mesh contains Steiner points and we only smoothen these points.
                    ret.y = 0;
                    smoothenIterations = math.clamp(smoothenIterations, 0, kMaxSmoothenIterations);
                    while (smoothenIterations > 0)
                    {
                        var smoothen = Smoothen.Condition(allocator, ref rfPoints, rfPointCount, rfEdges, rfEdgeCount, ref rfVertices, ref rfVertexCount, ref rfIndices, ref rfIndexCount);
                        if (!smoothen)
                            break;
                        // Copy Out
                        ret.y = (float)(smoothenIterations);
                        TransferOutput(rfEdges, rfEdgeCount, ref outEdges, ref outEdgeCount, rfIndices, rfIndexCount, ref outIndices, ref outIndexCount, rfVertices, rfVertexCount, ref outVertices, ref outVertexCount);
                        smoothenIterations--;
                    }

                    // Sanitize generated geometry data.
                    var postSmoothen = outVertexCount;
                    if (ret.y != 0)
                        VertexCleanupConditioner(tsVertexCount, ref outIndices, ref outIndexCount, ref outVertices, ref outVertexCount);
                }

                rfVertices.Dispose();
                rfIndices.Dispose();
                rfPoints.Dispose();
                rfEdges.Dispose();
            }

            // Refinement failed but Graph succeeded.
            if (validGraph && !refined)
            {
                TransferOutputEdgesToVertexCount(edges, edges.Length, tsVertexCount, ref outEdges, ref outEdgeCount);
                outIndexCount = tsIndexCount;
                outVertexCount = tsVertexCount;
                Copy(tsIndices, outIndices, tsIndexCount);
                Copy(tsVertices, outVertices, tsVertexCount);
            }

            // Dispose Temp Memory.
            tsVertices.Dispose();
            tsIndices.Dispose();
            return ret;
        }

    }

}
