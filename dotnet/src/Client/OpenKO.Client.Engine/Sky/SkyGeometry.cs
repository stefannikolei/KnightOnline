using Microsoft.Xna.Framework;

namespace OpenKO.Client.Engine.Sky;

/// <summary>A vertex-colored, textured cloud vertex (__VertexXyzColorT2).</summary>
public readonly record struct SkyCloudVertex(Vector3 Position, uint Color, Vector2 Uv, Vector2 Uv2);

/// <summary>A vertex-colored sky-fan vertex (__VertexXyzColor / FVF_XYZCOLOR).</summary>
public readonly record struct SkyFanVertex(Vector3 Position, uint Color);

/// <summary>
/// Pure port of the geometry set up in <c>CN3Sky::Init</c> and
/// <c>CN3Cloud::Init</c> (Client/N3Base): the two horizon-glow fans (front +
/// bottom) and the two-tier cloud dome. Colours come from the sky's fog colour
/// exactly as <c>CN3Sky::Tick</c> applies it. The device layer draws the fans
/// camera-centred with Z and fog disabled, alpha-blended.
/// </summary>
public static class SkyGeometry
{
    // CN3Sky::Init constants.
    private const float FanWidth = 3.5f;
    private const float FanTopY = 0.5f;
    private const float FanBottomY = 0.1f;
    private const float FanDistance = 1.5f;
    private const float FanBottomOffset = -5.0f;

    public const int CloudVertexCount = 8;

    /// <summary>The default day fog colour (CN3Sky constructor: 0xFFB5C6DE).</summary>
    public const uint DefaultFogColor = 0xFFB5C6DE;

    /// <summary>CN3Cloud::Render index list — 10 triangles over the 8 vertices.</summary>
    public static readonly short[] CloudIndices =
        [0, 1, 4, 1, 2, 5, 2, 3, 6, 3, 0, 7, 5, 4, 1, 6, 5, 2, 7, 6, 3, 4, 7, 0, 4, 5, 7, 5, 6, 7];

    /// <summary>
    /// The four "front" fan vertices (horizon glow). The top two fade to
    /// transparent (alpha 0), the bottom two keep the fog alpha — matching
    /// <c>CN3Sky::Init</c> + <c>Tick</c> (RGB replaced by the fog colour,
    /// alpha preserved).
    /// </summary>
    public static SkyFanVertex[] BuildFrontFan(uint fogColor)
    {
        uint rgb = fogColor & 0x00FFFFFFu;
        uint full = fogColor; // alpha kept from the fog colour (0xFF by default)
        return
        [
            new SkyFanVertex(new Vector3(FanWidth, FanTopY, FanDistance), rgb),
            new SkyFanVertex(new Vector3(FanWidth, FanBottomY, FanDistance), full),
            new SkyFanVertex(new Vector3(-FanWidth, FanBottomY, FanDistance), full),
            new SkyFanVertex(new Vector3(-FanWidth, FanTopY, FanDistance), rgb),
        ];
    }

    /// <summary>The four "bottom" fan vertices (pure fog below the glow).</summary>
    public static SkyFanVertex[] BuildBottomFan(uint fogColor)
        =>
        [
            new SkyFanVertex(new Vector3(FanWidth, FanBottomY, FanDistance), fogColor),
            new SkyFanVertex(new Vector3(FanWidth, FanBottomOffset, FanDistance), fogColor),
            new SkyFanVertex(new Vector3(-FanWidth, FanBottomOffset, FanDistance), fogColor),
            new SkyFanVertex(new Vector3(-FanWidth, FanBottomY, FanDistance), fogColor),
        ];

    /// <summary>
    /// The two-tier cloud dome (CN3Cloud::Init): a large low square (alpha 0)
    /// and a smaller raised square (opaque), giving the frustum silhouette the
    /// index list stitches. Both UV sets are seeded identically; the runtime
    /// scrolls them independently (handled by the renderer, not here).
    /// </summary>
    public static SkyCloudVertex[] BuildCloudDome()
    {
        const float sqrt3Inv = 1.0f / 1.7320508f; // 1/sqrt(3)
        const float smallLength = 8.0f;
        const float bigHeight = 5.0f;
        // fSmallHeight uses the pre-reset big length (16), then big length -> 24.
        float smallHeight = bigHeight + ((16.0f - smallLength) * sqrt3Inv);
        const float bigLength = 24.0f;

        const uint bigColor = 0x00FFFFFFu;   // transparent at the horizon skirt
        const uint smallColor = 0xFFFFFFFFu; // opaque at the top

        const float uvRight = 4.0f;
        const float uvBottom = 4.0f;
        float offU = (1.0f - (smallLength / bigLength)) * (uvRight / 2.0f);
        float offV = (1.0f - (smallLength / bigLength)) * (uvBottom / 2.0f);

        return
        [
            Cloud(-bigLength, bigHeight, -bigLength, bigColor, 0f, 0f),
            Cloud(bigLength, bigHeight, -bigLength, bigColor, uvRight, 0f),
            Cloud(bigLength, bigHeight, bigLength, bigColor, uvRight, uvBottom),
            Cloud(-bigLength, bigHeight, bigLength, bigColor, 0f, uvBottom),
            Cloud(-smallLength, smallHeight, -smallLength, smallColor, offU, offV),
            Cloud(smallLength, smallHeight, -smallLength, smallColor, uvRight - offU, offV),
            Cloud(smallLength, smallHeight, smallLength, smallColor, uvRight - offU, uvBottom - offV),
            Cloud(-smallLength, smallHeight, smallLength, smallColor, offU, uvBottom - offV),
        ];
    }

    private static SkyCloudVertex Cloud(float x, float y, float z, uint color, float u, float v)
        => new(new Vector3(x, y, z), color, new Vector2(u, v), new Vector2(u, v));

    /// <summary>
    /// The camera-centred world rotation for the sky (CN3Sky::Render /
    /// CN3SkyMng::Render): the view translation is zeroed and the sky yaws to
    /// face the camera direction. Mirrors the BoardY-style branch exactly.
    /// </summary>
    public static Matrix CameraYaw(System.Numerics.Vector3 eye, System.Numerics.Vector3 at)
    {
        System.Numerics.Vector3 dir = eye - at;
        if (dir.X == 0f)
            return Matrix.Identity;
        float yaw = dir.X > 0f
            ? -MathF.Atan(dir.Z / dir.X) - (MathF.PI * 0.5f)
            : -MathF.Atan(dir.Z / dir.X) + (MathF.PI * 0.5f);
        return Matrix.CreateRotationY(yaw);
    }
}
