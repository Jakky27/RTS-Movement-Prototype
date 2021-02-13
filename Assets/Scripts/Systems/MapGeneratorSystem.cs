using System;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;


[DisableAutoCreation]
public class MapGeneratorSystem : SystemBase {
    private static float _cellSize = 1f;

    private Mesh _mapMesh;
    //private RenderMesh _renderMesh;

    // Fields passed in at startup
    public MapGeneratorSettings mapGenSettings;
    public Entity mapEntity;
    public GameObject mapPrefab;
    public Material debugMaterial;


    private EndSimulationEntityCommandBufferSystem m_EndSimulationEcbSystem;

    protected override void OnCreate() {
        base.OnCreate();
        m_EndSimulationEcbSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        _mapMesh = new Mesh();
        //_renderMesh = new RenderMesh();
        //_renderMesh.castShadows = UnityEngine.Rendering.ShadowCastingMode.On;
        //_renderMesh.receiveShadows = true;
    }

    protected override void OnDestroy() {
        base.OnDestroy();
        if (_mapMesh != null) {
            Resources.UnloadAsset(_mapMesh);
        }
    }

    protected override void OnUpdate() {
    }

    /// <summary>
    /// Calculates the appropriate chunk size (height & width) for the given map
    /// Such that it does not exceed 2^16 vertices
    /// </summary>
    private void CalculateChunkSize() {

    }

