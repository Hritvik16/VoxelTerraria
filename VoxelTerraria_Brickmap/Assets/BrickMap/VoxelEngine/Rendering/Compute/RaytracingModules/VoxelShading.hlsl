// Evaluates the Voxel's macro-environment to assign a single, uniform Material ID
uint GetProceduralMaterial(int3 voxelPos, int3 baseChunkCoord, uint denseBase, int layer) {
    // 1. CALCULATE MACRO-SLOPE (Density Gradient)
    float nx = (float)FastIsSolid1Bit(voxelPos + int3(-1, 0, 0), baseChunkCoord, denseBase, layer) - (float)FastIsSolid1Bit(voxelPos + int3(1, 0, 0), baseChunkCoord, denseBase, layer);
    float ny = (float)FastIsSolid1Bit(voxelPos + int3(0, -1, 0), baseChunkCoord, denseBase, layer) - (float)FastIsSolid1Bit(voxelPos + int3(0, 1, 0), baseChunkCoord, denseBase, layer);
    float nz = (float)FastIsSolid1Bit(voxelPos + int3(0, 0, -1), baseChunkCoord, denseBase, layer) - (float)FastIsSolid1Bit(voxelPos + int3(0, 0, 1), baseChunkCoord, denseBase, layer);
    
    float3 macroNormal = length(float3(nx, ny, nz)) > 0.1 ? normalize(float3(nx, ny, nz)) : float3(0, 1, 0);

    // 2. SURFACE & SLOPE RULES
    bool isSurface = !FastIsSolid1Bit(voxelPos + int3(0, 1, 0), baseChunkCoord, denseBase, layer); 
    bool isSteepSlope = macroNormal.y < 0.6; 

    // 3. BIOME EVALUATION (Perturbed Voronoi)
    int currentBiome = 0;
    float closestDist = 999999.0;
    float3 worldPos = voxelPos * _ClipmapCenters[layer].w;

    // The Large-Scale Noise Warp (Makes borders snake naturally)
    float boundaryWarp = (hash(worldPos * 0.005) - 0.5) * 150.0; 

    for (int i = 0; i < _BiomeAnchorCount; i++) {
        BiomeAnchor anchor = _BiomeAnchors[i];
        
        // Add the noise warp to the distance check
        float distToAnchor = distance(worldPos, anchor.position) + boundaryWarp;
        
        // Pure Nearest-Neighbor search (No radius gaps!)
        if (distToAnchor < closestDist) {
            closestDist = distToAnchor;
            currentBiome = anchor.biomeType;
        }
    }

    // 4. THE MATERIAL ASSIGNMENT (Using your exact Palette Indices)
    switch (currentBiome) {
        case 0: // FOREST BIOME
            if (isSteepSlope) return 3;       // Stone Cliffs
            if (isSurface) return 1;          // Grass
            return 2;                         // Dirt
            
        case 1: // DESERT BIOME
            if (isSteepSlope) return 6;       // Hard Sandstone
            if (isSurface) return 4;          // Sand
            return 5;                         // Soft Sandstone
            
        case 2: // SNOW BIOME
            if (isSteepSlope) return 3;       // Stone Cliffs
            if (isSurface) return 7;          // Snow (Index 7)
            return 8;                         // Ice/Permafrost (Index 8)

        case 3: // JUNGLE BIOME
            if (isSteepSlope) return 3;       // Stone Cliffs
            if (isSurface) return 10;         // Dark Jungle Grass (Index 10)
            return 12;                        // Mud/Moss (Index 12)

        case 4: // EVIL / VOLCANIC BIOME
            if (isSteepSlope) return 13;      // Dark Slate (Index 13)
            if (isSurface) return 14;         // Glowing Red Lava (Index 14)
            return 11;                        // Dark Ash/Dirt (Index 11)
    }

    return 3; 
}

#if !DUAL_STATE_1BIT
void UnpackLight(uint data, out uint matID, out float r, out float g, out float b, out float sun, out uint ao) {
    matID = data & 0xFF;
    r = ((data >> 8) & 0xF) / 15.0f;
    g = ((data >> 12) & 0xF) / 15.0f;
    b = ((data >> 16) & 0xF) / 15.0f;
    sun = ((data >> 20) & 0xF) / 15.0f;
    ao = (data >> 24) & 0xFF;
}
#endif
