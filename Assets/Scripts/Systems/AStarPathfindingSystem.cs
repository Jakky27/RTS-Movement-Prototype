using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Authoring;
using Unity.Physics.Systems;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Experimental.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.VFX;
using Material = UnityEngine.Material;
using RaycastHit = Unity.Physics.RaycastHit;

namespace Pathfinding
{



    #region ECS

    public struct Cell : IComponentData
    {
        public int2 Position;

        //public CellFlags CellFlag;
        //public bool Is(CellFlags flag) => (CellFlag & CellFlag) != 0;
        //public void SetFlags(CellFlags mask) => CellFlag |= mask;
        //public void UnsetFlags(CellFlags mask) => CellFlag &= ~mask;
    }

    [Flags]
    public enum CellFlags : byte
    {
        IsBlocked = 1 << 0
    }

    public struct WalkableTag : IComponentData
    {
    }

    public struct Clearance : IComponentData
    {
        public int Value;
    }

    public struct Cluster : IComponentData
    {
        public int2 Position;
        public Direction NeighborsFlag;
    }

    public struct ClusterEdge : IComponentData
    {
        public Entity ClusterOne;
        public Entity ClusterTwo;
    }

    public struct IntraEdges : IBufferElementData
    {
        public Entity NodeOne;
        public Entity NodeTwo;
        public int2 Position;
        public int Length;
    }

    #endregion

    [Flags]
    public enum Direction : byte
    {
        LeftNeighbor = 1 << 0,
        BottomNeighbor = 1 << 1,
        RightNeighbor = 1 << 2,
        TopNeighbor = 1 << 3
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [DisableAutoCreation]
    public class AStarPathfindingSystem : SystemBase
    {
        private BattleMapSettings _battleMapSettings;
        private int _gridWidth;
        private int _gridHeight;
        private int _numCells;

        private const int CLUSTER_SIZE = 10; // TODO move to BattleMapSettings?
        private const int CLUSTER_CELL_SIZE = CLUSTER_SIZE * CLUSTER_SIZE;

        private int _numLevels;

        private BuildPhysicsWorld _physicsWorldSystem;
        private CollisionWorld _collisionWorld;
        private EndSimulationEntityCommandBufferSystem _endSimulationEntityCommandBufferSystem;

        // Entity Archetypes
        private EntityArchetype _nodeArchetype;
        private EntityArchetype _clusterArcheytype;
        private EntityArchetype _nodeEdgesArchetype;

        // Caching the node positions from an index? 
        // Index: cell index
        // Value: int2 position 
        private NativeArray<int2> _nodePositions; // Length: num cells

        // Caching the cluster position form an index?
        private NativeArray<int2> _clusterPositions;

        private NativeArray<bool> _walkableNodes; // Length: num cells

        // Cluster Edges
        // Index is the cluster index: x + (y * numClustersAcross)
        // Contents is a bit flag for its neighbors 
        private NativeArray<Direction> _clusterNeighborFlags;

        #region Inter-Edges

        // Key: Cluster index: x + (y * numClustersAcross)
        // Value: 
        private NativeMultiHashMap<int, InterEdge> _clustersIntraEdges;

        public struct InterEdge : IEquatable<InterEdge>
        {
            // x: Intra edge node
            // y: Inter edge node
            public int2 NodeIndicies;
            public Direction Direction;

            public bool Equals(InterEdge other)
            {
                return NodeIndicies.x == other.NodeIndicies.x && NodeIndicies.y == other.NodeIndicies.y;
            }
        }

        // Key: (intra node #1, intra node #2). NOTE: the flipped version is also in this data structure
        // Value: list of int2 positions that represents the path from going from node #1 to #2
        private NativeMultiHashMap<int2, int2> _intraEdgesPaths;

        public struct IntraEdge : IEquatable<IntraEdge>
        {
            // x: Intra edge node
            // y: Inter edge node
            public int2 NodeIndicies;
            public int Cost;
            public NativeList<int2> Path;

            public bool Equals(IntraEdge other)
            {
                return NodeIndicies.x == other.NodeIndicies.x && NodeIndicies.y == other.NodeIndicies.y;
            }
        }


        private NativeMultiHashMap<int2, int2> _interEdgesNeighbors;

        #endregion


        // TODO clearance map as well?
        private NativeArray<byte> _nodeClearances; // Aligns with _nodeIndices

        [Flags]
        private enum Offset : byte
        {
            TopLeft = 1 << 0,
            TopMiddle = 1 << 1,
            TopRight = 1 << 2,
            CenterRight = 1 << 3,
            BottomRight = 1 << 4,
            BottomMiddle = 1 << 5,
            BottomLeft = 1 << 6,
            CenterLeft = 1 << 7
        }

        private NativeArray<int> _clusterOffetsArr;
        private NativeArray<int> _nodeOffetsArr;
        private NativeArray<int> _offsetsCost;

        #region DEBUG

        private Transform _plane;

        #endregion

        protected override void OnCreate()
        {
            return;
            _endSimulationEntityCommandBufferSystem = World.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();
            _physicsWorldSystem = World.GetOrCreateSystem<BuildPhysicsWorld>();

            _plane = GameObject.Find("Pathfinding Plane").transform;

            Addressables.LoadAssetAsync<BattleMapSettings>("Battle Map Settings")
                .Completed += handle =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    _battleMapSettings = handle.Result;
                    _numLevels = _battleMapSettings.NumLevels;
                    _gridWidth = _battleMapSettings.MapSize.x;
                    _gridHeight = _battleMapSettings.MapSize.y;
                    _numCells = _gridWidth * _gridHeight;
                    BuildGraph();
                }
                else
                {
                    Debug.LogError("Failed to load battle map settings");
                }
            };

            _nodeArchetype = EntityManager.CreateArchetype(
                typeof(Cell),
                typeof(Clearance)
            );

            _clusterArcheytype = EntityManager.CreateArchetype(
                typeof(Cluster)
            );

        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _nodePositions.Dispose();
            _walkableNodes.Dispose();
            _clusterNeighborFlags.Dispose();
            _clustersIntraEdges.Dispose();
            _intraEdgesPaths.Dispose();
            _nodeOffetsArr.Dispose();
            _offsetsCost.Dispose();
            _clusterOffetsArr.Dispose();
            _clusterPositions.Dispose();
        }

