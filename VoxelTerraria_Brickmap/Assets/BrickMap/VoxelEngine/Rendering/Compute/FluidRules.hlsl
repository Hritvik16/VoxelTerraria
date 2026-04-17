// ==========================================
// FluidRules.hlsl - THE PHYSICS SANDBOX
// ==========================================

uint HashVoxel(uint3 p) {
    uint state = p.x * 73856093u + p.y * 19349663u + p.z * 83492791u;
    state ^= state >> 16;
    state *= 2654435769u;
    state ^= state >> 16;
    state *= 2654435769u;
    state ^= state >> 16;
    return state;
}

bool CheckSpace(int3 globalPos, out uint outTicket, out int3 outLocalPos, out uint outMatID) {
    outLocalPos = globalPos & 31;
    outMatID = 0;
    if (IsSolid1Bit(globalPos, 0)) return false; // Blocked by Static Terrain
    
    int3 chunkCoord = globalPos >> 5;
    int sideXZ = 2 * (int)_RenderBounds.x + 1;
    int sideY = 2 * (int)_RenderBounds.y + 1;
    int mx = (int)(((uint)(chunkCoord.x + 400000 * sideXZ)) % (uint)sideXZ);
    int my = (int)(((uint)(chunkCoord.y + 400000 * sideY)) % (uint)sideY);
    int mz = (int)(((uint)(chunkCoord.z + 400000 * sideXZ)) % (uint)sideXZ);
    uint mapIndex = mx + mz * sideXZ + my * sideXZ * sideXZ;
    
    outTicket = _ChunkFluidPointers[mapIndex];
    outMatID = GetFluidData(outTicket, outLocalPos) & 0x3F; 
    
    return (outMatID == 0); // True if it's air
}
int3 GetDir(uint dir) {
    if (dir == 0) return int3(1, 0, 0);
    if (dir == 1) return int3(1, 0, 1);
    if (dir == 2) return int3(0, 0, 1);
    if (dir == 3) return int3(-1, 0, 1);
    if (dir == 4) return int3(-1, 0, 0);
    if (dir == 5) return int3(-1, 0, -1);
    if (dir == 6) return int3(0, 0, -1);
    return int3(1, 0, -1);
}

// --- THE VACUUM SLAP HELPER ---
void WakeUpVoxel(int3 globalPos, uint nextPhase) {
    uint tTicket; int3 tLocal; uint tMat;
    bool isAir = CheckSpace(globalPos, tTicket, tLocal, tMat);
    
    if (!isAir && tMat > 0 && tTicket != 0xFFFFFFFF) { 
        uint rawData = GetFluidData(tTicket, tLocal);
        if (((rawData >> 13) & 0x01) == 0) { // If it is currently asleep...
            uint newData = rawData | (1 << 13); // Force Awake to 1
            newData = (newData & ~(1 << 14)) | (nextPhase << 14); // Sync phase
            WriteFluidData(tTicket, tLocal, newData);
            InterlockedMax(_ChunkAwakeFlags[tTicket], 2); // Keep the chunk alive
        }
    }
}

// --- THE TRUE OMNIDIRECTIONAL VACUUM SLAP ---
void WakeUpNeighbors(int3 globalPos, uint nextPhase) {
    WakeUpVoxel(globalPos + int3(0, 1, 0), nextPhase);  // Up
    WakeUpVoxel(globalPos + int3(0, -1, 0), nextPhase); // Down
    WakeUpVoxel(globalPos + int3(1, 0, 0), nextPhase);  // Right
    WakeUpVoxel(globalPos + int3(-1, 0, 0), nextPhase); // Left
    WakeUpVoxel(globalPos + int3(0, 0, 1), nextPhase);  // Forward
    WakeUpVoxel(globalPos + int3(0, 0, -1), nextPhase); // Back
}

