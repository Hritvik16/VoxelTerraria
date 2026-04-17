RWTexture2D<float4> Result;
// [0] = Hit Position & Status. [1] = Geometric Face Normal
RWStructuredBuffer<int4> _CrosshairTarget;
float4x4 _CameraToWorld;
float4x4 _CameraInverseProjection;
float4 _CameraForward;
int _PoolSize;

struct BiomeAnchor {
    float3 position;
    float radius;
    int biomeType; // 0 = Forest, 1 = Desert, 2 = Winter, etc.
};

StructuredBuffer<BiomeAnchor> _BiomeAnchors;
int _BiomeAnchorCount;

struct SVONode { uint childIndex; uint material; };
struct ChunkData { uint packedState; uint densePoolIndex; };

StructuredBuffer<SVONode> _SVOPool;
StructuredBuffer<uint> _DenseChunkPool;
StructuredBuffer<uint> _MacroMaskPool; 
StructuredBuffer<float> _ChunkHeightMap;
StructuredBuffer<ChunkData> _ChunkMap;

int _ChunkCount;
float4 _ClipmapCenters[8];
int _ClipmapLayers;
float4 _RenderBounds;
int _DebugViewMode;
int _ShowRedWireframe;
float3 _SunDir;

int _IsEditMode;
int _EditMode;
int _BrushSize;
int _BrushShape;
float _VoxelScale;

// NEW: The exact block the crosshair is looking at
float4 _TargetBlock;
float4 _TargetNormal;

struct Ray { float3 origin; float3 direction; };

struct VoxelMaterial {
    float4 albedo;
    float roughness;
    float metallic;
    float emission;
    float padding;
};

StructuredBuffer<VoxelMaterial> _MaterialPalette;


// --- PHASE 4: FLUID RAYTRACING ---
StructuredBuffer<uint> _DynamicFluids;
StructuredBuffer<uint> _ChunkFluidPointers;

uint Trace_GetFluidData(uint ticket, int3 localPos) {
    uint flatIdx = localPos.x + (localPos.y << 5) + (localPos.z << 10);
    uint uintIdx = flatIdx >> 1;
    uint shift = (flatIdx & 1) * 16;
    return (_DynamicFluids[(ticket * 16384) + uintIdx] >> shift) & 0xFFFF;
}

