# Copilot / Agent Instructions — Voxel-Terraria

Short, actionable guidance to make an AI coding agent productive in this Unity project.

1) Big picture
- Project: Unity voxel island generator. Main runtime pieces live under `Assets/Scripts/World` and `Assets/Scripts/World/SDF` (SDF terrain evaluation + feature packing). Voxel meshing & generation live in `Assets/Scripts/World/Generation` (see `VoxelGenerator.cs`).
- Data & editor-facing assets live under `Assets/Scripts/Data` (ScriptableObjects for `WorldSettings` and feature SOs such as `ForestFeature.cs`). `SdfBootstrap.cs` (ExecuteAlways) converts these SOs into a Burst-friendly `SdfContext` used at runtime.

2) Key files to read first
- `Assets/Scripts/World/SDF/FeatureSO.cs` — base class for all feature ScriptableObjects (important: `ToFeature()` must return an unmanaged `Feature`).
- `Assets/Scripts/World/SDF/Feature.cs` — the unmanaged struct used by Burst jobs. Do NOT introduce managed fields here.
- `Assets/Scripts/World/SDF/SdfBootstrap.cs` and `SdfBootstrapInternal.cs` — editor bootstrap that builds the `SdfContext` from SOs.
- `Assets/Scripts/World/Generation/VoxelGenerator.cs` — shows how SDF evaluation is used inside Burst jobs to produce voxels.
- `Assets/Scripts/Data/WorldSettings.cs` — global tuning flags (voxel size, chunk size, `useJobs`, `useBurst`, etc.).

3) Important conventions & constraints (project-specific)
- All data passed into Burst jobs must be unmanaged: prefer `struct` with primitive fields (see `Feature` and `SdfContext`). Do not store `UnityEngine.Object`, `GameObject`, `Texture`, `List<T>`, or other managed types inside these structs.
- Editor-facing configuration is implemented as ScriptableObjects under `Assets/Scripts/Data` and `Assets/Scripts/Data/Features` (examples: `ForestFeature.cs`, `MountainFeature.cs`). To add a new feature:
  - Add a `FeatureSO` subclass in `Assets/Scripts/World/SDF` that implements `ToFeature()` returning a packed `Feature`.
  - Add a corresponding ScriptableObject asset under `Assets/Scripts/Data/Features` (use `CreateAssetMenu` pattern used by existing features).
  - Ensure `SdfBootstrapInternal` knows how to convert it (search for similar feature adapters under `FeatureAdapters`).
- Naming: SOs end with `Feature`/`Settings`; runtime structs are suffixed with `Data` or are plain `Feature` (see examples).

4) Typical workflows and developer tips
- Quick local iteration: open the project in Unity Editor and attach `SdfBootstrap` to an Editor GameObject (it is `ExecuteAlways`) — it will build the `SdfContext` from ScriptableObjects for the editor and play-mode.
- To test generation for a chunk, run the scene and use provided debug utilities (e.g., `Raw3DDebug.cs`) or inspect generated `ChunkData`/`VoxelChunkView` while `SdfRuntime.Initialized` is true.
- Toggle `useJobs` and `useBurst` in `WorldSettings` to isolate concurrency or Burst-related bugs.
- When adding new Burst-enabled code, run with `WorldSettings.useBurst = false` first to reproduce logic without Burst optimizations.

5) Integration & external dependencies
- Uses Unity packages: `Unity.Burst`, `Unity.Collections`, `Unity.Jobs`, `Unity.Mathematics`. Ensure the Editor has these packages installed (Package Manager).
- The project relies on Burst-friendly design — tests or code changes that add managed references to runtime structs will break jobs or cause IL2CPP/Burst compile errors.

6) Debugging guidance
- If you see Burst compile errors, inspect the types passed into the job (look at `GenerateVoxelsJob` in `VoxelGenerator.cs`). Check for managed members in `SdfContext`, `Feature`, or other structs.
- Memory leaks: `SdfRuntime.SetContext()` disposes previous contexts. If you add allocs, ensure `Dispose()` is called (see `SdfRuntime.Dispose()`).
- Fast path: `SdfRuntime.FastRejectChunk()` is used to skip chunk generation — changes to SDF evaluation impact chunk culling.

7) Examples (how to add a Feature)
- Create `Assets/Scripts/World/SDF/MyFancyFeatureSO.cs`:
  - Inherit `FeatureSO` and implement `ToFeature()` to fill `Feature` fields (pack parameters into `data0/data1/data2` and `centerXZ`).
- Create a ScriptableObject asset in `Assets/Scripts/Data/Features/` using `CreateAssetMenu` entry.
- Open an object with `SdfBootstrap` in the scene to pick up the new feature and verify `SdfRuntime` behaves correctly.

8) What not to change without careful checks
- Do not change the public layout of `Feature` or `SdfContext` without updating all `ToFeature()` implementations and `SdfBootstrapInternal`.
- Avoid adding managed fields to types used in jobs; this will cause Burst/Jobs failures.

If anything in this file looks incomplete or you want more detail (build scripts, CI, or prefabs to open), tell me which area to expand and I will iterate.
