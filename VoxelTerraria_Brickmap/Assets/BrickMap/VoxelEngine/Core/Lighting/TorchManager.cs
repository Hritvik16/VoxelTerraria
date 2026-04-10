using UnityEngine;
using System.Collections.Generic;

// 1. Define the exact memory layout we will send to the GPU
public struct TorchData {
    public Vector3 position;
    public float radius;
    public Vector3 color;
    public float intensity;
}

[ExecuteAlways]
public class TorchManager : MonoBehaviour
{
    public static TorchManager Instance;

    [Header("Torches")]
    // 2. We now track standard Unity Lights instead of Transforms!
    public List<Light> torches = new List<Light>();

    private ComputeBuffer torchBuffer;
    private TorchData[] torchDataArray = new TorchData[100]; // Max 100 for now

    void OnEnable()
    {
        Instance = this;
    }

    void Update()
    {
        if (torches.Count == 0) {
            Shader.SetGlobalInt("_TorchCount", 0);
            return;
        }

        // 3. Pack the Unity Light data into our custom struct
        int activeCount = Mathf.Min(torches.Count, 100);
        for (int i = 0; i < activeCount; i++) {
            if (torches[i] != null) {
                torchDataArray[i] = new TorchData {
                    position = torches[i].transform.position,
                    radius = torches[i].range, // Unity's light radius
                    color = new Vector3(torches[i].color.r, torches[i].color.g, torches[i].color.b),
                    intensity = torches[i].intensity
                };
            }
        }

        // 4. Initialize the buffer with the exact byte size (Stride)
        // Stride: 3 floats (pos) + 1 float (rad) + 3 floats (col) + 1 float (int) = 8 floats * 4 bytes = 32 bytes
        if (torchBuffer == null || !torchBuffer.IsValid()) {
            torchBuffer = new ComputeBuffer(100, 32);
        }

        // 5. Upload to the GPU
        torchBuffer.SetData(torchDataArray);
        Shader.SetGlobalBuffer("_TorchBuffer", torchBuffer);
        Shader.SetGlobalInt("_TorchCount", activeCount);
    }

    void OnDisable()
    {
        if (torchBuffer != null) {
            torchBuffer.Release();
            torchBuffer = null;
        }
    }
}
