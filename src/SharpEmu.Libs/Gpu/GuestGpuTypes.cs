// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Agc;

namespace SharpEmu.Libs.Gpu;

// The types that cross the guest-GPU backend seam. Every field is either a neutral
// primitive (dimensions, counts, host pixel bytes) or a raw guest/AGC value (guest
// addresses, guest format and number-type codes, guest register bitfields). Host
// graphics-API values must never appear here: each backend owns the guest -> native
// translation for its API.

/// <summary>A guest texture referenced by a draw or dispatch. Format/NumberType/
/// TileMode/DstSelect/Type are raw guest descriptor codes. Depth is the
/// normalized volume depth (one for non-3D resources).</summary>
internal sealed record GuestDrawTexture(
    ulong Address,
    uint Width,
    uint Height,
    uint Format,
    uint NumberType,
    byte[] RgbaPixels,
    bool IsFallback,
    bool IsStorage,
    uint MipLevels = 1,
    uint MipLevel = 0,
    uint BaseMipLevel = 0,
    uint ResourceMipLevels = 1,
    uint Pitch = 0,
    uint TileMode = 0,
    uint DstSelect = 0xFAC,
    GuestSampler Sampler = default,
    // Guest CPU write-tracker generation of the memory RgbaPixels was read
    // from; -1 when the range is untracked or the pixels were not read here.
    long WriteGeneration = -1,
    bool ArrayedView = false,
    uint ArrayLayers = 1,
    uint BaseArrayLayer = 0,
    uint Type = 9,
    uint Depth = 1,
    // GPU-detile opt-in (SHARPEMU_GPU_DETILE): when Detile is non-null the AGC
    // layer skipped the CPU deswizzle and shipped the raw TILED bytes here in
    // TiledSource; the Vulkan backend detiles them on the GPU. RgbaPixels is
    // empty in that case. Both are neutral (no host graphics-API values).
    byte[]? TiledSource = null,
    DetileParams? Detile = null);

/// <summary>Raw guest sampler descriptor dwords, copied verbatim from guest memory.</summary>
internal readonly record struct GuestSampler(
    uint Word0,
    uint Word1,
    uint Word2,
    uint Word3);

/// <summary>Identity of a texture's content in a backend texture cache, keyed
/// entirely on raw guest descriptor values; the AGC layer uses it to skip texel
/// copies for content the backend already holds.</summary>
internal readonly record struct TextureContentIdentity(
    ulong Address,
    uint Width,
    uint Height,
    uint Format,
    uint NumberType,
    uint DstSelect,
    uint TileMode,
    uint Pitch,
    GuestSampler Sampler,
    bool Arrayed = false,
    uint ArrayLayers = 1,
    uint BaseArrayLayer = 0,
    uint Type = 9,
    uint Depth = 1);

internal sealed record GuestMemoryBuffer(
    ulong BaseAddress,
    byte[] Data,
    int Length,
    bool Pooled,
    bool Writable = false,
    bool WriteBackToGuest = true);

/// <summary>DataFormat/NumberFormat are raw guest vertex-attribute codes.</summary>
internal sealed record GuestVertexBuffer(
    uint Location,
    uint ComponentCount,
    uint DataFormat,
    uint NumberFormat,
    ulong BaseAddress,
    uint Stride,
    uint OffsetBytes,
    byte[] Data,
    int Length,
    bool Pooled,
    bool PerInstance = false);

internal sealed record GuestIndexBuffer(
    byte[] Data,
    int Length,
    bool Is32Bit,
    bool Pooled);

internal readonly record struct GuestRect(
    int X,
    int Y,
    uint Width,
    uint Height);

internal readonly record struct GuestViewport(
    float X,
    float Y,
    float Width,
    float Height,
    float MinDepth,
    float MaxDepth);

internal readonly record struct GuestRasterState(
    bool CullFront,
    bool CullBack,
    bool FrontFaceClockwise,
    bool Wireframe)
{
    public static GuestRasterState Default { get; } = new(false, false, false, false);
}

// CompareOp uses the GCN DB_DEPTH_CONTROL ZFUNC encoding, which matches the
// Vulkan CompareOp ordering (0=Never through 7=Always).
internal readonly record struct GuestDepthState(
    bool TestEnable,
    bool WriteEnable,
    uint CompareOp,
    bool ClearEnable = false)
{
    public static GuestDepthState Default { get; } = new(false, false, 7, false);
}

