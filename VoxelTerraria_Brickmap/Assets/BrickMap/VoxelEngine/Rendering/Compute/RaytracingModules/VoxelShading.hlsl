// Evaluates the Voxel's macro-environment to assign a single, uniform Material ID
uint GetProceduralMaterial(int3 voxelPos, int layer) {
    // 1. CALCULATE MACRO-SLOPE (Density Gradient)
    // Normal points from Solid (1) to Air (0)
    float nx = (float)IsSolid1Bit(voxelPos + int3(-1, 0, 0), layer) - (float)IsSolid1Bit(voxelPos + int3(1, 0, 0), layer);
    float ny = (float)IsSolid1Bit(voxelPos + int3(0, -1, 0), layer) - (float)IsSolid1Bit(voxelPos + int3(0, 1, 0), layer);
    float nz = (float)IsSolid1Bit(voxelPos + int3(0, 0, -1), layer) - (float)IsSolid1Bit(voxelPos + int3(0, 0, 1), layer);
    
    // If the block is completely buried (no gradient), default to pointing up
    float3 macroNormal = length(float3(nx, ny, nz)) > 0.1 ? normalize(float3(nx, ny, nz)) : float3(0, 1, 0);

    // 2. SURFACE & SLOPE RULES
    // Is there air directly above this specific block?
    bool isSurface = !IsSolid1Bit(voxelPos + int3(0, 1, 0), layer); 
    // Is the general terrain steep here?
    bool isSteepSlope = macroNormal.y < 0.6; 

    // 3. BIOME EVALUATION
    int currentBiome = 0; 
    float closestDist = 999999.0;
    float3 worldPos = voxelPos * _ClipmapCenters[layer].w;

    for (int i = 0; i < _BiomeAnchorCount; i++) {
        BiomeAnchor anchor = _BiomeAnchors[i];
        float volumeNoise = (hash(worldPos * 0.05) - 0.5) * 20.0; 
        float distToAnchor = distance(worldPos, anchor.position) + volumeNoise;
        
        if (distToAnchor < anchor.radius && distToAnchor < closestDist) {
            closestDist = distToAnchor;
            currentBiome = anchor.biomeType;
        }
    }

    // Temporary Test Biome Split (Remove once C# buffer is wired)
    if (worldPos.x > 0) currentBiome = 1; 

    // 4. THE MATERIAL ASSIGNMENT (Whole Voxel gets one ID)
    switch (currentBiome) {
        case 0: // FOREST BIOME
            if (isSteepSlope) return 3;       // Stone Cliffs
            if (isSurface) return 1;          // Full Grass Block
            return 2;                         // Dirt Block
            
        case 1: // DESERT BIOME
            if (isSteepSlope) return 6;       // Hard Sandstone Cliffs
            if (isSurface) return 4;          // Full Sand Block
            return 5;                         // Soft Sandstone
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
