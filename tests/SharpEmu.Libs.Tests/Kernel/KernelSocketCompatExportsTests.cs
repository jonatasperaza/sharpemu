// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Kernel;

public sealed class KernelSocketCompatExportsTests
{
    private const ulong MemoryBase = 0x0000_7FFF_3000_0000;

    [Fact]
    public void Connect_InvalidSockaddrLeavesFdOpenForGuestClose()
    {
        const ulong memoryBase = 0x0000_7FFF_3000_0000;
        var context = new CpuContext(new FakeCpuMemory(memoryBase, 0x1000), Generation.Gen5);
        context[CpuRegister.Rdi] = 2;
        context[CpuRegister.Rsi] = 1;
        context[CpuRegister.Rdx] = 6;

        Assert.Equal(0, KernelSocketCompatExports.Socket(context));
        Assert.NotEqual(ulong.MaxValue, context[CpuRegister.Rax]);
        var guestFd = checked((int)context[CpuRegister.Rax]);

        try
        {
            context[CpuRegister.Rdi] = unchecked((ulong)guestFd);
            context[CpuRegister.Rsi] = memoryBase;
            context[CpuRegister.Rdx] = 0;

            Assert.Equal(0, KernelSocketCompatExports.Connect(context));
            Assert.Equal(ulong.MaxValue, context[CpuRegister.Rax]);

            context[CpuRegister.Rdi] = unchecked((ulong)guestFd);
            Assert.Equal(
                (int)OrbisGen2Result.ORBIS_GEN2_OK,
                KernelMemoryCompatExports.PosixClose(context));
            Assert.Equal(0UL, context[CpuRegister.Rax]);

            // A second close of an already-closed fd fails per the POSIX ABI:
            // -1 with errno set, not the raw Orbis NOT_FOUND sentinel.
            context[CpuRegister.Rdi] = unchecked((ulong)guestFd);
            Assert.Equal(-1, KernelMemoryCompatExports.PosixClose(context));
            Assert.Equal(ulong.MaxValue, context[CpuRegister.Rax]);
        }
        finally
        {
            KernelSocketCompatExports.TryCloseSocketFd(guestFd);
        }
    }

    [Theory]
    [InlineData("oBr313PppNE", "sendto")]
    [InlineData("lUk6wrGXyMw", "recvfrom")]
    public void UdpNids_RegisterAsLibKernelExports(string nid, string exportName)
    {
        var manager = new ModuleManager();
        manager.RegisterExports(
            SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));

        Assert.True(manager.TryGetExport(nid, out var export));
        Assert.Equal(exportName, export.Name);
        Assert.Equal("libKernel", export.LibraryName);
    }

    [Fact]
    public void Recvfrom_EmptyNonBlockingSocketReturnsMinusOne()
    {
        var context = new CpuContext(
            new FakeCpuMemory(MemoryBase, 0x1000),
            Generation.Gen5);
        context[CpuRegister.Rdi] = 2;
        context[CpuRegister.Rsi] = 2 | 0x20000000u;
        context[CpuRegister.Rdx] = 17;
        Assert.Equal(0, KernelSocketCompatExports.Socket(context));
        var guestFd = checked((int)context[CpuRegister.Rax]);

        try
        {
            context[CpuRegister.Rdi] = unchecked((ulong)guestFd);
            context[CpuRegister.Rsi] = MemoryBase;
            context[CpuRegister.Rdx] = 64;
            context[CpuRegister.Rcx] = 0;
            context[CpuRegister.R8] = 0;
            context[CpuRegister.R9] = 0;

            Assert.Equal(0, KernelSocketCompatExports.Recvfrom(context));
            Assert.Equal(ulong.MaxValue, context[CpuRegister.Rax]);
        }
        finally
        {
            KernelSocketCompatExports.TryCloseSocketFd(guestFd);
        }
    }

    [Fact]
    public void Sendto_SuppressesNonLoopbackDatagramAndReportsLength()
    {
        var memory = new FakeCpuMemory(MemoryBase, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        context[CpuRegister.Rdi] = 2;
        context[CpuRegister.Rsi] = 2;
        context[CpuRegister.Rdx] = 17;
        Assert.Equal(0, KernelSocketCompatExports.Socket(context));
        var guestFd = checked((int)context[CpuRegister.Rax]);

        try
        {
            var payload = new byte[] { 1, 2, 3, 4 };
            Assert.True(memory.TryWrite(MemoryBase, payload));
            Span<byte> sockaddr = stackalloc byte[16];
            sockaddr[0] = 16;
            sockaddr[1] = 2;
            sockaddr[2] = 0x4A;
            sockaddr[3] = 0x38;
            sockaddr[4] = 203;
            sockaddr[5] = 0;
            sockaddr[6] = 113;
            sockaddr[7] = 1;
            Assert.True(memory.TryWrite(MemoryBase + 0x100, sockaddr));

            context[CpuRegister.Rdi] = unchecked((ulong)guestFd);
            context[CpuRegister.Rsi] = MemoryBase;
            context[CpuRegister.Rdx] = unchecked((ulong)payload.Length);
            context[CpuRegister.Rcx] = 0;
            context[CpuRegister.R8] = MemoryBase + 0x100;
            context[CpuRegister.R9] = 16;

            Assert.Equal(0, KernelSocketCompatExports.Sendto(context));
            Assert.Equal(unchecked((ulong)payload.Length), context[CpuRegister.Rax]);
        }
        finally
        {
            KernelSocketCompatExports.TryCloseSocketFd(guestFd);
        }
    }
}
