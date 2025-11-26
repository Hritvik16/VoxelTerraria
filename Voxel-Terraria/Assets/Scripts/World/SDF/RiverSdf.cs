using Unity.Mathematics;

public static class RiverSdf
{
    /// <summary>
    /// Evaluates a 3D river SDF.
    /// The river is a tube that descends from startHeight to endHeight.
    /// It meanders in XZ space based on noise.
    /// </summary>
    public static float Evaluate(float3 p, in Feature f)
    {
        // Unpack data
        float2 centerXZ = new float2(f.data0.x, f.data0.y);
        float radius    = f.data0.z; // Length of influence
        
        float width     = f.data1.x;
        float depth     = f.data1.y;
        float meanderFreq = f.data1.z;
        
        float meanderAmp = f.data2.x;
        float startHeight = f.data2.y;
        float endHeight   = f.data2.z;
        
        float seed = f.data3.x;

        // 1. Check bounds optimization
        // If we are too far from the river's general area, return early.
        // This is a rough check; the river wanders, so we need a buffer.
        float distToCenter = math.length(p.xz - centerXZ);
        if (distToCenter > radius + meanderAmp * 2f)
            return 9999f;
            
        // 2. Determine River Path
        // We define the river path parametrically or implicitly.
        // A simple way for a descending river is to map Y to a "progress" value.
        
        // Normalize Y between start and end height
        // t=0 at start (top), t=1 at end (bottom)
        float t = math.unlerp(startHeight, endHeight, p.y); 
        
        // If we are above start or below end, we fade out or cap it.
        // For now, let's just clamp it but add a distance penalty if out of vertical bounds.
        float verticalDist = 0f;
        if (p.y > startHeight) verticalDist = p.y - startHeight;
        if (p.y < endHeight)   verticalDist = endHeight - p.y;
        
        // 3. Calculate River Center at this Y
        // The river flows along Z (relative to center) but meanders in X.
        // Or better: It flows radially outward? Or in a specific direction?
        // Let's assume it flows generally along the Z axis for now, or use noise for both X and Z.
        
        // Let's make it flow from centerXZ outwards in a random direction?
        // Or just a linear path modified by noise.
        // Let's assume a linear path along Z for simplicity, modified by noise.
        
        // Calculate the "ideal" position on the river curve at this height.
        // We map height to a position along the river.
        // Let's say the river length is 'radius'.
        float progress = t * radius; // 0 to radius
        
        // Base path: Straight line along Z (relative to center)
        float2 pathPos = centerXZ + new float2(0, progress - radius * 0.5f);
        
        // Add meandering (Noise based on progress)
        float meanderX = NoiseUtils.Noise2D(new float2(progress, 0) * meanderFreq + seed, 1f, 1f) * meanderAmp;
        pathPos.x += meanderX;
        
        // 4. Distance to River Curve
        // We are at p.xz. The river center is at pathPos.
        float distToCurve = math.length(p.xz - pathPos);
        
        // 5. Cross-Section Shape (U-Shape or Tube)
        // We want a channel.
        // SDF = distance to the tube surface.
        // Inside the tube = negative.
        
        // Horizontal distance component
        float dH = distToCurve - width * 0.5f;
        
        // Vertical component is handled by the "tube" logic.
        // Actually, for a river *bed*, we want to carve downwards.
        // So we want anything *above* the river bed to be air? No, we want to remove the ground.
        // So the shape is a cylinder extending upwards?
        // No, we want to carve a U-shape valley.
        
        // Let's define the SDF of the "Water Body" first (the tube).
        // Then the "Carve Shape" is usually the water body + some banks.
        
        // Let's define a simple capsule/tube SDF.
        // But we need it to be infinite upwards? 
        // If we subtract a tube, we get a tunnel.
        // If we subtract a vertical capsule, we get a hole.
        // We want a valley. A valley is a tube that extends infinitely upwards.
        
        // So: SDF = distToCurve - width.
        // This creates a vertical infinite wall.
        // We need to limit the depth.
        
        // Let's say the river bed is at 'p.y'.
        // But we are calculating SDF at 'p'.
        // The river bed height at this XZ is... tricky because XZ determines Y in the heightmap approach.
        // But here we are fully 3D.
        
        // Let's stick to the 3D Tube approach.
        // We are carving a tunnel. If the tunnel is near the surface, it becomes a valley.
        // If it's deep underground, it's a cave.
        // This is exactly what we want for "feature agnostic".
        
        // Tube Radius
        float tubeRadius = width * 0.5f;
        
        // Distance to the 3D line segment is hard with noise.
        // Approximation: We computed 'pathPos' which is the river center at this Y.
        // This assumes the river is vertical? No, that's wrong.
        
        // Better approach:
        // The river is a curve in 3D space: (x(t), y(t), z(t)).
        // We need distance from p to this curve.
        // Since the curve is mostly horizontal (flowing downhill), we can approximate.
        
        // Unpack rotation (sin, cos)
        float sinRot = f.data3.y;
        float cosRot = f.data3.z;

        // Transform p into local river space (aligned with Z)
        // 1. Translate to center
        float2 relP = p.xz - centerXZ;
        
        // 2. Rotate
        // x' = x*cos - y*sin
        // y' = x*sin + y*cos
        float2 rotatedP;
        rotatedP.x = relP.x * cosRot - relP.y * sinRot;
        rotatedP.y = relP.x * sinRot + relP.y * cosRot;
        
        // 3. Translate back (optional, but easier to just work in local space)
        // Actually, let's work in local space where center is (0,0).
        // River flows along Z (rotatedP.y) from -length/2 to +length/2.
        
        // OPTIMIZATION: Check local bounds before doing expensive noise/curve math
        // Local Z bounds: [-length/2, +length/2]
        // Local X bounds: [-meanderAmp, +meanderAmp] (roughly)
        // We add a buffer for width and blending.
        float halfLength = radius * 0.5f;
        float buffer = width * 2f + meanderAmp; // generous buffer
        
        if (math.abs(rotatedP.y) > halfLength + width * 2f ||
            math.abs(rotatedP.x) > buffer)
        {
            return 100f; // Return safe positive value (air)
        }

        // Update logic to use rotatedP instead of p.xz
        
        // We need to find 't' closest to 'p'.
        // Since Z=t (roughly), we can use rotatedP.y as a guess for t.
        // River flows from Z = -length/2 to Z = +length/2
        float localZ = rotatedP.y; // -halfLength to +halfLength
        float t_est = localZ + halfLength; // 0 to length (radius)
        
        // Refine t? For now, simple projection is okay for gentle slopes.
        
        // Calculate curve position at t_est
        float curveY = math.lerp(startHeight, endHeight, math.saturate(t_est / radius));
        // Curve X deviation in local space
        float curveX_local = NoiseUtils.Noise2D(new float2(t_est, 0) * meanderFreq + seed, 1f, 1f) * meanderAmp;
        // Curve Z in local space is just localZ (approx)
        float curveZ_local = localZ;
        
        // Distance from p (local) to curve point (local)
        // p is (rotatedP.x, p.y, rotatedP.y)
        // curve is (curveX_local, curveY, curveZ_local)
        float3 p_local = new float3(rotatedP.x, p.y, rotatedP.y);
        float3 curvePos_local = new float3(curveX_local, curveY, curveZ_local);
        
        float distToPoint = math.length(p_local - curvePos_local);
        
        // SDF: sphere subtraction
        // sdf = distToPoint - width
        
        // To make it a valley (open top), we can modify the distance metric.
        // But a tunnel is safer for now.
        // Let's make it a flattened tube (wider than tall).
        
        float dHorizontal = math.length(p_local.xz - new float2(curveX_local, curveZ_local));
        float dVertical   = p.y - curveY;
        
        // Ellipsoid metric for flattened tube
        // d = length( vec2(dH, dV*ratio) ) - radius
        float verticalScale = width / depth; // If depth < width, we scale Y up so it counts more (making the shape flatter)
        
        float d = math.length(new float2(dHorizontal, dVertical * verticalScale)) - width * 0.5f;
        
        return d;
    }
}
