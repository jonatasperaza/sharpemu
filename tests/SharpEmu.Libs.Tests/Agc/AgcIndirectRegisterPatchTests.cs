// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class AgcIndirectRegisterPatchTests
{
    [Theory]
    [InlineData(0x10000000u, true, false)]
    [InlineData(0x1000000Fu, true, false)]
    [InlineData(0x00000318u, true, true)]
    [InlineData(0x10000000u, false, true)]
    [InlineData(0x000000C8u, false, true)]
    [InlineData(0x0000025Bu, false, true)]
    public void DecodeIndirectRegisterOffset_SkipsOnlyUnresolvedVirtualContextRegisters(
        uint encodedRegister,
        bool contextRegister,
        bool expectedResolved)
    {
        Assert.Equal(
            expectedResolved,
            AgcExports.TryDecodeIndirectRegisterOffset(
                encodedRegister,
                contextRegister,
                out var registerOffset));
        Assert.Equal(encodedRegister, registerOffset);
    }

    [Fact]
    public void ResolveVirtualContextRegisterGroup_MaterializesColorTargetSlot()
    {
        var encoded = Enumerable.Repeat(0x1000000Fu, 16).ToArray();
        var values = new uint[16];
        var resolved = new uint[16];

        Assert.True(AgcExports.TryResolveVirtualContextRegisterGroup(encoded, values, resolved));
        Assert.Equal(0x327u, resolved[0]);
        Assert.Equal(0x334u, resolved[9]);
        Assert.Equal(0x391u, resolved[10]);
        Assert.Equal(0x3B9u, resolved[15]);
    }

    [Fact]
    public void ResolveVirtualContextRegisterGroup_MaterializesColorTargetZero()
    {
        var encoded = Enumerable.Repeat(0x10000000u, 16).ToArray();
        var values = new uint[16];
        values[15] = 0x4506C000u;
        var resolved = new uint[16];

        Assert.True(AgcExports.TryResolveVirtualContextRegisterGroup(encoded, values, resolved));
        Assert.Equal(0x318u, resolved[0]);
        Assert.Equal(0x3B8u, resolved[15]);
    }

    [Fact]
    public void ResolveVirtualContextRegisterGroup_MaterializesDepthTarget()
    {
        var encoded = Enumerable.Repeat(0x10000000u, 16).ToArray();
        var values = new uint[16];
        var resolved = new uint[16];

        Assert.True(AgcExports.TryResolveVirtualContextRegisterGroup(encoded, values, resolved));
        Assert.Equal(0x010u, resolved[0]);
        Assert.Equal(0x002u, resolved[11]);
        Assert.Equal(0x00Au, resolved[15]);
    }

    [Fact]
    public void ResolveVirtualContextRegisterGroup_MaterializesAllInterpolantsByPosition()
    {
        var encoded = Enumerable.Range(0, 19)
            .Select(index => 0x10000000u + (uint)(index & 0xFu))
            .ToArray();
        var values = new uint[19];
        var resolved = new uint[19];

        Assert.True(AgcExports.TryResolveVirtualContextRegisterGroup(encoded, values, resolved));
        Assert.Equal(0x191u, resolved[0]);
        Assert.Equal(0x1A0u, resolved[15]);
        Assert.Equal(0x1A3u, resolved[18]);
    }

    [Theory]
    [InlineData(0x45010501u, 0x0000000Fu, true)]
    [InlineData(0x00000000u, 0x0000000Fu, true)]
    [InlineData(0x45010501u, 0x00000000u, false)]
    public void BlendTargetMaskPair_RecognizesCapturedState(
        uint blendControl,
        uint targetMask,
        bool expected)
    {
        Assert.Equal(
            expected,
            AgcExports.IsBlendTargetMaskPair(blendControl, targetMask));
    }
}
