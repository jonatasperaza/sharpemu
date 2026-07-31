// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Core.Cpu.Native;
using SharpEmu.Core.Loader;
using SharpEmu.Core.Memory;
using Xunit;

namespace SharpEmu.Libs.Tests.Cpu;

public sealed class TlsScanRangeTests
{
    [Fact]
    public void UsesEntireExecutableRegionContainingEntryPoint()
    {
        const ulong imageBase = 0x0000_0008_0000_0000;
        const ulong codeSize = 0x0B7B_44EC;
        VirtualMemoryRegion[] regions =
        [
            new(
                imageBase,
                codeSize,
                fileOffset: 0,
                fileSize: codeSize,
                ProgramHeaderFlags.Read | ProgramHeaderFlags.Execute),
        ];

        var found = DirectExecutionBackend.TryGetExecutableScanRange(
            regions,
            imageBase + 0x80,
            out var start,
            out var end);

        Assert.True(found);
        Assert.Equal(imageBase, start);
        Assert.Equal(imageBase + codeSize, end);
        Assert.True(end > imageBase + 0x0800_0000);
    }

    [Fact]
    public void IgnoresNonExecutableRegionContainingEntryPoint()
    {
        VirtualMemoryRegion[] regions =
        [
            new(
                virtualAddress: 0x1000,
                memorySize: 0x1000,
                fileOffset: 0,
                fileSize: 0x1000,
                ProgramHeaderFlags.Read | ProgramHeaderFlags.Write),
        ];

        Assert.False(DirectExecutionBackend.TryGetExecutableScanRange(
            regions,
            entryPoint: 0x1080,
            out _,
            out _));
    }
}
