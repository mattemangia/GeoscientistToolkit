#version 450

// Input from vertex shader (normalized screen coords)
layout(location = 0) in vec3 in_TexCoord;

// Output color
layout(location = 0) out vec4 out_Color;

// --- UNIFORM BUFFERS & TEXTURES ---
layout(set = 0, binding = 0) uniform Constants
{
    mat4 InvViewProj;
    vec4 CameraPosition;
    vec4 VolumeSize;
    vec4 ThresholdParams;    // x:min, y:max, z:stepSize, w:showGrayscale
    vec4 SliceParams;        // xyz:slicePositions, w:showSlices
    vec4 RenderParams;       // x:colorMapIndex
    vec4 CutPlaneX;          // x:enabled, y:forward (1/-1), z:position
    vec4 CutPlaneY;
    vec4 CutPlaneZ;
    vec4 ClippingPlane;      // xyz:normal, w:distance
    vec4 ClippingParams;     // x:enabled, y:mirror
};

layout(set = 0, binding = 1) uniform sampler VolumeSampler;
layout(set = 0, binding = 2) uniform texture3D VolumeTexture; // Grayscale data
layout(set = 0, binding = 3) uniform texture3D LabelTexture;  // Label data for materials
layout(set = 0, binding = 4) uniform texture1D ColorMapTexture;
layout(set = 0, binding = 5) uniform texture1D MaterialParamsTexture; // x:visible, y:opacity
layout(set = 0, binding = 6) uniform texture1D MaterialColorsTexture;

// --- HELPER FUNCTIONS ---

// Ray-box intersection for finding entry/exit points of the volume
bool IntersectBox(vec3 rayOrigin, vec3 rayDir, vec3 boxMin, vec3 boxMax, out float tNear, out float tFar)
{
    vec3 invRayDir = 1.0 / rayDir;
    vec3 t1 = (boxMin - rayOrigin) * invRayDir;
    vec3 t2 = (boxMax - rayOrigin) * invRayDir;
    vec3 tMin = min(t1, t2);
    vec3 tMax = max(t1, t2);
    tNear = max(max(tMin.x, tMin.y), tMin.z);
    tFar = min(min(tMax.x, tMax.y), tMax.z);
    return tFar > tNear && tFar > 0.0;
}

// Checks if a point is clipped by the axis-aligned cutting planes
bool IsCutByPlanes(vec3 pos)
{
    if (CutPlaneX.x > 0.5 && (pos.x - CutPlaneX.z) * CutPlaneX.y > 0.0) return true;
    if (CutPlaneY.x > 0.5 && (pos.y - CutPlaneY.z) * CutPlaneY.y > 0.0) return true;
    if (CutPlaneZ.x > 0.5 && (pos.z - CutPlaneZ.z) * CutPlaneZ.y > 0.0) return true;
    return false;
}

// Checks if a point is clipped by the arbitrary rotating plane
bool IsClipped(vec3 pos)
{
    if (ClippingParams.x < 0.5) return false;
    float dist = dot(pos, ClippingPlane.xyz) - ClippingPlane.w;
    return ClippingParams.y > 0.5 ? dist < 0.0 : dist > 0.0;
}

// Samples the color map texture
vec4 ApplyColorMap(float intensity)
{
    float mapOffset = RenderParams.x * 256.0;
    float samplePos = (mapOffset + intensity * 255.0) / 1024.0;
    return texture(sampler1D(ColorMapTexture, VolumeSampler), samplePos);
}

// --- MAIN SHADER LOGIC ---
void main()
{
    // Reconstruct world space position and ray direction from screen coordinates
    vec4 worldPos = InvViewProj * vec4(in_TexCoord.xy * 2.0 - 1.0, 0.0, 1.0);
    worldPos /= worldPos.w;
    vec3 rayOrigin = CameraPosition.xyz;
    vec3 rayDir = normalize(worldPos.xyz - rayOrigin);

    // Find where the ray enters and exits the unit cube volume
    float tNear, tFar;
    if (!IntersectBox(rayOrigin, rayDir, vec3(0.0), vec3(1.0), tNear, tFar))
    {
        discard; // Ray doesn't hit the volume at all
    }
    tNear = max(tNear, 0.0); // Don't start behind the camera

    // --- RAY MARCHING LOOP ---
    vec4 accumulatedColor = vec4(0.0);
    float t = tNear;
    float step = ThresholdParams.z / length(VolumeSize.xyz); // Normalize step size

    for (int i = 0; i < 512; i++) // Max steps to prevent infinite loops
    {
        if (t > tFar || accumulatedColor.a > 0.99) break;

        vec3 currentPos = rayOrigin + t * rayDir;
        
        // Check bounds and clipping/cutting planes
        if (any(lessThan(currentPos, vec3(0.0))) || any(greaterThan(currentPos, vec3(1.0))) || 
            IsCutByPlanes(currentPos) || IsClipped(currentPos))
        {
            t += step;
            continue;
        }

        // --- DATA SAMPLING & COLORING ---
        vec4 sampledColor = vec4(0.0);
        int materialId = int(texture(sampler3D(LabelTexture, VolumeSampler), currentPos).r * 255.0 + 0.5);
        vec2 materialParams = texelFetch(sampler1D(MaterialParamsTexture, VolumeSampler), materialId, 0).xy;

        // Render material if it's visible
        if (materialId > 0 && materialParams.x > 0.5)
        {
            vec4 materialColor = texelFetch(sampler1D(MaterialColorsTexture, VolumeSampler), materialId, 0);
            materialColor.a *= materialParams.y; // Apply opacity
            sampledColor = materialColor;
        }
        // Otherwise, render grayscale if enabled and within threshold
        else if (ThresholdParams.w > 0.5)
        {
            float intensity = texture(sampler3D(VolumeTexture, VolumeSampler), currentPos).r;
            if (intensity >= ThresholdParams.x && intensity <= ThresholdParams.y)
            {
                float normIntensity = (intensity - ThresholdParams.x) / (ThresholdParams.y - ThresholdParams.x);
                if (RenderParams.x > 0) // Use color map
                {
                    sampledColor = ApplyColorMap(normIntensity);
                }
                else // Use grayscale
                {
                    sampledColor = vec4(vec3(normIntensity), normIntensity);
                }
                sampledColor.a *= 0.1; // Apply a base alpha for accumulation
            }
        }
        
        // --- COMPOSITING ---
        // Front-to-back compositing for correct transparency
        if (sampledColor.a > 0.0)
        {
            sampledColor.rgb *= sampledColor.a; // Pre-multiply alpha
            accumulatedColor += (1.0 - accumulatedColor.a) * sampledColor;
        }
        
        t += step;
    }

    // --- ORTHOGONAL SLICES ---
    // This is a separate check to draw opaque slices on top
    if (SliceParams.w > 0.5)
    {
        // Find intersection with the three slice planes
        vec3 invDir = 1.0 / rayDir;
        vec3 tSlice = (SliceParams.xyz - rayOrigin) * invDir;

        float tIntersect = min(tSlice.x, min(tSlice.y, tSlice.z));
        if (tIntersect > tNear && tIntersect < tFar)
        {
             vec3 intersectPos = rayOrigin + tIntersect * rayDir;
             if (!IsCutByPlanes(intersectPos) && !IsClipped(intersectPos))
             {
                 float intensity = texture(sampler3D(VolumeTexture, VolumeSampler), intersectPos).r;
                 out_Color = vec4(vec3(intensity), 1.0);
                 return;
             }
        }
    }

    out_Color = accumulatedColor;
}