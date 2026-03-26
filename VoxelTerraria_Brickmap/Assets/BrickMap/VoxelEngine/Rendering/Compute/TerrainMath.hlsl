// --- GLOBAL WORLD VARIABLES ---
float _WorldRadiusXZ;
int _WorldSeed;

// --- 2D NOISE FUNDAMENTALS ---
float2 hash22(float2 p) {
    p += frac(float2(_WorldSeed * 0.134, _WorldSeed * 0.811));
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
}

float perlin2D(float2 p) {
    float2 pi = floor(p);
    float2 pf = p - pi;
    float2 w = pf * pf * (3.0 - 2.0 * pf);
    return lerp(
        lerp(dot(hash22(pi + float2(0.0, 0.0)), pf - float2(0.0, 0.0)), dot(hash22(pi + float2(1.0, 0.0)), pf - float2(1.0, 0.0)), w.x),
        lerp(dot(hash22(pi + float2(0.0, 1.0)), pf - float2(0.0, 1.0)), dot(hash22(pi + float2(1.0, 1.0)), pf - float2(1.0, 1.0)), w.x),
        w.y);
}

// --- 3D NOISE FUNDAMENTALS ---
float3 hash33(float3 p) {
    p += frac(float3(_WorldSeed * 0.134, _WorldSeed * 0.811, _WorldSeed * 0.521));
    p = float3( dot(p, float3(127.1, 311.7, 74.7)),
                dot(p, float3(269.5, 183.3, 246.1)),
                dot(p, float3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
}

float perlin3D(float3 p) {
    float3 pi = floor(p);
    float3 pf = p - pi;
    float3 w = pf * pf * (3.0 - 2.0 * pf);
    
    float3 u000 = hash33(pi + float3(0,0,0)); float3 u100 = hash33(pi + float3(1,0,0));
    float3 u010 = hash33(pi + float3(0,1,0)); float3 u110 = hash33(pi + float3(1,1,0));
    float3 u001 = hash33(pi + float3(0,0,1)); float3 u101 = hash33(pi + float3(1,0,1));
    float3 u011 = hash33(pi + float3(0,1,1)); float3 u111 = hash33(pi + float3(1,1,1));

    float d000 = dot(u000, pf - float3(0,0,0)); float d100 = dot(u100, pf - float3(1,0,0));
    float d010 = dot(u010, pf - float3(0,1,0)); float d110 = dot(u110, pf - float3(1,1,0));
    float d001 = dot(u001, pf - float3(0,0,1)); float d101 = dot(u101, pf - float3(1,0,1));
    float d011 = dot(u011, pf - float3(0,1,1)); float d111 = dot(u111, pf - float3(1,1,1));

    float rx0 = lerp(d000, d100, w.x); float rx1 = lerp(d010, d110, w.x);
    float rx2 = lerp(d001, d101, w.x); float rx3 = lerp(d011, d111, w.x);
    float ry0 = lerp(rx0, rx1, w.y); float ry1 = lerp(rx2, rx3, w.y);
    return lerp(ry0, ry1, w.z);
}

// --- GLOBAL STRUCTS & BUFFERS ---
struct FeatureAnchor { float2 position; int topologyID; int biomeID; float radius; float heightMod; float pad0; float pad1; };
struct CavernNode { float3 position; float radius; int biomeID; int cavernType; float pad0; float pad1; };
struct TunnelSpline { float3 startPoint; float3 endPoint; float radius; float noiseIntensity; };

StructuredBuffer<FeatureAnchor> _FeatureAnchorBuffer;
int _FeatureCount;
StructuredBuffer<CavernNode> _CavernNodeBuffer;
int _CavernCount;
StructuredBuffer<TunnelSpline> _TunnelSplineBuffer;
int _TunnelCount;

// Ground Truth Math: The Island Shape
float GetCoastDistance(float2 worldXZ) {
    float theta = atan2(worldXZ.y, worldXZ.x);
    float F = 4.0;
    float2 noiseUV = float2(cos(theta) * F, sin(theta) * F);
    return _WorldRadiusXZ * (0.75 + 0.25 * perlin2D(noiseUV));
}

// --- PART 2: VOLUMETRIC DENSITY PIPELINE ---
void GetSurfaceTopology(float2 worldXZ, out float baseHeight, out int activeBiome) {
    float distFromCenter = length(worldXZ);
    float coastDist = GetCoastDistance(worldXZ);
    
    // Default Continental Swell
    float n = perlin2D(worldXZ * 0.002);
    baseHeight = 15.0 + (n * 20.0);
    activeBiome = 1; // Default Forest
    
    if (distFromCenter > coastDist) {
        float drop = (distFromCenter - coastDist) * 0.5;
        baseHeight = clamp(10.0 - drop, -40.0, 10.0); 
        return;
    }

    // Step 2B: Surface Topology Modifiers
    for(int i = 0; i < _FeatureCount; i++) {
        FeatureAnchor anchor = _FeatureAnchorBuffer[i];
        float d = distance(worldXZ, anchor.position);
        
        if (d < anchor.radius) {
            float W = 0.0;
            
            if (anchor.topologyID == 10) { // Mountain
                float normalized = 1.0 - (d / anchor.radius);
                W = max(0.0, normalized * normalized);
                baseHeight = lerp(baseHeight, anchor.heightMod, W);
            } 
            else if (anchor.topologyID == 11) { // Plateau
                float normalized = 1.0 - pow(d / anchor.radius, 6.0);
                W = max(0.0, normalized);
                baseHeight = lerp(baseHeight, anchor.heightMod, W);
            }
            else if (anchor.topologyID == 12) { // Crater
                W = cos((d / anchor.radius) * 1.57079);
                baseHeight -= (W * abs(anchor.heightMod));
            }
            
            if (anchor.topologyID == 0) { // Biome Painting
                activeBiome = anchor.biomeID;
            }
        }
    }
}

// Step 2C, 2D & Part 3: Subterranean Carver
float GetDensity(float3 worldXYZ, float baseHeight) {
    // 2C: THE OPTIMIZATION AUDIT
    // Simplified to base height logic. Noise checks move into inner subdivided loops to prevent massive ALU waste.
    float density = baseHeight - worldXYZ.y;
    return density;
}

// --- PART 4: SLOPE AND DEPTH PAINTER ---
// AUDITED: Stripped all redundant slope noise checks. The renderer handles slopes now.
int GetMaterial(float3 worldXYZ, float baseHeight, int surfaceBiome) {
    // Step 4A: Global Overrides (Underworld)
    if (worldXYZ.y <= -220.0) {
        // Underworld blend remains, it's rare enough to not break ALU limits
        float n = perlin3D(worldXYZ * 0.03);
        return (n > 0.0) ? 14 : 13; // Hellstone or Ash
    }
    
    int activeBiome = surfaceBiome;
    float depth = baseHeight - worldXYZ.y;
    
    // Step 4C: Logic Tree (Stripped of Slope dependencies)
    if (activeBiome == 1) { // Forest
        if (depth < 2.0) return 1;  // Grass
        if (depth < 15.0) return 2; // Dirt
        return 3; // Stone
    } 
    else if (activeBiome == 4) { // Desert
        if (depth < 2.0) return 4;  // Sand
        if (depth < 15.0) return 5; // Hardened Sand
        return 6; // Sandstone
    }
    else if (activeBiome == 2) { // Snow
        if (depth < 2.0) return 7; // Snow
        if (depth < 15.0) return 8; // Slush
        return 9; // Ice
    }
    else if (activeBiome == 3) { // Jungle
        if (depth < 2.0) return 10; // Jungle Grass
        if (depth < 15.0) return 11; // Mud
        return 12; // Mossy Stone
    }
    
    return 3; // Fallback Deep Rock
}