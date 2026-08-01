// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

public sealed class KernelPthreadCompatExportsTests
{
    [Fact]
    public void PosixCondattrInit_WritesOpaqueHandle()
    {
        const ulong memoryBase = 0x1_0000_0000;
        const ulong attrAddress = memoryBase + 0x100;
        var memory = new FakeCpuMemory(memoryBase, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        context[CpuRegister.Rdi] = attrAddress;

        Assert.Equal(
            (int)OrbisGen2Result.ORBIS_GEN2_OK,
            KernelPthreadCompatExports.PosixPthreadCondattrInit(context));
        Assert.True(context.TryReadUInt64(attrAddress, out var handle));
        Assert.Equal(1UL, handle);
    }

    [Fact]
    public void PosixCondattrInit_NullAddressReturnsInvalidArgument()
    {
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);

        Assert.Equal(
            (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT,
            KernelPthreadCompatExports.PosixPthreadCondattrInit(context));
    }

    [Fact]
    public void PosixCondattrSetclock_AcceptsInitializedAttribute()
    {
        const ulong memoryBase = 0x1_0000_0000;
        const ulong attrAddress = memoryBase + 0x100;
        var memory = new FakeCpuMemory(memoryBase, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        context[CpuRegister.Rdi] = attrAddress;
        context[CpuRegister.Rsi] = 4;

        Assert.Equal(
            (int)OrbisGen2Result.ORBIS_GEN2_OK,
            KernelPthreadCompatExports.PosixPthreadCondattrSetclock(context));
    }

    [Fact]
    public void SceRwlockTrywrlock_AcquiresWithoutBlocking()
    {
        const ulong memoryBase = 0x1_1000_0000;
        const ulong rwlockAddress = memoryBase + 0x100;
        var memory = new FakeCpuMemory(memoryBase, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        context[CpuRegister.Rdi] = rwlockAddress;

        Assert.Equal(
            (int)OrbisGen2Result.ORBIS_GEN2_OK,
            KernelPthreadExtendedCompatExports.PthreadRwlockInit(context));
        Assert.Equal(
            (int)OrbisGen2Result.ORBIS_GEN2_OK,
            KernelPthreadExtendedCompatExports.PthreadRwlockTrywrlock(context));
        Assert.Equal(
            (int)OrbisGen2Result.ORBIS_GEN2_OK,
            KernelPthreadExtendedCompatExports.PthreadRwlockUnlock(context));
    }
}
