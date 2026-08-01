// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Gpu;
using Xunit;

namespace SharpEmu.Libs.Tests.Gpu;

public sealed class GuestMrtPassPlannerTests
{
    [Fact]
    public void EqualExtentTargetsRemainInOnePass()
    {
        GuestRenderTarget[] targets =
        [
            new(0x1000, 1920, 1080, 10, 0),
            new(0x2000, 1920, 1080, 11, 7),
        ];

        Assert.False(GuestMrtPassPlanner.RequiresSeparatePasses(targets));
    }

    [Fact]
    public void IndependentExtentTargetsUseSeparatePasses()
    {
        GuestRenderTarget[] targets =
        [
            new(0x1000, 120, 67, 2, 7),
            new(0x2000, 1920, 1080, 6, 7),
        ];

        Assert.True(GuestMrtPassPlanner.RequiresSeparatePasses(targets));
    }

    [Fact]
    public void BorrowedSnapshotsKeepBackingStorageWithoutReturningIt()
    {
        var data = new byte[64];
        GuestMemoryBuffer[] memory = [new(0x1000, data, data.Length, Pooled: true)];
        GuestVertexBuffer[] vertices =
        [
            new(0, 4, 10, 0, 0x2000, 16, 0, data, data.Length, Pooled: true),
        ];
        var indices = new GuestIndexBuffer(data, data.Length, Is32Bit: false, Pooled: true);

        var borrowedMemory = GuestMrtPassPlanner.BorrowMemoryBuffers(memory);
        var borrowedVertices = GuestMrtPassPlanner.BorrowVertexBuffers(vertices);
        var borrowedIndices = GuestMrtPassPlanner.BorrowIndexBuffer(indices);

        Assert.Same(data, borrowedMemory[0].Data);
        Assert.Same(data, borrowedVertices[0].Data);
        Assert.Same(data, borrowedIndices!.Data);
        Assert.False(borrowedMemory[0].Pooled);
        Assert.False(borrowedVertices[0].Pooled);
        Assert.False(borrowedIndices.Pooled);
        Assert.True(memory[0].Pooled);
        Assert.True(vertices[0].Pooled);
        Assert.True(indices.Pooled);
    }

    [Fact]
    public void OnlyFinalNormalPassWritesDepth()
    {
        var depth = new GuestDepthState(
            TestEnable: true,
            WriteEnable: true,
            CompareOp: 1,
            ClearEnable: false);

        var first = GuestMrtPassPlanner.GetDepthStateForPass(depth, 0, 2);
        var final = GuestMrtPassPlanner.GetDepthStateForPass(depth, 1, 2);

        Assert.True(first.TestEnable);
        Assert.False(first.WriteEnable);
        Assert.Equal(depth, final);
    }

    [Fact]
    public void OnlyFirstClearPassTouchesDepth()
    {
        var depth = new GuestDepthState(
            TestEnable: true,
            WriteEnable: true,
            CompareOp: 1,
            ClearEnable: true);

        var first = GuestMrtPassPlanner.GetDepthStateForPass(depth, 0, 2);
        var final = GuestMrtPassPlanner.GetDepthStateForPass(depth, 1, 2);

        Assert.Equal(depth, first);
        Assert.Equal(GuestDepthState.Default, final);
    }
}