[numthreads(8,8,8)]
void CSMain (uint3 groupID : SV_GroupID, uint3 groupThreadID : SV_GroupThreadID)
{
    uint chunkIndex = groupID.x / 2;
    if (chunkIndex >= (uint)_DispatchCount) return;

    int4 dispatchData = _ActiveTickets[chunkIndex];
    uint currentTicket = (uint)dispatchData.x;
    
    if (_ChunkAwakeFlags[currentTicket] == 0) return; // Asleep!
    
    int3 chunkCoord = dispatchData.yzw; 
    
    int localGroupX = groupID.x % 2;
    int localX = (localGroupX * 8) + groupThreadID.x;
    int localY = groupID.y * 8 + groupThreadID.y;
    int localZ = groupID.z * 8 + groupThreadID.z;
    
    int3 localPos = int3(localX, localY, localZ) * 2 + (int3)_CheckerOffset.xyz;
    if (any(localPos < 0) || any(localPos > 31)) return;

    uint currentPhase = _Tick & 1;
    uint nextPhase = 1 - currentPhase;

    uint rawData = GetFluidData(currentTicket, localPos);
    uint myMat = rawData & 0x3F;
    uint myDensity = (rawData >> 6) & 0x0F;
    uint myDir = (rawData >> 10) & 0x07;      
    uint isAwake = (rawData >> 13) & 0x01;
    uint myPhase = (rawData >> 14) & 0x01; 
    
    // FIX 2D: Override the voxel's sleep bit if the GPU Janitor demands a Force-Wake!
    bool forceWake = (_ChunkAwakeFlags[currentTicket] >= 3);
    
    if (myMat == 0 || myPhase != currentPhase) return;
    if (!forceWake && isAwake == 0) return;
    
    // If we are force-waking, keep the chunk at its high state, otherwise set to normal awake (2).
    if (!forceWake) _ChunkAwakeFlags[currentTicket] = 2;
    
    int3 globalPos = (chunkCoord * 32) + localPos;

    uint targetTicket; int3 targetLocal; uint dummyMat;

    // --- RULE 1: DIRECT FALL ---
    int3 downPos = globalPos + int3(0, -1, 0);
    if (CheckSpace(downPos, targetTicket, targetLocal, dummyMat)) {
        if (targetTicket == 0xFFFFFFFF) {
            _TicketRequestsInbox.Append(downPos >> 5); // ASK CPU FOR MEMORY
            
            // THE NARCOLEPSY FIX: Wait for memory, but STAY AWAKE and FLIP PHASE!
            WriteFluidData(currentTicket, localPos, (myMat & 0x3F) | (myDensity << 6) | (myDir << 10) | (1 << 13) | (nextPhase << 14));
            InterlockedMax(_ChunkAwakeFlags[currentTicket], 2);
            return; 
        }
        
        WakeUpNeighbors(globalPos, nextPhase); // THE VACUUM SLAP! 
        WriteFluidData(currentTicket, localPos, 0);
        WriteFluidData(targetTicket, targetLocal, (myMat & 0x3F) | (myDensity << 6) | (myDir << 10) | (1 << 13) | (nextPhase << 14));
        InterlockedMax(_ChunkAwakeFlags[targetTicket], 2); 
        return;
    }

    // --- RULE 2: DIAGONAL DRAIN ---
    uint searchOrder[8] = { myDir, (myDir+1)&7, (myDir+7)&7, (myDir+2)&7, (myDir+6)&7, (myDir+3)&7, (myDir+5)&7, (myDir+4)&7 };
    for (int i = 0; i < 8; i++) {
        uint testDir = searchOrder[i];
        int3 testPos = globalPos + GetDir(testDir) + int3(0, -1, 0);
        
        if (CheckSpace(testPos, targetTicket, targetLocal, dummyMat)) {
            if (targetTicket == 0xFFFFFFFF) {
                _TicketRequestsInbox.Append(testPos >> 5); // ASK CPU FOR MEMORY
                
                // THE NARCOLEPSY FIX: Wait for memory, but STAY AWAKE and FLIP PHASE!
                WriteFluidData(currentTicket, localPos, (myMat & 0x3F) | (myDensity << 6) | (myDir << 10) | (1 << 13) | (nextPhase << 14));
                InterlockedMax(_ChunkAwakeFlags[currentTicket], 2);
                return; 
            }
            
            WakeUpNeighbors(globalPos, nextPhase); // THE VACUUM SLAP!
            WriteFluidData(currentTicket, localPos, 0);
            WriteFluidData(targetTicket, targetLocal, (myMat & 0x3F) | (myDensity << 6) | (testDir << 10) | (1 << 13) | (nextPhase << 14));
            InterlockedMax(_ChunkAwakeFlags[targetTicket], 2); 
            return;
        }
    }

    // --- RULE 3: HORIZONTAL FLOW (Chaos Steering) ---
    // Query the block directly below us
    uint downTicket; int3 downLocal; uint underMat;
    CheckSpace(globalPos + int3(0, -1, 0), downTicket, downLocal, underMat);

    // Only move horizontally if resting on a liquid!
    if (underMat > 0) {
        int3 forwardPos = globalPos + GetDir(myDir);
        if (CheckSpace(forwardPos, targetTicket, targetLocal, dummyMat)) {
            if (targetTicket == 0xFFFFFFFF) {
                _TicketRequestsInbox.Append(forwardPos >> 5); // ASK CPU FOR MEMORY
                
                // THE NARCOLEPSY FIX: Wait for memory, but STAY AWAKE and FLIP PHASE!
                WriteFluidData(currentTicket, localPos, (myMat & 0x3F) | (myDensity << 6) | (myDir << 10) | (1 << 13) | (nextPhase << 14));
                InterlockedMax(_ChunkAwakeFlags[currentTicket], 2);
                return; 
            }
            
            WakeUpVoxel(globalPos + int3(0, 1, 0), nextPhase); // THE VACUUM SLAP!
            WriteFluidData(currentTicket, localPos, 0);
            WriteFluidData(targetTicket, targetLocal, (myMat & 0x3F) | (myDensity << 6) | (myDir << 10) | (1 << 13) | (nextPhase << 14));
            InterlockedMax(_ChunkAwakeFlags[targetTicket], 2); 
            return;
        } else {
            // Change direction if blocked!
            uint randomSteer = HashVoxel((uint3)globalPos + (uint3)_Tick);
            myDir = randomSteer & 0x07;
            WriteFluidData(currentTicket, localPos, (myMat & 0x3F) | (myDensity << 6) | (myDir << 10) | (1 << 13) | (nextPhase << 14));
            return;
        }
    }

    // RULE 4: GO TO SLEEP
    WriteFluidData(currentTicket, localPos, (myMat & 0x3F) | (myDensity << 6) | (myDir << 10) | (0 << 13) | (currentPhase << 14));
}
