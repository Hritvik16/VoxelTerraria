// --- GLOBAL WORLD VARIABLES ---
float _WorldRadiusXZ;
int _WorldSeed;

// --- NOISE GENERATION ---
float2 hash22(float2 p) {
    p += float2(_WorldSeed * 1.34, _WorldSeed * 0.81);
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
}

float perlinNoise(float2 p) {
    float2 pi = floor(p);
    float2 pf = p - pi;
    float2 w = pf * pf * (3.0 - 2.0 * pf);
    return lerp(
        lerp(dot(hash22(pi + float2(0.0, 0.0)), pf - float2(0.0, 0.0)), dot(hash22(pi + float2(1.0, 0.0)), pf - float2(1.0, 0.0)), w.x),
        lerp(dot(hash22(pi + float2(0.0, 1.0)), pf - float2(0.0, 1.0)), dot(hash22(pi + float2(1.0, 1.0)), pf - float2(1.0, 1.0)), w.x),
        w.y);
}

float fbm(float2 p) {
    float total = 0.0; float amplitude = 1.0; float frequency = 1.0;
    for (int i = 0; i < 4; i++) {
        total += perlinNoise(p * frequency) * amplitude;
        frequency *= 2.0; amplitude *= 0.4;
    }
    return total;
}

// --- TERRAIN SHAPE FUNCTIONS ---

// 1. Coastline / Island Bounds
bool IsInsideIsland(float2 worldXZ) {
    float distFromCenter = length(worldXZ);
    float coastlineNoise = (fbm(worldXZ * 0.002) * 2.0 - 1.0) * (_WorldRadiusXZ * 0.3);
    float finalDist = distFromCenter + coastlineNoise;
    return finalDist <= _WorldRadiusXZ;
}