float hash(float3 p) {
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

Ray CreateCameraRay(float2 uv) {
    float3 origin = mul(_CameraToWorld, float4(0.0f, 0.0f, 0.0f, 1.0f)).xyz;
    float3 direction = mul(_CameraInverseProjection, float4(uv, 0.0f, 1.0f)).xyz;
    direction = mul(_CameraToWorld, float4(direction, 0.0f)).xyz;
    Ray ray; ray.origin = origin; ray.direction = normalize(direction);
    return ray;
}

