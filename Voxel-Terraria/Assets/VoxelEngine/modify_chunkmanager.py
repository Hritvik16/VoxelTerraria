import re
with open('/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria/VoxelTerraria/Voxel-Terraria/Assets/VoxelEngine/Scripts/ChunkManager.cs', 'r') as f:
    text = f.read()

# Edit 1: tempDenseBuffer declaration
text = text.replace(
    'private ComputeBuffer svoPoolBuffer, chunkMapBuffer, jobQueueBuffer;',
    'private ComputeBuffer svoPoolBuffer, chunkMapBuffer, jobQueueBuffer;\n    private ComputeBuffer tempDenseBuffer; // NEW: The flat 1D array for bottom-up SVO generation'
)

# Edit 2: tempDenseBuffer creation
search2 = '''        if (jobQueueBuffer == null) jobQueueBuffer = new ComputeBuffer(maxConcurrentJobs, 48); // FIX: 48 bytes for proper stride'''
replace2 = '''        if (jobQueueBuffer == null) jobQueueBuffer = new ComputeBuffer(maxConcurrentJobs, 48); // FIX: 48 bytes for proper stride
        
        if (tempDenseBuffer == null) {
            // Allocate exact space for the 37,449 SVO mipmap tree nodes multiplied by max jobs
            tempDenseBuffer = new ComputeBuffer(maxConcurrentJobs * 37449, sizeof(uint));
            Shader.SetGlobalBuffer("_TempDenseBuffer", tempDenseBuffer);
        }'''
text = text.replace(search2, replace2)

# Edit 3: Dispatch mapping
search3_start = "int kernel = worldGenShader.FindKernel(\"GenerateChunk\");"
search3_end = "// --- PHASE 3: THE STUTTER-FIX DISPATCH ---"

pattern3 = re.compile(re.escape(search3_start) + r".*?" + re.escape(search3_end), re.DOTALL)
replace3 = """int kEval = worldGenShader.FindKernel("EvaluateVoxels");
            int kReduce1 = worldGenShader.FindKernel("ReduceL1");
            int kReduce2 = worldGenShader.FindKernel("ReduceL2");
            int kReduce3 = worldGenShader.FindKernel("ReduceL3");
            int kReduce4 = worldGenShader.FindKernel("ReduceL4");
            int kReduce5 = worldGenShader.FindKernel("ReduceL5");
            int kConstruct = worldGenShader.FindKernel("ConstructSVO");

            worldGenShader.SetBuffer(kEval, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kEval, "_JobQueue", jobQueueBuffer); 
            worldGenShader.SetBuffer(kEval, "_DeltaMapBuffer", deltaMapBuffer);

            worldGenShader.SetBuffer(kReduce1, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kReduce2, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kReduce3, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kReduce4, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kReduce5, "_TempDenseBuffer", tempDenseBuffer);

            worldGenShader.SetBuffer(kConstruct, "_TempDenseBuffer", tempDenseBuffer);
            worldGenShader.SetBuffer(kConstruct, "_SVOPool", svoPoolBuffer);
            worldGenShader.SetBuffer(kConstruct, "_ChunkMap", chunkMapBuffer);
            worldGenShader.SetBuffer(kConstruct, "_JobQueue", jobQueueBuffer);
            
            worldGenShader.SetInt("_JobCount", dispatchesThisFrame);

            // TEST 3: THE ALU PROFILER
            Stopwatch aluTimer = Stopwatch.StartNew();
            
            worldGenShader.Dispatch(kEval, dispatchesThisFrame * 4, 4, 4);
            worldGenShader.Dispatch(kReduce1, dispatchesThisFrame * 2, 2, 2);
            worldGenShader.Dispatch(kReduce2, dispatchesThisFrame, 1, 1);
            worldGenShader.Dispatch(kReduce3, dispatchesThisFrame, 1, 1);
            worldGenShader.Dispatch(kReduce4, dispatchesThisFrame, 1, 1);
            worldGenShader.Dispatch(kReduce5, dispatchesThisFrame, 1, 1);
            worldGenShader.Dispatch(kConstruct, dispatchesThisFrame, 1, 1);
            
            aluTimer.Stop();

            // --- PHASE 3: THE STUTTER-FIX DISPATCH ---"""
text = pattern3.sub(replace3, text)

# Edit 4: Release buffer
text = text.replace(
    'jobQueueBuffer?.Release();',
    'jobQueueBuffer?.Release();\n        tempDenseBuffer?.Release();'
)
text = text.replace(
    'jobQueueBuffer = null;',
    'jobQueueBuffer = null;\n        tempDenseBuffer = null;'
)

with open('/Users/hritvikjv/Desktop/Personal Projects/Unity/Voxel Terraria/VoxelTerraria/Voxel-Terraria/Assets/VoxelEngine/Scripts/ChunkManager.cs', 'w') as f:
    f.write(text)