/// <summary>Factors/funcs are raw guest CB_BLEND*_CONTROL register bitfields; the
/// defaults (1/0) are the guest ONE/ZERO codes.</summary>
internal readonly record struct GuestBlendState(
    bool Enable,
    uint ColorSrcFactor,
    uint ColorDstFactor,
    uint ColorFunc,
    uint AlphaSrcFactor,
    uint AlphaDstFactor,
    uint AlphaFunc,
    bool SeparateAlphaBlend,
    uint WriteMask)
{
    public static GuestBlendState Default { get; } = new(
        Enable: false,
        ColorSrcFactor: 1,
        ColorDstFactor: 0,
        ColorFunc: 0,
        AlphaSrcFactor: 1,
        AlphaDstFactor: 0,
        AlphaFunc: 0,
        SeparateAlphaBlend: false,
        WriteMask: 0xFu);
}

/// <summary>CB_BLEND_RED..ALPHA: the constant color referenced by the
/// CONSTANT_COLOR / CONSTANT_ALPHA blend factors. One constant serves every
/// render target of a draw; the hardware reset value is transparent black.</summary>
internal readonly record struct GuestBlendConstant(
    float Red,
    float Green,
    float Blue,
    float Alpha);

internal sealed record GuestRenderState(
    IReadOnlyList<GuestBlendState> Blends,
    GuestRect? Scissor,
    GuestViewport? Viewport,
    GuestRasterState Raster,
    GuestDepthState Depth,
    GuestBlendConstant BlendConstant = default)
{
    public static GuestRenderState Default { get; } = new(
        [GuestBlendState.Default],
        Scissor: null,
        Viewport: null,
        GuestRasterState.Default,
        GuestDepthState.Default);

    public GuestBlendState Blend =>
        Blends.Count == 0 ? GuestBlendState.Default : Blends[0];
}

/// <summary>Format/NumberType are raw guest render-target register codes.</summary>
internal sealed record GuestRenderTarget(
    ulong Address,
    uint Width,
    uint Height,
    uint Format,
    uint NumberType,
    uint MipLevels = 1);

/// <summary>
/// Plans host passes for guest MRT draws. Vulkan and Metal require every color
/// attachment in a render pass to share a usable render area, while Gen5 can
/// bind color targets with independent extents. Such draws are replayed once
/// per target, with a single fragment output in each host pass.
/// </summary>
internal static class GuestMrtPassPlanner
{
    public static bool RequiresSeparatePasses(IReadOnlyList<GuestRenderTarget> targets)
    {
        if (targets.Count < 2)
        {
            return false;
        }

        var width = targets[0].Width;
        var height = targets[0].Height;
        for (var index = 1; index < targets.Count; index++)
        {
            if (targets[index].Width != width || targets[index].Height != height)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Shared snapshots are consumed in queue order. Non-final passes borrow
    /// the backing arrays; only the final pass returns them to GuestDataPool.
    /// </summary>
    public static IReadOnlyList<GuestMemoryBuffer> BorrowMemoryBuffers(
        IReadOnlyList<GuestMemoryBuffer> buffers) =>
        buffers.Select(static buffer => buffer.Pooled
            ? buffer with { Pooled = false }
            : buffer).ToArray();

    public static IReadOnlyList<GuestVertexBuffer> BorrowVertexBuffers(
        IReadOnlyList<GuestVertexBuffer> buffers) =>
        buffers.Select(static buffer => buffer.Pooled
            ? buffer with { Pooled = false }
            : buffer).ToArray();

    public static GuestIndexBuffer? BorrowIndexBuffer(GuestIndexBuffer? buffer) =>
        buffer is { Pooled: true } ? buffer with { Pooled = false } : buffer;

    /// <summary>
    /// Preserve one guest depth operation while color output is replayed.
    /// Before a normal depth-writing final pass, earlier color passes test but
    /// do not update depth. A DB clear is performed by the first pass only;
    /// color passes themselves are depth-independent for that operation.
    /// </summary>
    public static GuestDepthState GetDepthStateForPass(
        GuestDepthState depth,
        int passIndex,
        int passCount)
    {
        if (passCount < 2)
        {
            return depth;
        }

        if (depth.ClearEnable)
        {
            return passIndex == 0
                ? depth
                : GuestDepthState.Default;
        }

        return depth.WriteEnable && passIndex < passCount - 1
            ? depth with { WriteEnable = false }
            : depth;
    }
}

/// <summary>Guest DB surface bound alongside a color render target.</summary>
internal sealed record GuestDepthTarget(
    ulong ReadAddress,
    ulong WriteAddress,
    uint Width,
    uint Height,
    uint GuestFormat,
    uint SwizzleMode,
    float ClearDepth,
    bool ReadOnly)
{
    public ulong Address => WriteAddress != 0 ? WriteAddress : ReadAddress;
}
