// --- VoxelFastPath.hlsl ---
// Bypasses GetChunkData by checking if the neighbor is inside the chunk we already fetched
bool FastIsSolid1Bit(int3 globalPos, int3 baseChunkCoord, uint baseDenseBase, int layer) {
    int3 chunkCoord = globalPos >> 5;
    
    // FAST PATH: We are still inside the same 32x32x32 chunk!
    if (all(chunkCoord == baseChunkCoord)) {
        int3 localPos = globalPos & 31;
        int flatIdx = localPos.x + (localPos.y << 5) + (localPos.z << 10);
        uint matData = _DenseChunkPool[baseDenseBase + (flatIdx >> 5)];
        return (matData & (1u << (flatIdx & 31))) != 0;
    }
    
    // SLOW PATH: We crossed a chunk boundary. Fall back to the heavy global check.
    return IsSolid1Bit(globalPos, layer); 
}