        /// <summary>
        /// Creates the pathfinding graph of all levels (for HPA*)
        /// Creates the nodes, then the inter-edges, and then the intra-edges
        /// </summary>
        private void BuildGraph()
        {
            _collisionWorld = _physicsWorldSystem.PhysicsWorld.CollisionWorld;

            var gridWidth = _gridWidth;
            var gridHeight = _gridHeight;
            int numCells = _gridWidth * _gridHeight;
            float cellSize = _battleMapSettings.CellSize;
            var clusterCellCount = CLUSTER_SIZE * CLUSTER_SIZE;
            int numClusters = _battleMapSettings.MapSize.x * _battleMapSettings.MapSize.y / clusterCellCount;
            var clusterVerticalStep = _gridWidth * CLUSTER_SIZE; // A vertical step for clusters 
            var clustersAcross = _gridWidth / CLUSTER_SIZE;

            _nodeOffetsArr = new NativeArray<int>(8, Allocator.Persistent)
            {
                // Clockwise
                [0] = -1 + gridWidth, // Top left
                [1] = gridWidth,
                [2] = 1 + gridWidth,
                [3] = 1,
                [4] = 1 - gridWidth,
                [5] = -gridWidth,
                [6] = -1 - gridWidth,
                [7] = -1
            };
            _clusterOffetsArr = new NativeArray<int>(8, Allocator.Persistent)
            {
                // Clockwise
                [1] = clustersAcross, // Top
                [3] = 1,
                [5] = -clustersAcross,
                [7] = -1
            };
            _offsetsCost = new NativeArray<int>(8, Allocator.Persistent)
            {
                [0] = 2,
                [1] = 1,
                [2] = 2,
                [3] = 1,
                [4] = 2,
                [5] = 1,
                [6] = 2,
                [7] = 1
            };
            _intraEdgesPaths = new NativeMultiHashMap<int2, int2>(numCells, Allocator.Persistent);
            var walkableNodes = new NativeArray<bool>(numCells, Allocator.Persistent);
            _nodePositions = new NativeArray<int2>(numCells, Allocator.Persistent);
            _clusterNeighborFlags =
                new NativeArray<Direction>(numCells, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _walkableNodes = walkableNodes;

            // Initialize raycast inputs to check which cells are good or bad
            // 1 cell = 4 raycast inputs (for each corner)
            var raycastInputs = new NativeArray<RaycastInput>(numCells * 4, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);


            for (int y = 0; y < gridHeight; y++)
            {
                float yBottom = y * cellSize; // Bottom 
                float yTop = yBottom + cellSize;
                for (int x = 0; x < gridWidth; x++)
                {
                    int bottomLeftIndex = (x + (y * gridWidth)) * 4;
                    float xLeft = x * cellSize; // Left side 
                    float xRight = xLeft + cellSize;

                    raycastInputs[bottomLeftIndex] = new RaycastInput
                    {
                        Start = new float3(xLeft, 100f, yBottom),
                        End = new float3(xLeft, -1f, yBottom),
                        Filter = CollisionFilter.Default
                    };
                    raycastInputs[bottomLeftIndex + 1] = new RaycastInput
                    {
                        Start = new float3(xRight, 100f, yBottom),
                        End = new float3(xRight, -1f, yBottom),
                        Filter = CollisionFilter.Default
                    };
                    raycastInputs[bottomLeftIndex + 2] = new RaycastInput
                    {
                        Start = new float3(xLeft, 100f, yTop),
                        End = new float3(xLeft, -1f, yTop),
                        Filter = CollisionFilter.Default
                    };
                    raycastInputs[bottomLeftIndex + 3] = new RaycastInput
                    {
                        Start = new float3(xRight, 100f, yTop),
                        End = new float3(xRight, -1f, yTop),
                        Filter = CollisionFilter.Default
                    };

                    _nodePositions[x + (y * gridWidth)] = new int2(x, y);
                }
            }

            var raycastHits = new NativeArray<RaycastHit>(numCells * 4, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            var raycastsHandle =
                RaycastUtils.ScheduleBatchRayCast(_collisionWorld, raycastInputs, raycastHits, this.Dependency);

            var bodies = _collisionWorld.Bodies;

            raycastsHandle.Complete();

            // Initialize traversable terrain grid
            for (int i = 0; i < numCells; i++)
            {
                bool isCellPathable = true;
                // Iterating through the cell's 4 corners
                for (int j = 0; j < 4; j++)
                {
                    var raycastHit = raycastHits[(i * 4) + j];
                    var layer = bodies[raycastHit.RigidBodyIndex].Collider.Value.Filter.BelongsTo;
                    isCellPathable = layer == 1u && isCellPathable;
                }

                _walkableNodes[i] = isCellPathable;
            }

            // Create intra-edges and assign them to clusters
            _clustersIntraEdges = new NativeMultiHashMap<int, InterEdge>(numClusters, Allocator.Persistent);

            var clusterNeighborFlags = _clusterNeighborFlags;
            var clustersEdgeWriter = _clustersIntraEdges;
            for (int i = 0; i < numClusters; i++)
            {
                var clusterIndex = i;

                // The start position is always the bottom left corner of the cluster
                var bottomLeftIndex = (clusterIndex * CLUSTER_SIZE % gridWidth) +
                                      (clusterIndex / clustersAcross) * clusterVerticalStep;
                var topLeftIndex = bottomLeftIndex + clusterVerticalStep - gridWidth; // 9 Units up

                var handle = Job
                    .WithName("InitializeClusterEdgesJob")
                    .WithReadOnly(walkableNodes)
                    .WithCode((() =>
                    {
                        var currClusterEdges =
                            new NativeList<int2>(4,
                                Allocator
                                    .Temp); // Note: initial capacity is 4 b/c it's the most likely case (I believe)

                        if (clusterIndex < numClusters - clustersAcross)
                        {
                            // If we're not at the top of grid, we can check for edges at the top of the clusters
                            FindTopDownEdges(in walkableNodes, ref currClusterEdges, topLeftIndex, gridWidth);

                            if (!currClusterEdges.IsEmpty)
                            {
                                var clusterNeighborIndex = clusterIndex + clustersAcross; // Above it

                                // Initialize a cluster edge (between current cluster and the cluster above it)
                                clusterNeighborFlags[clusterIndex] |= Direction.TopNeighbor;
                                clusterNeighborFlags[clusterNeighborIndex] |= Direction.BottomNeighbor;

                                for (int j = 0; j < currClusterEdges.Length; j++)
                                {
                                    var edge = currClusterEdges[j];
                                    clustersEdgeWriter.Add(clusterIndex,
                                        new InterEdge {NodeIndicies = edge, Direction = Direction.TopNeighbor});
                                    clustersEdgeWriter.Add(clusterNeighborIndex,
                                        new InterEdge
                                        {
                                            NodeIndicies = new int2(edge.y, edge.x),
                                            Direction = Direction.BottomNeighbor
                                        });

                                    //var node1 = new Vector3((edge.x % gridWidth) * cellSize, 0.1f, (edge.x / gridWidth) * cellSize);
                                    //var node2 = new Vector3((edge.y % gridWidth) * cellSize, 0.1f, (edge.y / gridWidth) * cellSize);
                                    //Debug.DrawLine(node1, node2, Color.blue, 50f);
                                }
                            }
                        }

                        if ((clusterIndex % clustersAcross) > 0)
                        {
                            // Checking left side (this cluster has an adjacent cluster to its left)
                            currClusterEdges.Clear();

                            FindSideEdges(in walkableNodes, ref currClusterEdges, bottomLeftIndex, gridWidth, -1);

                            if (!currClusterEdges.IsEmpty)
                            {
                                var clusterNeighborIndex = clusterIndex - 1; // To its left

                                // Initialize a cluster edge (between current cluster and the cluster above it)
                                clusterNeighborFlags[clusterIndex] |= Direction.RightNeighbor;
                                clusterNeighborFlags[clusterNeighborIndex] |= Direction.LeftNeighbor;

                                for (int j = 0; j < currClusterEdges.Length; j++)
                                {
                                    var edge = currClusterEdges[j];
                                    clustersEdgeWriter.Add(clusterIndex,
                                        new InterEdge {NodeIndicies = edge, Direction = Direction.RightNeighbor});
                                    clustersEdgeWriter.Add(clusterNeighborIndex,
                                        new InterEdge
                                        {
                                            NodeIndicies = new int2(edge.y, edge.x), Direction = Direction.LeftNeighbor
                                        });

                                    //var node1 = new Vector3((edge.x % gridWidth) * cellSize, 0.1f, (edge.x / gridWidth) * cellSize + (cellSize / 2));
                                    //var node2 = new Vector3((edge.y % gridWidth) * cellSize, 0.1f, (edge.y / gridWidth) * cellSize + (cellSize / 2));
                                    //Debug.DrawLine(node1, node2, Color.blue, 50f);
                                }
                            }
                        }
                    })).Schedule(Dependency);

                handle.Complete();
            }

            // Initialize cluster positions (very trivial)
            _clusterPositions = new NativeArray<int2>(numClusters, Allocator.Persistent);
            for (int y = 0; y < clustersAcross; y++)
            {
                for (int x = 0; x < clustersAcross; x++)
                {
                    _clusterPositions[x + (y * clustersAcross)] = new int2(x, y);
                }
            }

            // Initialize intra-edges
            var initIntraEdgesHandle = new InitializeIntraEdgesJob
            {
                OffsetsArr = _nodeOffetsArr,
                OffsetsCost = _offsetsCost,
                NodePositions = _nodePositions,
                WalkableNodes = walkableNodes,
                ClustersIntraEdges = clustersEdgeWriter,
                ClustersAccross = clustersAcross,
                IntraEdgesPaths = _intraEdgesPaths.AsParallelWriter(),
                NumCells = numCells
            }.Schedule(numClusters, 4, Dependency);

            DebugUpdatePlane();

            raycastInputs.Dispose();
            raycastHits.Dispose();
        }

        /// <summary>
        /// Initializes the given list with edges with the given cluster properties
        /// The edges are only for the bottom horizontal row of the cluster
        /// </summary>
        /// <param name="walkableNodes"></param>
        /// <param name="edgeNodeIndices">Contains (intra edges, inter edges) pairs </param>
        /// <param name="topLeftIndex">The start index of the cluster (either the top left or bottom left)</param>
        /// <param name="offset">The vertical offset of the nodes we're iterating through to find edges</param>
        private static void FindTopDownEdges(in NativeArray<bool> walkableNodes, ref NativeList<int2> edgeNodeIndices,
            int topLeftIndex, int offset)
        {
            bool hasOpening = false; // How big the opening is. Will be reused and set to 0 if a blockage is found.
            int openingStartingIndex = topLeftIndex; // Will update if we find multiple openings
            var lastNodeIndex = topLeftIndex + CLUSTER_SIZE;
            for (int i = topLeftIndex; i < lastNodeIndex; i += 1)
            {
                var otherNodeIndex = i + offset; // x + (y * width), where we're checking a vertical neighbor
                if (walkableNodes[i] && walkableNodes[otherNodeIndex])
                {
                    // An edge is found (both nodes are walkable)

                    // If an opening wasn't found yet, then update the new opening's starting index
                    openingStartingIndex = math.select(i, openingStartingIndex, hasOpening);

                    // The node below the curr node is traversable, so keep iterating until we find a blockage
                    hasOpening = true;

                    // TODO maybe have a limit for opening size? We currently have edges that can span 10 units 
                }
                else
                {
                    // The bottom node is not traversable, so if we currently have an opening, then we create an edge!
                    if (hasOpening)
                    {
                        int intraEdgeNodeIndex =
                            (openingStartingIndex + i) /
                            2; // The halfway point of the opening of the node inside the cluster
                        int outsideEdgeNodeIndex =
                            intraEdgeNodeIndex + offset; // The node below (the 2nd node for the edge)
                        edgeNodeIndices.Add(new int2(intraEdgeNodeIndex, outsideEdgeNodeIndex));

                        // Reset variables
                        hasOpening = false;
                    }
                }
            }

            // Case: if we don't find a blockage at the end of the cluster
            if (hasOpening)
            {
                // The halfway point of the opening of the node inside the cluster
                int intraEdgeNodeIndex = (openingStartingIndex + lastNodeIndex) / 2;
                int outsideEdgeNodeIndex = intraEdgeNodeIndex + offset; // The node below (the 2nd node for the edge)
                edgeNodeIndices.Add(new int2(intraEdgeNodeIndex, outsideEdgeNodeIndex));
            }
        }

        /// <summary>
        /// Initializes the given list with edges with the given cluster properties
        /// The edges are only for the bottom horizontal row of the cluster
        /// </summary>
        /// <param name="walkableNodes"></param>
        /// <param name="edgeNodeIndices">Contains (intra edges, inter edges) pairs </param>
        /// <param name="leftMostIndex">The start index of the cluster (either the top left or bottom left)</param>
        /// <param name="gridWidth"></param>
        /// <param name="offset">The horizontal offset of the nodes we're iterating through to find edges (-1 or 1)</param>
        private static void FindSideEdges(in NativeArray<bool> walkableNodes, ref NativeList<int2> edgeNodeIndices,
            int leftMostIndex, int gridWidth, int offset)
        {
            int openingSize = 0; // How big the opening is. Will be reused and set to 0 if a blockage is found.
            int openingStartingIndex = leftMostIndex; // Will update if we find multiple openings
            var lastNodeIndex = leftMostIndex + (gridWidth * CLUSTER_SIZE);
            for (int i = leftMostIndex; i < lastNodeIndex; i += gridWidth)
            {
                var otherNodeIndex = i + offset; // x + (y * width), where we're checking a vertical neighbor

                if (walkableNodes[i] && walkableNodes[otherNodeIndex])
                {
                    // The node below the curr node is traversable, so keep iterating until we find a blockage
                    openingSize++;

                    // TODO maybe have a limit for opening size? We currently have edges that can span 10 units 
                }
                else
                {
                    // The bottom node is not traversable, so if we currently have an opening, then we create an edge!
                    if (openingSize > 0)
                    {
                        // The halfway point of the opening of the node inside the cluster
                        int intraEdgeNodeIndex = openingStartingIndex + (openingSize / 2) * gridWidth;
                        int outsideEdgeNodeIndex = intraEdgeNodeIndex + offset; // The 2nd node for the edge
                        edgeNodeIndices.Add(new int2(intraEdgeNodeIndex, outsideEdgeNodeIndex));

                        // Reset variables
                        openingSize = 0;
                    }

                    openingStartingIndex = i;
                }
            }

            // Case: if we don't find a blockage at the end of the cluster
            if (openingSize > 0)
            {
                // The halfway point of the opening of the node inside the cluster
                int intraEdgeNodeIndex = openingStartingIndex + (openingSize / 2) * gridWidth;
                int outsideEdgeNodeIndex = intraEdgeNodeIndex + offset; // The 2nd node for the edge
                edgeNodeIndices.Add(new int2(intraEdgeNodeIndex, outsideEdgeNodeIndex));
            }
        }

        [BurstCompile]
        private struct InitializeIntraEdgesJob : IJobParallelFor
        {
            // Array of offsets
            [ReadOnly] public NativeArray<int> OffsetsArr;
            [ReadOnly] public NativeArray<int> OffsetsCost;

            [ReadOnly] public NativeArray<int2> NodePositions;
            [ReadOnly] public NativeArray<bool> WalkableNodes;

            [ReadOnly] public NativeMultiHashMap<int, InterEdge> ClustersIntraEdges;

            [ReadOnly] public int ClustersAccross;
            [ReadOnly] public int NumCells;

            // Output
            [WriteOnly] public NativeMultiHashMap<int2, int2>.ParallelWriter IntraEdgesPaths;

            // Index is the cluster index
            // Iterating through every cluster in the grid
            // For each intra edge pair, initialize their path 
            // This also includes smoothing
            public void Execute(int clusterIndex)
            {
                // TODO this way of getting the node indicies suck
                var intraNodes =
                    new NativeList<int>(ClustersIntraEdges.CountValuesForKey(clusterIndex), Allocator.Temp);
                if (ClustersIntraEdges.TryGetFirstValue(clusterIndex, out var intraEdgeNode, out var iterator))
                {
                    // Initializing intra nodes
                    do
                    {
                        intraNodes.AddNoResize(intraEdgeNode.NodeIndicies[0]);
                    } while (ClustersIntraEdges.TryGetNextValue(out intraEdgeNode, ref iterator));
                }

                var clusterMinBoundX = (clusterIndex % ClustersAccross) * CLUSTER_SIZE;
                var clusterMaxBoundX = clusterMinBoundX + CLUSTER_SIZE;
                var clusterMinBoundY = (clusterIndex / ClustersAccross) * CLUSTER_SIZE;
                var clusterMaxBoundY = clusterMinBoundY + CLUSTER_SIZE;

                #region Reusing data structures

                var outputPath = new NativeList<int2>(CLUSTER_CELL_SIZE, Allocator.Temp);

                var openSet = new NativeHeap<Node, NodeComparer>(CLUSTER_CELL_SIZE, Allocator.Temp);

                // Used to check if a cell is already in the open list
                // Index: x + (y * CLUSTER_SIZE)
                var openList = new NativeHashSet<int>(CLUSTER_CELL_SIZE, Allocator.Temp);
                var closedList = new NativeHashSet<int>(CLUSTER_CELL_SIZE, Allocator.Temp);
                var cameFromMap = new NativeHashMap<int, int>(CLUSTER_SIZE, Allocator.Temp);
                var gCostsArr = new NativeHashMap<int, int>(CLUSTER_CELL_SIZE, Allocator.Temp);

                #endregion

                for (int i = 0; i < intraNodes.Length; i++)
                {
                    // A* Pathfinding for each intra edge

                    var startNodeIndex = intraNodes[i];
                    var startNode = new Node(startNodeIndex, 0);


                    for (int j = i + 1; j < intraNodes.Length; j++)
                    {
                        closedList.Clear();
                        openList.Clear();
                        cameFromMap.Clear();
                        outputPath.Clear();
                        openSet.Clear();
                        gCostsArr.Clear();

                        // A* starts here

                        cameFromMap.Add(startNodeIndex, -1);
                        openSet.Insert(startNode);
                        gCostsArr[startNodeIndex] = 0;
                        var goalNodeIndex = intraNodes[j];
                        var goalNodePosition = NodePositions[goalNodeIndex];

                        while (!openSet.IsEmpty)
                        {
                            var currNode = openSet.Pop();
                            //Debug.Log($"Cluster:{clusterIndex} Cost:{currNode.Cost}, {NodePositions[currNode.Index]}");
                            var currNodeIndex = currNode.Index;
                            openList.Remove(currNodeIndex);

                            if (currNodeIndex == goalNodeIndex)
                            {
                                // Reached goal node. Reconstruct path
                                //Debug.Log($"({i}, {j}) Reached goal for cluster {clusterIndex}");

                                var tempNodeIndex = currNodeIndex;
                                while (tempNodeIndex != -1)
                                {
                                    outputPath.AddNoResize(NodePositions[tempNodeIndex]);

                                    #region DEBUG

                                    var temp1 = NodePositions[tempNodeIndex];
                                    int2 temp;
                                    if (cameFromMap[tempNodeIndex] != -1)
                                    {
                                        temp = NodePositions[cameFromMap[tempNodeIndex]];
                                    }
                                    else
                                    {
                                        temp = NodePositions[startNodeIndex];
                                    }
                                    // Draw path 
                                    //Debug.DrawLine(new Vector3(temp1.x * 2f + 1f, 0.1f, temp1.y * 2f + 1f), new Vector3(temp.x * 2f + 1f,0.1f, temp.y * 2f + 1f), Color.cyan, 50f);

                                    if (tempNodeIndex == cameFromMap[tempNodeIndex])
                                    {
                                        Debug.LogError("ERROR IN GOAL");
                                    }

                                    #endregion

                                    tempNodeIndex = cameFromMap[tempNodeIndex];
                                }

                                // TODO Apply Path Smoothing
                                /*
                                if (path.Length > 2)
                                {
                                    // Apply smoothing
                                    // NOTE: The smoothing algorithm will also make sure that some invalids paths are culled (refer to figure 4 at the link below)
                                    // https://www.gamasutra.com/view/feature/131505/toward_more_realistic_pathfinding.php?print=1 
                                    
                                    var newPath = new NativeList<int2>(Allocator.Temp);
                                    var startPos = path[0];
                                    var currPos = path[2];
    
                                    for (int k = 3; k < path.Length; k++)
                                    {
                                        var temp = currPos;
                                        if (!LineOfSight(temp, path[k]))
                                        {
                                            newPath.Add(temp);
                                        }
                                    }
                                }
                                */

                                var key1 = new int2(currNodeIndex, goalNodeIndex);
                                var key2 = new int2(currNodeIndex, goalNodeIndex);
                                for (int k = 0; k < outputPath.Length; k++)
                                {
                                    IntraEdgesPaths.Add(key1, outputPath[k]);
                                }

                                for (int k = outputPath.Length - 1; k >= 0; k--)
                                {
                                    IntraEdgesPaths.Add(key2, outputPath[k]);
                                }

                                break;
                            }

                            // Expand to its 8 possible neighbor nodes
                            for (int k = 0; k < OffsetsArr.Length; k++)
                            {
                                var successorNodeIndex = currNodeIndex + OffsetsArr[k];

                                if (successorNodeIndex < 0 || successorNodeIndex >= NumCells)
                                {
                                    continue;
                                }

                                var successorNodePos = NodePositions[successorNodeIndex];

                                if (WalkableNodes[successorNodeIndex]
                                    && !closedList.Contains(successorNodeIndex)
                                    && successorNodePos.x >= clusterMinBoundX && successorNodePos.x < clusterMaxBoundX
                                    && successorNodePos.y >= clusterMinBoundY && successorNodePos.y < clusterMaxBoundY)
                                {
                                    // This is a valid neighbor node (not in the closed list, outside of the cluster boundaries, and is walkable)
                                    // Now to calculate its total cost and add it to the open list

                                    var tentativeScore = gCostsArr[currNodeIndex] + OffsetsCost[k];
                                    if (!gCostsArr.ContainsKey(successorNodeIndex) ||
                                        tentativeScore < gCostsArr[successorNodeIndex])
                                    {
                                        cameFromMap[successorNodeIndex] = currNodeIndex;
                                        gCostsArr[successorNodeIndex] = tentativeScore;
                                        var h = ManhattanDistance(successorNodePos, goalNodePosition);
                                        var f = tentativeScore + h;
                                        var successorNode = new Node(successorNodeIndex, f);
                                        if (!openList.Contains(successorNodeIndex))
                                        {
                                            openList.Add(successorNodeIndex);
                                            openSet.Insert(successorNode);
                                        }

                                        // TODO openSet does not update the sucessorNode's lower cost if a better path was found
                                    }

                                    //Debug.DrawLine(new Vector3(successorNodePos.x * 2f + 1f, 0f, successorNodePos.y * 2f + 1f), new Vector3(successorNodePos.x * 2f + 1f,3f, successorNodePos.y * 2f + 1f), Color.magenta, 50f);
                                }
                            }

                            closedList.Add(currNodeIndex);
                        }
                    }
                }

                outputPath.Dispose();
                openSet.Dispose();
            }

            private bool LineOfSight(int2 start, int2 end)
            {
                return false;
            }
        }

        private struct Node
        {
            public int Index;
            public int Cost;

            public Node(int index, int cost)
            {
                Index = index;
                Cost = cost;
            }
        }

        private struct NodeComparer : IComparer<Node>
        {
            public int Compare(Node x, Node y)
            {
                return x.Cost.CompareTo(y.Cost);
            }
        }

        private static int ManhattanDistance(int2 startPos, int2 endPos)
        {
            return math.abs(startPos.x - endPos.x) + math.abs(startPos.y - endPos.y);
        }

        private void DebugUpdatePlane()
        {
            _plane.position = new Vector3(_gridWidth, 0.1f, _gridHeight);
            _plane.localScale = new Vector3(_gridWidth / 5, 1f, _gridHeight / 5);
            _plane.rotation = Quaternion.Euler(0f, 180f, 0f);

            Texture2D tex = new Texture2D(_gridWidth, _gridHeight);
            tex.filterMode = FilterMode.Point;

            for (int y = 0; y < _gridHeight; y++)
            {
                for (int x = 0; x < _gridWidth; x++)
                {
                    Color color;
                    if (_walkableNodes[x + (y * _gridWidth)])
                    {
                        color = Color.green;
                    }
                    else
                    {
                        color = Color.red;
                    }

                    tex.SetPixel(x, y, color);
                }
            }

            tex.Apply();
            _plane.GetComponent<MeshRenderer>().material.mainTexture = tex;
        }

        protected override void OnUpdate()
        {
            var ecb = _endSimulationEntityCommandBufferSystem.CreateCommandBuffer().AsParallelWriter();

            // If a unit has reached their target position, then check if they should start moving to their next one
            Entities
                .WithName("UpdateTargetPositionJob")
                .WithAll<UnitTag, TargetPosition>()
                .ForEach((Entity unitEntity, ref TargetPosition targetPos,
                    ref DynamicBuffer<PathfindingPath> pathsBuffer, in LocalToWorld ltw) =>
                {
                    if (math.distancesq(ltw.Position, targetPos.Value) < 0.25f) // TODO arbitrary
                    {
                        targetPos.Value = pathsBuffer[pathsBuffer.Length - 1].Position;
                        pathsBuffer.RemoveAt(pathsBuffer.Length - 1);
                    }

                    pathsBuffer.RemoveAt(pathsBuffer.Length - 1);
                }).ScheduleParallel();

            // Check if unit has reached their LAST target position
            /*
            Entities
                .WithName("RemoveTargetPositionJob")
                .WithNone<PathfindingPath>()
                .WithAll<UnitTag>()
                .ForEach((Entity unitEntity, int entityInQueryIndex, ref TargetPosition targetPos, in LocalToWorld ltw) =>
                {
                    if (math.distancesq(ltw.Position, targetPos.Value) < 0.25f) // TODO arbitrary
                    {
                        ecb.RemoveComponent<TargetPosition>(entityInQueryIndex, unitEntity);
                    }
                }).ScheduleParallel();
                */


            // If unit does not have TargetPosition component, then check if it has any paths to go to
            Entities
                .WithName("AddTargetPositionJob")
                .WithNone<TargetPosition>()
                .WithAll<UnitTag>()
                .ForEach((Entity unitEntity, int entityInQueryIndex, ref DynamicBuffer<PathfindingPath> pathsBuffer) =>
                {
                    var targetPos = new TargetPosition {Value = pathsBuffer[pathsBuffer.Length - 1].Position};
                    pathsBuffer.RemoveAt(pathsBuffer.Length - 1);
                    ecb.AddComponent<TargetPosition>(entityInQueryIndex, unitEntity, targetPos);
                }).ScheduleParallel();

            _endSimulationEntityCommandBufferSystem.AddJobHandleForProducer(this.Dependency);
        }

        public JobHandle HierarchicalPathfind(ref NativeList<int2> path, float3 start, float3 end, JobHandle inputDeps)
        {
            return new PathfindJob
            {
                StartPosition = start,
                GoalPosition = end,
                OffsetsArr = _nodeOffetsArr,
                OffsetsCost = _offsetsCost,
                NodePositions = _nodePositions,
                ClusterOffetsArr = _clusterOffetsArr,
                ClusterPositions = _clusterPositions,
                WalkableNodes = _walkableNodes,
                ClusterNeighborFlags = _clusterNeighborFlags,
                InterEdges = _clustersIntraEdges,
                GridWidth = _gridWidth,
                GridHeight = _gridHeight,
                ClustersAcrossY = 5,
                NumCells = _numCells,
                IntraEdgesPaths = _intraEdgesPaths,
                Path = path
            }.Schedule(inputDeps);
        }

        [BurstCompile]
        private struct PathfindJob : IJob
        {
            public float3 StartPosition;
            public float3 GoalPosition;

            // Array of offsets
            [ReadOnly] public NativeArray<int> OffsetsArr;
            [ReadOnly] public NativeArray<int> ClusterOffetsArr;
            [ReadOnly] public NativeArray<int> OffsetsCost;

            [ReadOnly] public NativeArray<int2> NodePositions;
            [ReadOnly] public NativeArray<int2> ClusterPositions;
            [ReadOnly] public NativeArray<bool> WalkableNodes;
            [ReadOnly] public NativeArray<Direction> ClusterNeighborFlags;

            [ReadOnly] public NativeMultiHashMap<int, InterEdge> InterEdges;

            [ReadOnly] public int GridWidth;
            [ReadOnly] public int GridHeight;
            [ReadOnly] public int ClustersAcrossY;
            [ReadOnly] public int NumCells;

            [ReadOnly] public NativeMultiHashMap<int2, int2> IntraEdgesPaths;

            // Output
            [WriteOnly] public NativeList<int2> Path;

            private static readonly int2 s_noParent = new int2(-1, -1);

            public void Execute()
            {
                var clustersPath =
                    new NativeList<int2>(CLUSTER_CELL_SIZE,
                        Allocator
                            .Temp); // Values: list of int2 (start node, end node) that represents the paths between clusters, up to the last one. Use this int2 to hash into IntraEdgesPaths get the actual path
                var lowLevelPath =
                    new NativeList<int2>(CLUSTER_CELL_SIZE, Allocator.Temp); // Values: list of int2 (grid position)  

                // Convert positions to cell indices
                var startNodeIndex = ConvertPositionToCellIndex(StartPosition.xz, GridWidth, GridHeight);
                var goalNodeIndex = ConvertPositionToCellIndex(GoalPosition.xz, GridWidth, GridHeight);

                // Find current cluster index
                int startClusterIndex = ConvertCellToClusterIndex(startNodeIndex, GridWidth, ClustersAcrossY);
                int goalClusterIndex = ConvertCellToClusterIndex(goalNodeIndex, GridWidth, ClustersAcrossY);

                Debug.Log($"{startNodeIndex}");
                Debug.Log($"{goalNodeIndex}");
                Debug.Log($"{startClusterIndex}");
                Debug.Log($"{goalClusterIndex}");

                /*
                // Do A* on the clusters
                if (startClusterIndex != goalClusterIndex)
                {
                    var openSet = new NativeHeap<ClusterNode, ClusterNodeComparer>(CLUSTER_CELL_SIZE, Allocator.Temp);
                    var openList = new NativeHashSet<int2>(CLUSTER_CELL_SIZE, Allocator.Temp); // Essentially a 2nd data structure that is for the open list
                    var closedList = new NativeHashSet<int2>(CLUSTER_CELL_SIZE, Allocator.Temp);
                    var cameFromMap = new NativeHashMap<int2, int2>(CLUSTER_SIZE, Allocator.Temp);
                    var gCostsArr = new NativeHashMap<int2, int>(CLUSTER_CELL_SIZE, Allocator.Temp);
                    
                    // Add all of the start cluster's inter-edges to the open list, if there is a path from the current position
                    var interEdgers = new NativeList<InterEdge>(Allocator.Temp);
                    if (InterEdges.TryGetFirstValue(startClusterIndex, out var interEdge, out var iterator))
                    {
                        do
                        {
                            if (HasPath(startNodeIndex, goalNodeIndex, NumCells, OffsetsArr, OffsetsCost, NodePositions, WalkableNodes))
                            {
                                interEdgers.AddNoResize(interEdge);
                                openList.Add(interEdge.NodeIndicies);
                                cameFromMap.Add(interEdge.NodeIndicies, -1);
                                var firstClusterNode = new ClusterNode(interEdge.NodeIndicies, 0);
                                openSet.Insert(firstClusterNode);
                                gCostsArr[interEdge.NodeIndicies] = 0;
                            }
                        } while (InterEdges.TryGetNextValue(out interEdge, ref iterator));
                    }
                    
                    var goalEdgePosition = ClusterPositions[goalClusterIndex]; // Represents the inter-edge's we're A* pathfinding through
                    
                    // Pathfinding between cluster inter-edges                
                    while (!openSet.IsEmpty)
                    {
                        var currNode = openSet.Pop();
                        var currNodePos = currNode.Position; 
                        openList.Remove(currNodePos);
    
                        if (currNodePos.Equals(goalEdgePosition))
                        {
                            // Reached goal node. Reconstruct path
                            Debug.Log("Found goal");
                            
                            var tempClusterPos = currNodePos;
                            while (!tempClusterPos.Equals(s_noParent))
                            {
                                clustersPath.AddNoResize(tempClusterPos);
                                
                                #region DEBUG
                                var temp1 = tempClusterPos;
                                int2 temp;
                                if (!cameFromMap[tempClusterPos].Equals(s_noParent))
                                {
                                    temp = cameFromMap[tempClusterPos];
                                }
                                else
                                {
                                    temp = ClusterPositions[startNodeIndex];
                                }
                                Debug.DrawLine(new Vector3(temp1.x * 2f + 1f, 0.1f, temp1.y * 2f + 1f), new Vector3(temp.x * 2f + 1f,0.1f, temp.y * 2f + 1f), Color.magenta, 50f);
                                
                                if (tempClusterPos.Equals(cameFromMap[tempClusterPos]))
                                {
                                    Debug.LogError("ERROR IN GOAL");
                                }
                                #endregion
                                
                                tempClusterPos = cameFromMap[tempClusterPos];
                            }
                            break;
                        }
                        
                        // Expand each inter-edge (could be any number of outgoing nodes)
                        
                        for (int k = 0; k < ClusterOffetsArr.Length; k++)
                        {
    
                            var intraEdges = new NativeList<int2>(Allocator.Temp);
                            if (IntraEdgesPaths.TryGetFirstValue(startClusterIndex, out var intraEdge, out var intraEdgeIterator))
                            {
                                // Initializing intra nodes
                                do
                                {
                                    interEdgers.AddNoResize(intraEdge);
                                } while (InterEdges.TryGetNextValue(out intraEdge, ref intraEdgeIterator));
                            }
    
                            // Iterate through each valid intra edge
                            
                            var successorNodeIndex = currNodePos + ClusterOffetsArr[k];
    
                            if (successorNodeIndex < 0 || successorNodeIndex >= NumCells)
                            {
                                continue;
                            }
                            
                            var successorNodePos = ClusterPositions[successorNodeIndex];
                            
                            if (!closedList.Contains(successorNodeIndex))
                            {
                                // This is a valid neighbor node (not in the closed list, outside of the cluster boundaries, and is walkable)
                                // Now to calculate its total cost and add it to the open list
    
                                var tentativeScore = gCostsArr[currNodePos] + 1;
                                if (!gCostsArr.ContainsKey(successorNodeIndex) || tentativeScore < gCostsArr[successorNodeIndex])
                                {
                                    cameFromMap[successorNodeIndex] = currNodePos;
                                    gCostsArr[successorNodeIndex] = tentativeScore;
                                    var h = ManhattanDistance(successorNodePos, goalNodeIndex);
                                    var f = tentativeScore + h;
                                    var successorNode = new Node(successorNodeIndex, f);
                                    if (!openList.Contains(successorNodeIndex))
                                    {
                                        openList.Add(successorNodeIndex);
                                        openSet.Insert(successorNode);
                                    }
                                    // TODO openSet does not update the sucessorNode's lower cost if a better path was found
                                }
                                
                                //Debug.DrawLine(new Vector3(successorNodePos.x * 2f + 1f, 0f, successorNodePos.y * 2f + 1f), new Vector3(successorNodePos.x * 2f + 1f,3f, successorNodePos.y * 2f + 1f), Color.magenta, 50f);
                            }
                        }
                        closedList.Add(currNodePos);
                    }
                }*/



                // Do the final A* on just inside the cluster where the goal node is

            }
        }

        private struct ClusterNode
        {
            // (Intra node, other intra node + offset (so essentially the internode)
            public int2 Position;
            public int Cost;

            public ClusterNode(int2 position, int cost)
            {
                Position = position;
                Cost = cost;
            }
        }

        private struct ClusterNodeComparer : IComparer<ClusterNode>
        {
            public int Compare(ClusterNode x, ClusterNode y)
            {
                return x.Cost.CompareTo(y.Cost);
            }
        }


        private static bool HasPath(int startNodeIndex, int goalNodeIndex, int numCells,
            in NativeArray<int> offsetsArr, in NativeArray<int> offsetsCost, in NativeArray<int2> nodePositions,
            in NativeArray<bool> walkableNodes)
        {
            var openSet = new NativeHeap<Node, NodeComparer>(CLUSTER_CELL_SIZE, Allocator.Temp);
            var openList = new NativeHashSet<int>(CLUSTER_CELL_SIZE, Allocator.Temp);
            var closedList = new NativeHashSet<int>(CLUSTER_CELL_SIZE, Allocator.Temp);
            var cameFromMap = new NativeHashMap<int, int>(CLUSTER_SIZE, Allocator.Temp);
            var gCostsArr = new NativeHashMap<int, int>(CLUSTER_CELL_SIZE, Allocator.Temp);

            var startNode = new Node(startNodeIndex, 0);
            cameFromMap.Add(startNodeIndex, -1);
            openSet.Insert(startNode);
            gCostsArr[startNodeIndex] = 0;
            var goalNodePosition = nodePositions[goalNodeIndex];

            while (!openSet.IsEmpty)
            {
                var currNode = openSet.Pop();
                var currNodeIndex = currNode.Index;
                openList.Remove(currNodeIndex);

                if (currNodeIndex == goalNodeIndex)
                {
                    return true;
                }

                // Expand to its 8 possible neighbor nodes
                for (int k = 0; k < offsetsArr.Length; k++)
                {
                    var successorNodeIndex = currNodeIndex + offsetsArr[k];

                    if (successorNodeIndex < 0 || successorNodeIndex >= numCells)
                    {
                        continue;
                    }

                    var successorNodePos = nodePositions[successorNodeIndex];

                    if (walkableNodes[successorNodeIndex]
                        && !closedList.Contains(successorNodeIndex))
                    {
                        // This is a valid neighbor node (not in the closed list, outside of the cluster boundaries, and is walkable)
                        // Now to calculate its total cost and add it to the open list

                        var tentativeScore = gCostsArr[currNodeIndex] + offsetsCost[k];
                        if (!gCostsArr.ContainsKey(successorNodeIndex) ||
                            tentativeScore < gCostsArr[successorNodeIndex])
                        {
                            cameFromMap[successorNodeIndex] = currNodeIndex;
                            gCostsArr[successorNodeIndex] = tentativeScore;
                            var h = ManhattanDistance(successorNodePos, goalNodePosition);
                            var f = tentativeScore + h;
                            var successorNode = new Node(successorNodeIndex, f);
                            if (!openList.Contains(successorNodeIndex))
                            {
                                openList.Add(successorNodeIndex);
                                openSet.Insert(successorNode);
                            }

                            // TODO openSet does not update the sucessorNode's lower cost if a better path was found
                        }
                    }
                }

                closedList.Add(currNodeIndex);
            }

            return false;
        }

        /// <summary>
        /// A* Pathfinding on the lowest node-level
        /// </summary>
        private static void LowLevelPathfind(ref NativeList<int2> lowLevelPath, int startNodeIndex, int goalNodeIndex,
            int numCells, in NativeArray<int> offsetsArr, in NativeArray<int> offsetsCost,
            in NativeArray<int2> nodePositions, in NativeArray<bool> walkableNodes)
        {
            var openSet = new NativeHeap<Node, NodeComparer>(CLUSTER_CELL_SIZE, Allocator.Temp);
            var openList = new NativeHashSet<int>(CLUSTER_CELL_SIZE, Allocator.Temp);
            var closedList = new NativeHashSet<int>(CLUSTER_CELL_SIZE, Allocator.Temp);
            var cameFromMap = new NativeHashMap<int, int>(CLUSTER_SIZE, Allocator.Temp);
            var gCostsArr = new NativeHashMap<int, int>(CLUSTER_CELL_SIZE, Allocator.Temp);

            var startNode = new Node(startNodeIndex, 0);
            cameFromMap.Add(startNodeIndex, -1);
            openSet.Insert(startNode);
            gCostsArr[startNodeIndex] = 0;
            var goalNodePosition = nodePositions[goalNodeIndex];

            while (!openSet.IsEmpty)
            {
                var currNode = openSet.Pop();
                var currNodeIndex = currNode.Index;
                openList.Remove(currNodeIndex);

                if (currNodeIndex == goalNodeIndex)
                {
                    // Reached goal node. Reconstruct path
                    var tempNodeIndex = currNodeIndex;
                    while (tempNodeIndex != -1)
                    {
                        lowLevelPath.AddNoResize(nodePositions[tempNodeIndex]);

                        #region DEBUG

                        var temp1 = nodePositions[tempNodeIndex];
                        int2 temp;
                        if (cameFromMap[tempNodeIndex] != -1)
                        {
                            temp = nodePositions[cameFromMap[tempNodeIndex]];
                        }
                        else
                        {
                            temp = nodePositions[startNodeIndex];
                        }
                        //Debug.DrawLine(new Vector3(temp1.x * 2f + 1f, 0.1f, temp1.y * 2f + 1f), new Vector3(temp.x * 2f + 1f,0.1f, temp.y * 2f + 1f), Color.cyan, 50f);

                        if (tempNodeIndex == cameFromMap[tempNodeIndex])
                        {
                            Debug.LogError("ERROR IN GOAL");
                        }

                        #endregion

                        tempNodeIndex = cameFromMap[tempNodeIndex];

                    }

                    break;
                }

                // Expand to its 8 possible neighbor nodes
                for (int k = 0; k < offsetsArr.Length; k++)
                {
                    var successorNodeIndex = currNodeIndex + offsetsArr[k];

                    if (successorNodeIndex < 0 || successorNodeIndex >= numCells)
                    {
                        continue;
                    }

                    var successorNodePos = nodePositions[successorNodeIndex];

                    if (walkableNodes[successorNodeIndex]
                        && !closedList.Contains(successorNodeIndex))
                    {
                        // This is a valid neighbor node (not in the closed list, outside of the cluster boundaries, and is walkable)
                        // Now to calculate its total cost and add it to the open list

                        var tentativeScore = gCostsArr[currNodeIndex] + offsetsCost[k];
                        if (!gCostsArr.ContainsKey(successorNodeIndex) ||
                            tentativeScore < gCostsArr[successorNodeIndex])
                        {
                            cameFromMap[successorNodeIndex] = currNodeIndex;
                            gCostsArr[successorNodeIndex] = tentativeScore;
                            var h = ManhattanDistance(successorNodePos, goalNodePosition);
                            var f = tentativeScore + h;
                            var successorNode = new Node(successorNodeIndex, f);
                            if (!openList.Contains(successorNodeIndex))
                            {
                                openList.Add(successorNodeIndex);
                                openSet.Insert(successorNode);
                            }

                            // TODO openSet does not update the sucessorNode's lower cost if a better path was found
                        }

                        //Debug.DrawLine(new Vector3(successorNodePos.x * 2f + 1f, 0f, successorNodePos.y * 2f + 1f), new Vector3(successorNodePos.x * 2f + 1f,3f, successorNodePos.y * 2f + 1f), Color.magenta, 50f);
                    }
                }

                closedList.Add(currNodeIndex);
            }
        }

        public static int ConvertPositionToCellIndex(in float2 position, int gridWidth, int gridHeight)
        {
            return (int) (position.x / 2f) % gridWidth + (int) (position.y / 2f) * gridHeight;
        }

        public static int ConvertCellToClusterIndex(int cellIndex, int gridWidth, int clustersAcrossY)
        {
            return (cellIndex % gridWidth) / CLUSTER_SIZE + (cellIndex / gridWidth / CLUSTER_SIZE) * clustersAcrossY;
        }

        public static int2 ConvertClusterIndexToClusterPosition(int clusterIndex, int clustersAcross)
        {
            return new int2(clusterIndex % clustersAcross, clusterIndex / clustersAcross);
        }
    }

}