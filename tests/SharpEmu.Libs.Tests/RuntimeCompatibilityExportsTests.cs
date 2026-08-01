// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Text;
using SharpEmu.HLE;
using SharpEmu.Libs.AppContent;
using SharpEmu.Libs.Kernel;
using SharpEmu.Libs.Network;
using Xunit;

namespace SharpEmu.Libs.Tests;

public sealed class RuntimeCompatibilityExportsTests
{
    private const ulong MemoryBase = 0x0000_7FFF_6000_0000;
    private const ulong OutputAddress = MemoryBase + 0x100;
    private readonly FakeCpuMemory _memory = new(MemoryBase, 0x1000);
    private readonly CpuContext _context;

    public RuntimeCompatibilityExportsTests()
    {
        _context = new CpuContext(_memory, Generation.Gen5);
    }

    [Fact]
    public void NetGetMacAddressWritesStableLocallyAdministeredAddress()
    {
        _context[CpuRegister.Rdi] = OutputAddress;

        Assert.Equal(0, NetExports.NetGetMacAddress(_context));

        Span<byte> mac = stackalloc byte[6];
        Assert.True(_memory.TryRead(OutputAddress, mac));
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00, 0x53, 0x45, 0x01 }, mac.ToArray());
    }

    [Fact]
    public void NetEtherNtostrFormatsAllSixBytes()
    {
        var stringAddress = MemoryBase + 0x200;
        ReadOnlySpan<byte> mac = stackalloc byte[] { 0x02, 0x0A, 0x10, 0x53, 0x45, 0xFF };
        Assert.True(_memory.TryWrite(OutputAddress, mac));
        _context[CpuRegister.Rdi] = OutputAddress;
        _context[CpuRegister.Rsi] = stringAddress;
        _context[CpuRegister.Rdx] = 18;

        Assert.Equal(0, NetExports.NetEtherNtostr(_context));

        Span<byte> formatted = stackalloc byte[18];
        Assert.True(_memory.TryRead(stringAddress, formatted));
        Assert.Equal("02:0a:10:53:45:ff\0", Encoding.ASCII.GetString(formatted));
    }

    [Fact]
    public void TemporaryDataAvailableSpaceWritesOneGiBInKiB()
    {
        _context[CpuRegister.Rdi] = MemoryBase + 0x20;
        _context[CpuRegister.Rsi] = OutputAddress;

        Assert.Equal(
            0,
            AppContentExports.AppContentTemporaryDataGetAvailableSpaceKb(_context));

        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        Assert.True(_memory.TryRead(OutputAddress, bytes));
        Assert.Equal(1024UL * 1024UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes));
    }

    [Fact]
    public void HardwareModeAndThreadDestructorCompatibilityReturnSuccess()
    {
        _context[CpuRegister.Rax] = ulong.MaxValue;
        Assert.Equal(0, KernelRuntimeCompatExports.KernelIsTrinityMode(_context));
        Assert.Equal(0UL, _context[CpuRegister.Rax]);

        _context[CpuRegister.Rax] = ulong.MaxValue;
        Assert.Equal(0, KernelExports.CxaThreadAtexitImpl(_context));
        Assert.Equal(0UL, _context[CpuRegister.Rax]);

        _context[CpuRegister.Rax] = ulong.MaxValue;
        Assert.Equal(0, KernelRuntimeCompatExports.KernelSync(_context));
        Assert.Equal(0UL, _context[CpuRegister.Rax]);
    }

    [Theory]
    [InlineData("6Oc0bLsIYe0", "sceNetGetMacAddress", "libSceNet")]
    [InlineData("SaKib2Ug0yI", "sceAppContentTemporaryDataGetAvailableSpaceKb", "libSceAppContent")]
    [InlineData("tU5e3f9gSiU", "sceKernelIsTrinityMode", "libKernel")]
    [InlineData("qBS714-Jr3g", "__cxa_thread_atexit_impl", "libKernel")]
    [InlineData("v6M4txecCuo", "sceNetEtherNtostr", "libSceNet")]
    [InlineData("uvT2iYBBnkY", "sceKernelSync", "libKernel")]
    public void CompatibilityNidsAreRegistered(string nid, string name, string library)
    {
        var manager = new ModuleManager();
        manager.RegisterExports(
            SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));

        Assert.True(manager.TryGetExport(nid, out var export));
        Assert.Equal(name, export.Name);
        Assert.Equal(library, export.LibraryName);
    }
}