    public void GenerateMap() {
        Debug.Log("System generate map");

        var ecb = m_EndSimulationEcbSystem.CreateCommandBuffer().AsParallelWriter();

        int width = 241;
        int height = 241;

        width = mapGenSettings.heightMap.width;
        height = mapGenSettings.heightMap.height;
        
        int LOD = 2;
        int increment = LOD == 0 ? 1 : LOD * 2;

        var colors = new NativeArray<Color>(mapGenSettings.heightMap.GetPixels(0, 0, width, height, 0), Allocator.TempJob);
        var triangles = new NativeArray<int>((width - 1) * (height - 1) * 6, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var vertices = new NativeArray<float3>(width * height, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var uvs = new NativeArray<float2>(width * height, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        #region single threaded

/*        Profiler.BeginSample("Generate map verts and tris single-threaded");
        int vertexIndex = 0;
        int triangleIndex = 0;
        
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                
                vertices[vertexIndex] = new float3(x, colors[vertexIndex].grayscale * 5f, y);
                
                // UVs
                uvs[vertexIndex] = new float2(x / (float)width, y / (float)height);
        
                if (x < width - 1 && y < height - 1) {
                    //triangles[triangleIndex] = vertexIndex;
                    //triangles[triangleIndex + 1] = vertexIndex + width + 1;
                    //triangles[triangleIndex + 2] = vertexIndex + width;
                    //
                    //triangles[triangleIndex + 3] = vertexIndex + width + 1;
                    //triangles[triangleIndex + 4] = vertexIndex;
                    //triangles[triangleIndex + 5] = vertexIndex + 1;
        
                    triangles[triangleIndex] = vertexIndex + width;
                    triangles[triangleIndex + 1] = vertexIndex + width + 1;
                    triangles[triangleIndex + 2] = vertexIndex;
        
                    triangles[triangleIndex + 3] = vertexIndex + 1;
                    triangles[triangleIndex + 4] = vertexIndex;
                    triangles[triangleIndex + 5] = vertexIndex + width + 1;
        
                    triangleIndex += 6;
                }
        
                vertexIndex++;
            }
        }
        Profiler.EndSample();
*/
        #endregion

        Profiler.BeginSample("Generate map verts and tris multi-threaded");
        var handle = new GeneratorMapJob {
            Colors = colors,
            Triangles = triangles,
            Vertices = vertices,
            Uvs = uvs,
            Width = width,
            Height = height,
            Increment =  increment,
            VerticesPerLine = (width - 1) / increment
        }.Schedule(width * height, 128, this.Dependency); // TODO adjust innerLoppBatchCount for something optimal
        handle.Complete();
        Profiler.EndSample();

        _mapMesh.Clear();
        _mapMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Allows your mesh to have more than 2^16 (65536) vertices (mesh will render weird if you don't set this)

        //mapMesh.vertices = vertices.Reinterpret<Vector3>().ToArray(); // Slow (ToArray() is a copy AND setting mapMesh.vertices makes another copy I think)
        _mapMesh.SetVertices(vertices);

        _mapMesh.triangles = triangles.ToArray(); // TODO switch to SetTriangles() if the API exists for NativeArray
        _mapMesh.SetUVs(0, uvs);
        _mapMesh.RecalculateNormals();

        //_renderMesh.mesh = _mapMesh;
        //_renderMesh.material = debugMaterial;

        //EntityManager.SetSharedComponentData(mapEntity, renderMesh);
        //EntityManager.Instantiate(mapEntity);
        //EntityManager.SetName(mapEntity, "Height Map");

        #region debug 

        // Initializing the game object to have this generated mesh
        mapPrefab.transform.GetComponent<MeshFilter>().sharedMesh = _mapMesh;
        mapPrefab.transform.GetComponent<MeshRenderer>().material = debugMaterial;
        mapPrefab.transform.localScale = Vector3.one;
        
        #endregion

        triangles.Dispose();
        vertices.Dispose();
        uvs.Dispose();
    }
    
    [BurstCompile]
    private struct GeneratorMapJob : IJobParallelFor {

        [ReadOnly]
        [DeallocateOnJobCompletion]
        public NativeArray<Color> Colors;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> Triangles;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> Vertices;

        [NativeDisableParallelForRestriction]
        public NativeArray<float2> Uvs;

        public int Width;
        public int Height;
        public int Increment; // For LODs. It skips vertices
        public int VerticesPerLine;

        public void Execute(int index) {

            int x = index % Width;
            int y = index / Width;

            float vertexHeight = Colors[index].grayscale * 115f;
            Vertices[index] = new float3(x, vertexHeight, y);

            // UVs
            Uvs[index] = new float2(x / (float)Width, y / (float)Height);

            // Setting Triangles
            if (x < Width - 1 && y < Height - 1) {

                // Need to calculate the proper triangle index to start at
                // which is complex b/c some iterations are skipped for being at the sides
                int offset = y;

                //Debug.Log("Vertex Index: " + index + " X:" + x + " Y: " + y + " Offset: " + offset);

                int triangleIndex = (index - offset) * 6;
                Triangles[triangleIndex] = index + Width;
                Triangles[triangleIndex + 1] = index + Width + 1;
                Triangles[triangleIndex + 2] = index;

                Triangles[triangleIndex + 3] = index + 1;
                Triangles[triangleIndex + 4] = index;
                Triangles[triangleIndex + 5] = index + Width +  1;
            }
        }
    }


    public void GenerateMapMatchingSquares() {

        // Calculate how many chunks we need?
        
        // Defines the width and the height of our chunk
        // Good number to not have a mesh over 2^16 vertices large
        // Max vertices for a 32-bit mesh is 255^2, so you COULD use 254, but 241 is convenient b/c it's a good divisible number for LODs
        int chunkSize = 241;

        int LOD = 4; 
        
        int width = chunkSize;
        int height = chunkSize;
        
        int numNodes = width * height;
        
        var colors = new NativeArray<Color>(mapGenSettings.heightMap.GetPixels(0, 0, width, height, 0), Allocator.TempJob);
        var controlNodes = new NativeArray<ControlNode>(numNodes, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var grid = new NativeArray<Cell>(numNodes, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);


        GenerateGrid(width, height, LOD, colors, controlNodes, grid);
    }

    /// <summary>
    /// Uses Marching Squares to generate a grid for mesh generation
    /// </summary>
    public void GenerateGrid(int width, int height, int LOD, NativeArray<Color> colors, NativeArray<ControlNode> controlNodes, NativeArray<Cell> grid) {

        var ecb = m_EndSimulationEcbSystem.CreateCommandBuffer().AsParallelWriter();
        
        float mapWidth = width * _cellSize;
        float mapHeight = height * _cellSize;

        int numNodes = width * height;

        new InitializeControlNodesJob {
            GridWidth = width,
            GridHeight = height,
            MapWidth = mapWidth,
            MapHeight = mapHeight,
            CellSize = _cellSize,
            ControlNodes = controlNodes,
            Colors = colors
        }.Schedule(numNodes, 128, this.Dependency).Complete();

        new InitializeCellsJob {
            GridWidth = width,
            GridHeight = height,
            Grid = grid,
            ControlNodes = controlNodes
        }.Schedule(numNodes, 32, this.Dependency).Complete();

        #region debug (show grid)

        
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++)
            {

                var pos = grid[x + y * width].topLeft.position;
                if (grid[x + y * width].topLeft.isActive)
                {
                    Debug.DrawLine(pos, pos + new float3(0, 1, 0), Color.white, 3f);
                }
                else
                {
                    Debug.DrawLine(pos, pos + new float3(0, 1, 0), Color.red, 3f);
                }
                
                pos = grid[x + y * width].topRight.position;
                if (grid[x + y * width].topLeft.isActive)
                {
                    Debug.DrawLine(pos, pos + new float3(0, 1, 0), Color.blue, 3f);
                }
                else
                {
                    Debug.DrawLine(pos, pos + new float3(0, 1, 0), Color.cyan, 3f);
                }
                
                
            }
        }

        #endregion

        // Generate mesh
        
    }

    [BurstCompile]
    private struct InitializeControlNodesJob : IJobParallelFor {
        public int GridWidth;
        public int GridHeight;
        public float MapWidth;
        public float MapHeight;

        public float CellSize;

        [NativeDisableParallelForRestriction]
        public NativeArray<ControlNode> ControlNodes;

        [ReadOnly]
        public NativeArray<Color> Colors;

        public void Execute(int index) {
            int x = index % GridWidth;
            int y = index / GridHeight;

            float3 pos = new float3(-MapWidth / 2 + x + CellSize / 2, 0f, -MapHeight / 2 + y + CellSize / 2);
            bool isActive = Colors[index].grayscale > 0.37f ? true : false; // TODO change threshold (0.37 is because (94, 94, 94) is sea level, so 0.299 * R + 0.587 * G + 0.114 * B is 0.37 (where the RGB is 94/255)
            ControlNodes[index] = new ControlNode(pos, index, isActive, CellSize);
        }
    }

    /// <summary>
    /// Initialize every cell with their respective control nodes (the four corners)
    /// </summary>
    [BurstCompile]
    private struct InitializeCellsJob : IJobParallelFor {
        public int GridWidth;
        public int GridHeight;

        [NativeDisableParallelForRestriction]
        public NativeArray<Cell> Grid;

        [ReadOnly]
        public NativeArray<ControlNode> ControlNodes;

        public void Execute(int index) {
            int x = index % GridWidth;
            int y = index / GridHeight;

            if (x == GridWidth - 1 || y == GridHeight - 1) {
                return;
            }

            var topLeft = ControlNodes[index + GridWidth];
            var topRight = ControlNodes[index + GridWidth + 1];
            var bottomLeft = ControlNodes[index];
            var bottomRight = ControlNodes[index + 1];

            Grid[index] = new Cell(topLeft, topRight, bottomLeft, bottomRight);
        }
    }

    public struct Node {
        public float3 Position;
        public int VertexIndex;

        public Node(float3 position) {
            this.Position = position;
            this.VertexIndex = -1;
        }
    }

    public struct ControlNode {
        public float3 position;
        public int vertexIndex;
        public bool isActive;
        public Node topNode, rightNode;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="position"></param> The world position of the cell
        /// <param name="vertexIndex"></param> The cell's index for the bitmap
        /// <param name="active"></param> Whether the control node is active in making the mesh
        /// <param name="cellSize"></param> The size of each grid cell
        public ControlNode(float3 position, int vertexIndex, bool active, float cellSize) {
            this.position = position;
            this.vertexIndex = vertexIndex;
            this.isActive = active;
            this.topNode = new Node(position + new float3(0f, 0f, 1f) * cellSize/2); // TODO stop instiating so many of the same floats (but idk how to have a static field w/ burst)
            this.rightNode = new Node(position + new float3(1f, 0f, 0f) * cellSize/2);
        }
    }

    public struct Cell {

        public ControlNode topLeft, topRight, bottomLeft, bottomRight;
        public Node centerTop, centerRight, centerLeft, centerDown;
        public int Configuration; // one of the 16 possible states

        public Cell(ControlNode topLeft, ControlNode topRight, ControlNode bottomLeft, ControlNode bottomRight) {
            this.topLeft = topLeft;
            this.topRight = topRight;
            this.bottomLeft = bottomLeft;
            this.bottomRight = bottomRight;

            centerTop = topLeft.rightNode;
            centerRight = bottomRight.topNode;
            centerLeft = bottomLeft.topNode;
            centerDown = bottomLeft.rightNode;
            
            Configuration = 0;
            if (topLeft.isActive) {
                Configuration += 0x1000;
            }
            if (topRight.isActive) {
                Configuration += 0x0100;
            }
            if (bottomRight.isActive) {
                Configuration += 0x0010;
            }
            if (bottomLeft.isActive) {
                Configuration += 0x0001;
            }

        }
    }
}
