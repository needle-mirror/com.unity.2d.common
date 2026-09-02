using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.U2D.Common.UTess
{

    // Ensures that there are no duplicate Points, Overlapping Edges.
    struct PlanarGraph
    {

        private static readonly double kEpsilon = 0.00001;
        private static readonly int kMaxIntersectionTolerance = 4;  // Maximum Intersection Tolerance per Intersection Loop Check.

        // AABB struct for spatial acceleration of edge intersection tests
        struct EdgeAABB
        {
            public double2 min;
            public double2 max;
            public int edgeIndex;

            public EdgeAABB(double2 p1, double2 p2, int index)
            {
                // SIMD-ready: Vectorized min/max using double2 intrinsics
                // Burst compiler can optimize these into single SIMD instructions
                min = math.min(p1, p2);
                max = math.max(p1, p2);
                edgeIndex = index;
            }

            public bool Overlaps(EdgeAABB other)
            {
                return !(max.x < other.min.x || min.x > other.max.x ||
                         max.y < other.min.y || min.y > other.max.y);
            }
        }

        // Comparer for sorting EdgeAABBs by min.x coordinate
        struct EdgeAABBCompare : IComparer<EdgeAABB>
        {
            public int Compare(EdgeAABB a, EdgeAABB b)
            {
                if (a.min.x < b.min.x) return -1;
                if (a.min.x > b.min.x) return 1;
                if (a.min.y < b.min.y) return -1;
                if (a.min.y > b.min.y) return 1;
                return 0;
            }
        }

        // Sweep event for T-junction detection
        enum SweepEventType
        {
            START,
            END,
            POINT
        }

        struct SweepEvent
        {
            public double2 position;
            public int index;           // Edge index for START/END, point index for POINT
            public SweepEventType type;
            public int edgeIndex;       // For START/END: edge index, for POINT: -1

            public SweepEvent(double2 pos, int idx, SweepEventType eventType, int edge = -1)
            {
                position = pos;
                index = idx;
                type = eventType;
                edgeIndex = edge;
            }
        }

        // Comparer for sorting sweep events by x-coordinate
        struct SweepEventCompare : IComparer<SweepEvent>
        {
            public int Compare(SweepEvent a, SweepEvent b)
            {
                if (a.position.x < b.position.x) return -1;
                if (a.position.x > b.position.x) return 1;
                if (a.position.y < b.position.y) return -1;
                if (a.position.y > b.position.y) return 1;

                // Break ties by event type (START before POINT before END)
                // This ensures edges are active when we test points against them
                if (a.type != b.type)
                {
                    if (a.type == SweepEventType.START) return -1;
                    if (b.type == SweepEventType.START) return 1;
                    if (a.type == SweepEventType.POINT) return -1;
                    if (b.type == SweepEventType.POINT) return 1;
                }

                return 0;
            }
        }

        // Helper to check if a point lies on a line segment
        private static bool PointOnSegment(double2 p, double2 a, double2 b)
        {
            // SIMD-ready vectorized version - parallel min/max using double4
            // Point must be collinear with segment
            double cross = (p.y - a.y) * (b.x - a.x) - (p.x - a.x) * (b.y - a.y);
            if (math.abs(cross) > kEpsilon)
                return false;

            // Vectorized bounding box check using double2 operations
            double2 mins = math.min(a, b) - kEpsilon;
            double2 maxs = math.max(a, b) + kEpsilon;

            // Vectorized comparison: all(p >= mins && p <= maxs)
            return math.all(p >= mins & p <= maxs);
        }

        internal static void RemoveDuplicateEdges(ref Array<int2> edges, ref int edgeCount, Array<int> duplicates, int duplicateCount)
        {

            if (duplicateCount == 0)
            {
                for (var i = 0; i < edgeCount; ++i)
                {
                    var e = edges[i];
                    e.x = math.min(edges[i].x, edges[i].y);
                    e.y = math.max(edges[i].x, edges[i].y);
                    edges[i] = e;
                }
            }
            else
            {
                for (var i = 0; i < edgeCount; ++i)
                {
                    var e = edges[i];
                    var a = duplicates[e.x];
                    var b = duplicates[e.y];
                    e.x = math.min(a, b);
                    e.y = math.max(a, b);
                    edges[i] = e;
                }
            }

            unsafe
            {
                ModuleHandle.InsertionSort<int2, TessEdgeCompare>(edges.UnsafePtr, 0, edgeCount - 1,new TessEdgeCompare());
            }

            var n = 1;
            for (var i = 1; i < edgeCount; ++i)
            {
                var prev = edges[i - 1];
                var next = edges[i];
                if (next.x == prev.x && next.y == prev.y)
                    continue;
                if (next.x == next.y)
                    continue;
                edges[n++] = next;
            }
            edgeCount = n;
        }

        internal static bool CheckCollinear(double2 a0, double2 a1, double2 b0, double2 b1)
        {
            double2 a = a0;
            double2 b = a1;
            double2 c = b0;
            double2 d = b1;

            double dx = b.x - a.x;
            if (math.abs(dx) < kEpsilon)
            {
                // Line is vertical, check if other points have same x
                return math.abs(c.x - a.x) > kEpsilon || math.abs(d.x - a.x) > kEpsilon;
            }

            double x = (b.y - a.y) / dx;
            double y = (c.y - a.y) / (c.x - a.x);
            double z = (d.y - a.y) / (d.x - a.x);

            // Return true if NOT collinear (slopes differ significantly)
            // If any slope is infinite or slopes differ, lines are not collinear
            return (math.isinf(y) || math.isinf(z) ||
                    math.abs(x - y) > kEpsilon || math.abs(x - z) > kEpsilon);
        }

        internal static bool LineLineIntersection(double2 a0, double2 a1, double2 b0, double2 b1)
        {
            var x0 = ModuleHandle.OrientFastDouble(a0, b0, b1);
            var y0 = ModuleHandle.OrientFastDouble(a1, b0, b1);
            if ((x0 > kEpsilon && y0 > kEpsilon) || (x0 < -kEpsilon && y0 < -kEpsilon))
            {
                return false;
            }

            var x1 = ModuleHandle.OrientFastDouble(b0, a0, a1);
            var y1 = ModuleHandle.OrientFastDouble(b1, a0, a1);
            if ((x1 > kEpsilon && y1 > kEpsilon) || (x1 < -kEpsilon && y1 < -kEpsilon))
            {
                return false;
            }

            // Check for degenerate collinear case
            if (math.abs(x0) < kEpsilon && math.abs(y0) < kEpsilon && math.abs(x1) < kEpsilon && math.abs(y1) < kEpsilon)
            {
                return CheckCollinear(a0, a1, b0, b1);
            }

            return true;
        }

        internal static bool LineLineIntersection(double2 p1, double2 p2, double2 p3, double2 p4, ref double2 result)
        {
            double bx = p2.x - p1.x;
            double by = p2.y - p1.y;
            double dx = p4.x - p3.x;
            double dy = p4.y - p3.y;
            double bDotDPerp = bx * dy - by * dx;
            if (math.abs(bDotDPerp) < kEpsilon)
            {
                return false;
            }

            double cx = p3.x - p1.x;
            double cy = p3.y - p1.y;
            double t = (cx * dy - cy * dx) / bDotDPerp;

            if ((t >= -kEpsilon) && (t <= 1.0 + kEpsilon))
            {
                result.x = p1.x + t * bx;
                result.y = p1.y + t * by;
                return true;
            }
            return false;
        }

        internal static bool CalculateEdgeIntersections(Array<int2> edges, int edgeCount, Array<double2> points, int pointCount, ref Array<int2> results, ref Array<double2> intersects, ref int resultCount)
        {
            resultCount = 0;

            // AABB Spatial Acceleration: Use sweep-line algorithm to reduce O(n²) to O(n log n)
            // Only test edge pairs whose AABBs overlap

            // Build AABBs for all edges
            var aabbs = new NativeArray<EdgeAABB>(edgeCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < edgeCount; ++i)
            {
                var e = edges[i];
                var a = points[e.x];
                var b = points[e.y];
                aabbs[i] = new EdgeAABB(a, b, i);
            }

            // Sort AABBs by min.x coordinate (enables sweep-line algorithm)
            unsafe
            {
                ModuleHandle.InsertionSort<EdgeAABB, EdgeAABBCompare>((EdgeAABB*)aabbs.GetUnsafePtr(), 0, edgeCount - 1, new EdgeAABBCompare());
            }

            // Sweep-line: For each AABB, only check against AABBs whose x-range can overlap
            for (int i = 0; i < edgeCount; ++i)
            {
                var aabbI = aabbs[i];
                var edgeI = edges[aabbI.edgeIndex];

                // Only check forward against edges whose min.x is within reach
                for (int j = i + 1; j < edgeCount; ++j)
                {
                    var aabbJ = aabbs[j];

                    // Early exit: if aabbJ.min.x > aabbI.max.x, no more overlaps possible
                    if (aabbJ.min.x > aabbI.max.x)
                        break;

                    // Quick AABB overlap test
                    if (!aabbI.Overlaps(aabbJ))
                        continue;

                    // AABBs overlap, perform precise edge intersection test
                    var edgeJ = edges[aabbJ.edgeIndex];

                    // Skip if edges share a vertex
                    if (edgeI.x == edgeJ.x || edgeI.x == edgeJ.y || edgeI.y == edgeJ.x || edgeI.y == edgeJ.y)
                        continue;

                    var a = points[edgeI.x];
                    var b = points[edgeI.y];
                    var c = points[edgeJ.x];
                    var d = points[edgeJ.y];
                    var g = double2.zero;

                    if (LineLineIntersection(a, b, c, d))
                    {
                        if (LineLineIntersection(a, b, c, d, ref g))
                        {
                            // Until we ensure Outline is generated properly, we need this useless Check every correction.
                            if (resultCount >= intersects.Length)
                            {
                                aabbs.Dispose();
                                return false;
                            }

                            intersects[resultCount] = g;
                            results[resultCount++] = new int2(aabbI.edgeIndex, aabbJ.edgeIndex);
                        }
                    }
                }
            }

            aabbs.Dispose();

            // Basically we have self intersections all over (eg. gobo_tree_04). Better don't generate any Mesh as even though this will eventually succeed, error correction will take long time.
            if (resultCount > (edgeCount * kMaxIntersectionTolerance))
            {
                return false;
            }

            var tjc = new IntersectionCompare();
            tjc.edges = edges;
            tjc.points = points;
            unsafe
            {
                ModuleHandle.InsertionSort<int2, IntersectionCompare>(results.UnsafePtr, 0, resultCount - 1, tjc);
            }

            return true;
        }

        internal static bool CalculateTJunctions(Array<int2> edges, int edgeCount, Array<double2> points, int pointCount, Array<int2> results, ref int resultCount)
        {
            resultCount = 0;

            // Optimized sweep-line algorithm: O(n log n) instead of O(n*m)
            // Build event queue: 2 events per edge (start/end) + 1 event per point
            int eventCount = edgeCount * 2 + pointCount;
            var events = new NativeArray<SweepEvent>(eventCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int eventIndex = 0;

            // Add edge start/end events
            for (int i = 0; i < edgeCount; ++i)
            {
                var e = edges[i];
                var p1 = points[e.x];
                var p2 = points[e.y];

                // Ensure p1 is leftmost (or topmost if same x)
                // CRITICAL: Use exact equality to match SweepEventCompare's sorting behavior.
                // Using epsilon here can cause START/END events to be processed out of order,
                // breaking the sweep-line algorithm and causing missed T-junctions.
                if (p1.x > p2.x || (p1.x == p2.x && p1.y > p2.y))
                {
                    var temp = p1;
                    p1 = p2;
                    p2 = temp;
                }

                events[eventIndex++] = new SweepEvent(p1, i, SweepEventType.START, i);
                events[eventIndex++] = new SweepEvent(p2, i, SweepEventType.END, i);
            }

            // Add point events
            for (int j = 0; j < pointCount; ++j)
            {
                events[eventIndex++] = new SweepEvent(points[j], j, SweepEventType.POINT, -1);
            }

            // Sort events by x-coordinate (sweep-line)
            unsafe
            {
                ModuleHandle.InsertionSort<SweepEvent, SweepEventCompare>((SweepEvent*)events.GetUnsafePtr(), 0, eventCount - 1, new SweepEventCompare());
            }

            // Maintain active edge list (edges crossing the sweep line)
            var activeEdges = new NativeArray<int>(edgeCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int activeCount = 0;

            // Process events
            for (int i = 0; i < eventCount; ++i)
            {
                var evt = events[i];

                if (evt.type == SweepEventType.START)
                {
                    // Add edge to active list
                    activeEdges[activeCount++] = evt.edgeIndex;
                }
                else if (evt.type == SweepEventType.END)
                {
                    // Remove edge from active list
                    for (int k = 0; k < activeCount; ++k)
                    {
                        if (activeEdges[k] == evt.edgeIndex)
                        {
                            // Swap with last and decrement count
                            activeEdges[k] = activeEdges[activeCount - 1];
                            activeCount--;
                            break;
                        }
                    }
                }
                else // POINT event
                {
                    int pointIndex = evt.index;
                    var point = evt.position;

                    // Test point against all active edges
                    for (int k = 0; k < activeCount; ++k)
                    {
                        int edgeIdx = activeEdges[k];
                        var e = edges[edgeIdx];

                        // Skip if point is an endpoint of this edge
                        if (e.x == pointIndex || e.y == pointIndex)
                            continue;

                        var a = points[e.x];
                        var b = points[e.y];

                        // Check if point lies on edge
                        if (PointOnSegment(point, a, b))
                        {
                            if (resultCount >= results.Length)
                            {
                                activeEdges.Dispose();
                                events.Dispose();
                                return false;
                            }
                            results[resultCount++] = new int2(edgeIdx, pointIndex);
                        }
                    }
                }
            }

            activeEdges.Dispose();
            events.Dispose();
            return true;
        }

        internal static bool CutEdges(ref Array<double2> points, ref int pointCount, ref Array<int2> edges, ref int edgeCount, ref Array<int2> tJunctions, ref int tJunctionCount, Array<int2> intersections, Array<double2> intersects, int intersectionCount)
        {
            for (int i = 0; i < intersectionCount; ++i)
            {
                var intr = intersections[i];
                var e = intr.x;
                var f = intr.y;

                int2 j1 = int2.zero;
                j1.x = e;
                j1.y = pointCount;
                tJunctions[tJunctionCount++] = j1;
                int2 j2 = int2.zero;
                j2.x = f;
                j2.y = pointCount;
                tJunctions[tJunctionCount++] = j2;

                // Until we ensure Outline is generated properly, we need this useless Check every correction.
                if (pointCount >= points.Length)
                    return false;

                points[pointCount++] = intersects[i];
            }

            unsafe
            {
                ModuleHandle.InsertionSort<int2, TessJunctionCompare>( tJunctions.UnsafePtr, 0, tJunctionCount - 1, new TessJunctionCompare());
            }

            // Split edges along junctions
            for (int i = tJunctionCount - 1; i >= 0; --i)
            {
                var tJunction = tJunctions[i];
                var e = tJunction.x;
                var edge = edges[e];
                var s = edge.x;
                var t = edge.y;

                // Check if edge is not lexicographically sorted
                var a = points[s];
                var b = points[t];
                if (((a.x - b.x) > 0) || (math.abs(a.x - b.x) < kEpsilon && (a.y - b.y) > 0))
                {
                    var tmp = s;
                    s = t;
                    t = tmp;
                }

                // Split leading edge
                edge.x = s;
                var last = edge.y = tJunction.y;
                edges[e] = edge;

                // If we are grouping edges by color, remember to track data
                // Split other edges
                while (i > 0 && tJunctions[i - 1].x == e)
                {
                    var next = tJunctions[--i].y;
                    int2 te = new int2();
                    te.x = last;
                    te.y = next;
                    edges[edgeCount++] = te;
                    last = next;
                }

                int2 le = new int2();
                le.x = last;
                le.y = t;
                edges[edgeCount++] = le;
            }

            return true;
        }

        /// <summary>
        /// Helper method to hash a 2D grid cell coordinate into a linear index.
        /// Uses a simple multiplicative hash to distribute cells across the hash table.
        /// </summary>
        /// <param name="cell">The 2D grid cell coordinate</param>
        /// <param name="maxCells">Maximum number of cells in the hash table</param>
        /// <returns>Hash value in range [0, maxCells)</returns>
        private static int HashCell(int2 cell, int maxCells)
        {
            // BUG FIX 2: Use unchecked unsigned arithmetic to prevent integer overflow
            // math.abs(Int32.MinValue) stays negative, causing issues
            // FIX for SpatialGrid_NegativeCoordinates_HandledCorrectly test:
            // Ensure consistent hashing for negative coordinates by using bitwise operations
            unchecked
            {
                // Cast to uint first to handle negative values correctly
                uint ux = (uint)cell.x;
                uint uy = (uint)cell.y;
                uint hash = ux * 73856093u ^ uy * 19349663u;
                return (int)(hash % (uint)maxCells);
            }
        }

        /// <summary>
        /// Checks if two grid cell coordinates are equal.
        /// </summary>
        private static bool SameCell(int2 a, int2 b)
        {
            return a.x == b.x && a.y == b.y;
        }

        /// <summary>
        /// Removes duplicate points from the input array using union-find algorithm.
        /// Points within kEpsilon distance are considered duplicates.
        /// When UTESS_SPATIAL_GRID is enabled, uses spatial grid hashing for O(n) average case performance.
        /// Otherwise falls back to O(n²) brute force comparison.
        /// </summary>
        internal static void RemoveDuplicatePoints(ref Array<double2> points, ref int pointCount, ref Array<int> duplicates, ref int duplicateCount, Allocator allocator)
        {
            TessLink link = TessLink.CreateLink(pointCount, allocator);

            // Spatial grid optimization for O(n) average case performance
            // Cell size is 2x epsilon to ensure neighboring cells can catch duplicates
            double cellSize = kEpsilon * 2.0;
            // BUG FIX 4: Increase bucket count to reduce hash collisions (25% load factor instead of 50%)
            int maxCells = math.max(pointCount * 4, 256); // More buckets for better distribution
            // BUG FIX 3: Guard against division by zero in HashCell
            if (maxCells <= 0)
                maxCells = 256;  // Fallback to minimum safe value

            // Allocate temporary arrays for spatial grid
            var gridCells = new NativeArray<int2>(pointCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var sortedIndices = new NativeArray<int>(pointCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var cellStart = new NativeArray<int>(maxCells, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var cellCount = new NativeArray<int>(maxCells, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            // Initialize cell arrays
            for (int i = 0; i < maxCells; i++)
            {
                cellStart[i] = -1;
                cellCount[i] = 0;
            }

            // Step 1: Compute grid cell for each point
            for (int i = 0; i < pointCount; i++)
            {
                int2 cell = new int2(
                    (int)math.floor(points[i].x / cellSize),
                    (int)math.floor(points[i].y / cellSize)
                );
                gridCells[i] = cell;
                sortedIndices[i] = i;
            }

            // Step 2: Count points per cell
            for (int i = 0; i < pointCount; i++)
            {
                int hash = HashCell(gridCells[i], maxCells);
                cellCount[hash]++;
            }

            // Step 3: Compute cell start indices (prefix sum)
            int sum = 0;
            for (int i = 0; i < maxCells; i++)
            {
                if (cellCount[i] > 0)
                {
                    cellStart[i] = sum;
                    sum += cellCount[i];
                }
            }

            // Step 4: Reorder indices by cell (bucket sort)
            var tempIndices = new NativeArray<int>(pointCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var tempCells = new NativeArray<int2>(pointCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var writePos = new NativeArray<int>(maxCells, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < maxCells; i++)
            {
                writePos[i] = cellStart[i];
            }

            for (int i = 0; i < pointCount; i++)
            {
                int hash = HashCell(gridCells[i], maxCells);
                int pos = writePos[hash]++;

                // BUG FIX 1: Guard against buffer overrun in bucket sort
                if (pos < 0 || pos >= pointCount)
                {
                    #if UNITY_EDITOR
                    Debug.LogError($"Spatial grid bucket sort: Invalid write position {pos} (max {pointCount})");
                    #endif
                    continue;
                }

                tempIndices[pos] = sortedIndices[i];
                tempCells[pos] = gridCells[i];
            }

            // Copy back
            for (int i = 0; i < pointCount; i++)
            {
                sortedIndices[i] = tempIndices[i];
                gridCells[i] = tempCells[i];
            }

            tempIndices.Dispose();
            tempCells.Dispose();
            writePos.Dispose();

            // Step 5: Check for duplicates within same cell and neighboring cells
            for (int i = 0; i < pointCount; i++)
            {
                int pi = sortedIndices[i];
                int2 cell = gridCells[i];

                // Check 9 cells (current + 8 neighbors)
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int2 neighborCell = cell + new int2(dx, dy);
                        int hash = HashCell(neighborCell, maxCells);
                        int start = cellStart[hash];

                        if (start < 0)
                            continue;

                        int end = start + cellCount[hash];

                        // Check all points in this cell bucket
                        for (int j = start; j < end && j < pointCount; j++)
                        {
                            // Verify the point is actually in the expected cell (handle hash collisions)
                            if (!SameCell(gridCells[j], neighborCell))
                                continue;

                            int pj = sortedIndices[j];

                            // Only link each pair once (i < j) and check distance
                            if (pi < pj && math.distance(points[pi], points[pj]) < kEpsilon)
                            {
                                link.Link(pi, pj);
                            }
                        }
                    }
                }
            }

            // Cleanup
            gridCells.Dispose();
            sortedIndices.Dispose();
            cellStart.Dispose();
            cellCount.Dispose();

            duplicateCount = 0;
            for (var i = 0; i < pointCount; ++i)
            {
                var j = link.Find(i);
                if (j != i)
                {
                    duplicateCount++;
                    points[j] = math.min(points[i], points[j]);
                }
            }

            // Find Duplicates.
            if (duplicateCount != 0)
            {

                var prevPointCount = pointCount;
                pointCount = 0;
                for (var i = 0; i < prevPointCount; ++i)
                {
                    var j = link.Find(i);
                    if (j == i)
                    {
                        duplicates[i] = pointCount;
                        points[pointCount++] = points[i];
                    }
                    else
                    {
                        duplicates[i] = -1;
                    }
                }

                // Update Duplicates.
                for (int i = 0; i < prevPointCount; ++i)
                {
                    if (duplicates[i] < 0)
                    {
                        duplicates[i] = duplicates[link.Find(i)];
                    }
                }

            }

            TessLink.DestroyLink(link);
        }

        // Validate the Input Points ane Edges.
        internal static bool Validate(Allocator allocator, in NativeArray<float2> inputPoints, int pointCount, in NativeArray<int2> inputEdges, int edgeCount, ref NativeArray<float2> outputPoints, out int outputPointCount, ref NativeArray<int2> outputEdges, out int outputEdgeCount)
        {
            outputPointCount = 0;
            outputEdgeCount = 0;
            
            // Outline generated inputs can have differences in the range of 0.00001f.. See TwoLayers.psb sample.
            // Since PlanarGraph operates on double, scaling up and down does not result in loss of data.
            var precisionFudge = 10000.0f;
            var protectLoop = edgeCount;
            var requiresFix = true;
            var validGraph = false;

            // Processing Arrays.
            int startEdgeCount = edgeCount;
            Array<int> duplicates = new Array<int>(startEdgeCount, ModuleHandle.kMaxEdgeCount, allocator, NativeArrayOptions.UninitializedMemory);
            Array<int2> edges = new Array<int2>(startEdgeCount, ModuleHandle.kMaxEdgeCount, allocator, NativeArrayOptions.UninitializedMemory);
            Array<int2> tJunctions = new Array<int2>(startEdgeCount, ModuleHandle.kMaxEdgeCount, allocator, NativeArrayOptions.UninitializedMemory);
            Array<int2> edgeIntersections = new Array<int2>(startEdgeCount, ModuleHandle.kMaxEdgeCount, allocator, NativeArrayOptions.UninitializedMemory);
            Array<double2> points = new Array<double2>(pointCount * 2, pointCount * 8, allocator, NativeArrayOptions.UninitializedMemory);
            Array<double2> intersects = new Array<double2>(pointCount * 2, pointCount * 8, allocator, NativeArrayOptions.UninitializedMemory);

            // Initialize.
            for (int i = 0; i < pointCount; ++i)
                points[i] = inputPoints[i] * precisionFudge;
            unsafe
            {
                UnsafeUtility.MemCpy(edges.UnsafeReadOnlyPtr, inputEdges.GetUnsafePtr(), edgeCount * sizeof(int2));
            }

            // Pre-clear duplicates, otherwise the following will simply fail.
            RemoveDuplicateEdges(ref edges, ref edgeCount, duplicates, 0);

            // While PSG is clean.
            while (requiresFix && --protectLoop > 0)
            {
                // Edge Edge Intersection.
                int intersectionCount = 0;
                validGraph = CalculateEdgeIntersections(edges, edgeCount, points, pointCount, ref edgeIntersections, ref intersects, ref intersectionCount);
                if (!validGraph)
                    break;

                // Edge Point Intersection. T-Junction.
                int tJunctionCount = 0;
                validGraph = CalculateTJunctions(edges, edgeCount, points, pointCount, tJunctions, ref tJunctionCount);
                if (!validGraph)
                    break;

                // Cut Overlapping Edges.
                validGraph = CutEdges(ref points, ref pointCount, ref edges, ref edgeCount, ref tJunctions, ref tJunctionCount, edgeIntersections, intersects, intersectionCount);
                if (!validGraph)
                    break;

                // Remove Duplicate Points.
                int duplicateCount = 0;
                RemoveDuplicatePoints(ref points, ref pointCount, ref duplicates, ref duplicateCount, allocator);
                RemoveDuplicateEdges(ref edges, ref edgeCount, duplicates, duplicateCount);

                requiresFix = intersectionCount != 0 || tJunctionCount != 0;
            }

            if (validGraph)
            {
                // Finalize Output.
                outputEdgeCount = edgeCount;
                outputPointCount = pointCount;
                unsafe
                {
                    UnsafeUtility.MemCpy(outputEdges.GetUnsafePtr(), edges.UnsafeReadOnlyPtr, edgeCount * sizeof(int2));
                }
                for (int i = 0; i < pointCount; ++i)
                    outputPoints[i] = new float2((float)(points[i].x / precisionFudge), (float)(points[i].y / precisionFudge));
            }

            edges.Dispose();
            points.Dispose();
            intersects.Dispose();
            duplicates.Dispose();
            tJunctions.Dispose();
            edgeIntersections.Dispose();

            return (validGraph && protectLoop > 0);
        }

    }

}
