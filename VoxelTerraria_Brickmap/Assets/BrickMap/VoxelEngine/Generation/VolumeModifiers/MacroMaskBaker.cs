using Unity.Collections;

namespace VoxelEngine.Generation
{
    public static class MacroMaskBaker
    {
        public static void Bake1Bit(ref NativeArray<uint> denseChunkPool, uint denseBase, ref NativeArray<uint> macroMaskPool, int maskBase)
        {
            for (int i = 0; i < 19; i++) macroMaskPool[maskBase + i] = 0; 
            
            // LEVEL 0: 4x4x4 (16 Uints)
            for (int x = 0; x < 32; x++) {
                for (int y = 0; y < 32; y++) {
                    for (int z = 0; z < 32; z++) {
                        int flatIdx = x + (y << 5) + (z << 10);
                        if ((denseChunkPool[(int)denseBase + (flatIdx >> 5)] & (1u << (flatIdx & 31))) != 0) {
                            int sx = x >> 2; int sy = y >> 2; int sz = z >> 2;
                            int subIndex = sx + (sy << 3) + (sz << 6);
                            macroMaskPool[maskBase + (subIndex >> 5)] |= (1u << (subIndex & 31));
                        }
                    }
                }
            }

            // LEVEL 1: 8x8x8 (2 Uints / 64 Bits)
            for(int i = 0; i < 512; i++) {
                if ((macroMaskPool[maskBase + (i >> 5)] & (1u << (i & 31))) != 0) {
                    int mx = (i & 7) >> 1;        // x in 0..3
                    int my = ((i >> 3) & 7) >> 1; // y in 0..3
                    int mz = (i >> 6) >> 1;       // z in 0..3
                    int mip1Idx = mx + (my << 2) + (mz << 4);
                    macroMaskPool[maskBase + 16 + (mip1Idx >> 5)] |= (1u << (mip1Idx & 31));
                }
            }

            // LEVEL 2: 16x16x16 (1 Uint / 8 Bits)
            for(int i = 0; i < 64; i++) {
                if ((macroMaskPool[maskBase + 16 + (i >> 5)] & (1u << (i & 31))) != 0) {
                    int mx = (i & 3) >> 1;        // x in 0..1
                    int my = ((i >> 2) & 3) >> 1; // y in 0..1
                    int mz = (i >> 4) >> 1;       // z in 0..1
                    int mip2Idx = mx + (my << 1) + (mz << 2);
                    macroMaskPool[maskBase + 18] |= (1u << mip2Idx);
                }
            }
        }
        
        public static void Bake32Bit(ref NativeArray<uint> denseChunkPool, uint denseBase, ref NativeArray<uint> macroMaskPool, int maskBase)
        {
            for (int i = 0; i < 16; i++) macroMaskPool[maskBase + i] = 0; // Clear the memory
            
            for (int x = 0; x < 32; x++) {
                for (int y = 0; y < 32; y++) {
                    for (int z = 0; z < 32; z++) {
                        int flatIdx = x + (y << 5) + (z << 10);
                        if (denseChunkPool[(int)denseBase + flatIdx] > 0) { // 32bit check
                            int sx = x >> 2; int sy = y >> 2; int sz = z >> 2;
                            int subIndex = sx + (sy << 3) + (sz << 6);
                            macroMaskPool[maskBase + (subIndex >> 5)] |= (1u << (subIndex & 31));
                        }
                    }
                }
            }
        }
    }
}
