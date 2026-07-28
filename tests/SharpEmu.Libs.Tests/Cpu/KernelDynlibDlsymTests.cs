// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Core.Cpu.Native;
using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.Libs.Tests.Cpu;

public sealed class KernelDynlibDlsymTests
{
    [Fact]
    public void NormalizeArguments_PreservesCanonicalLayout()
    {
        var context = CreateContext();
        context[CpuRegister.Rdi] = 0x2001;
        context[CpuRegister.Rsi] = 0x0041_4E3B;
        context[CpuRegister.Rdx] = 0x7FFF_F01F_FF30;

        DirectExecutionBackend.NormalizeKernelDynlibDlsymArguments(
            context,
            out var symbolAddress,
            out var outputAddress);

        Assert.Equal(0x2001UL, context[CpuRegister.Rdi]);
        Assert.Equal(0x0041_4E3BUL, context[CpuRegister.Rsi]);
        Assert.Equal(0x0041_4E3BUL, symbolAddress);
        Assert.Equal(0x7FFF_F01F_FF30UL, outputAddress);
    }

    [Fact]
    public void NormalizeArguments_SwapsStandaloneLoaderLayout()
    {
        var context = CreateContext();
        context[CpuRegister.Rdi] = 0x0041_4E3B;
        context[CpuRegister.Rsi] = 0x2001;
        context[CpuRegister.Rdx] = 0x7FFF_F01F_FF30;

        DirectExecutionBackend.NormalizeKernelDynlibDlsymArguments(
            context,
            out var symbolAddress,
            out var outputAddress);

        Assert.Equal(0x2001UL, context[CpuRegister.Rdi]);
        Assert.Equal(0x0041_4E3BUL, context[CpuRegister.Rsi]);
        Assert.Equal(0x0041_4E3BUL, symbolAddress);
        Assert.Equal(0x7FFF_F01F_FF30UL, outputAddress);
    }

    [Theory]
    [InlineData("sceKernelDlsym")]
    [InlineData("LwG8g3niqwA")]
    [InlineData("__internal_kernel_dynlib_dlsym")]
    public void DlsymIdentifiers_AreRecognized(string identifier)
    {
        Assert.True(
            DirectExecutionBackend.IsKernelDynlibDlsymIdentifier(identifier));
    }

    private static CpuContext CreateContext()
    {
        var memory = new FakeCpuMemory(0x10000, 0x1000);
        return new CpuContext(memory, Generation.Gen5);
    }
}
