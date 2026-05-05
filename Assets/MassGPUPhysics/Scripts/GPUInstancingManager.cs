using UnityEngine;
using System.Runtime.InteropServices;

public class GPUInstancingManager : MonoBehaviour
{
    // The exact same struct used in Compute Shader and Rendering Shader
    [System.Serializable]
    public struct AgentData
    {
        public Vector3 position;
        public Vector3 rotation; // Euler angles
        public Vector3 scale;
    }

    [Header("Instancing Settings")]
    [Tooltip("Number of instances to simulate and render")]
    public int instanceCount = 100000;
    public Mesh instanceMesh;
    public Material instanceMaterial;
    public ComputeShader computeShader;
    
    [Header("Spawn Settings")]
    public Vector3 spawnArea = new Vector3(100f, 0f, 100f);

    private ComputeBuffer agentBuffer;
    private ComputeBuffer argsBuffer;
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private int csKernel;

    void Start()
    {
        InitializeBuffers();
    }

    void InitializeBuffers()
    {
        if (instanceMesh == null || instanceMaterial == null || computeShader == null)
        {
            Debug.LogError("GPUInstancingManager: Missing Mesh, Material, or Compute Shader references.");
            return;
        }

        // 强行开启材质的 Instancing，确保 Shader 中的 procedural:setup 逻辑能正常跑通
        if (!instanceMaterial.enableInstancing)
        {
            instanceMaterial.enableInstancing = true;
        }

        // 1. Create Agent Data Buffer
        int stride = Marshal.SizeOf(typeof(AgentData)); // 3 * 3 * 4 = 36 bytes
        agentBuffer = new ComputeBuffer(instanceCount, stride);

        // Initialize mock data
        AgentData[] initialData = new AgentData[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            initialData[i] = new AgentData
            {
                position = new Vector3(
                    Random.Range(-spawnArea.x, spawnArea.x), 
                    Random.Range(-spawnArea.y, spawnArea.y), 
                    Random.Range(-spawnArea.z, spawnArea.z)
                ),
                rotation = new Vector3(0, Random.Range(0, 360f), 0),
                scale = Vector3.one
            };
        }
        agentBuffer.SetData(initialData);

        // 2. Setup arguments buffer for Graphics.DrawMeshInstancedIndirect
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        // args[0]: index count per instance (triangles)
        // args[1]: instance count
        // args[2]: start index location
        // args[3]: base vertex location
        // args[4]: start instance location
        args[0] = (uint)instanceMesh.GetIndexCount(0);
        args[1] = (uint)instanceCount;
        args[2] = (uint)instanceMesh.GetIndexStart(0);
        args[3] = (uint)instanceMesh.GetBaseVertex(0);
        args[4] = 0;
        argsBuffer.SetData(args);

        // 3. Bind buffers to Compute Shader
        csKernel = computeShader.FindKernel("CSMain");
        computeShader.SetBuffer(csKernel, "agentBuffer", agentBuffer);

        // 4. Bind buffers to Rendering Material
        instanceMaterial.SetBuffer("agentBuffer", agentBuffer);
    }

    void Update()
    {
        if (agentBuffer == null || argsBuffer == null) return;

        // 1. Run Compute Shader to update logic/positions
        computeShader.SetFloat("deltaTime", Time.deltaTime);
        
        // Dispatch threads. 64 is the [numthreads(64,1,1)] defined in the compute shader
        int threadGroupsX = Mathf.CeilToInt(instanceCount / 64f); 
        computeShader.Dispatch(csKernel, threadGroupsX, 1, 1);

        // 2. Issue the draw call to the GPU
        // We use a large bounds so it's always rendered. For culling, a more complex bounds calculation is needed.
        Bounds renderBounds = new Bounds(Vector3.zero, new Vector3(10000, 10000, 10000));
        Graphics.DrawMeshInstancedIndirect(instanceMesh, 0, instanceMaterial, renderBounds, argsBuffer);
    }

    void OnDisable()
    {
        ReleaseBuffers();
    }

    void ReleaseBuffers()
    {
        if (agentBuffer != null)
        {
            agentBuffer.Release();
            agentBuffer = null;
        }
        if (argsBuffer != null)
        {
            argsBuffer.Release();
            argsBuffer = null;
        }
    }
}
