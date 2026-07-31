// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Core.Cpu.Native;
using SharpEmu.HLE;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpEmu.Libs.Tests.Cpu;

public sealed class ExceptionHandlerTrampolineAbiTests
{
    [Fact]
    public unsafe void GeneratedTrampoline_AcquiresManagedEntryLockThroughR9()
    {
        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            return;
        }

        var code = CreateTrampolineBytes();
        ReadOnlySpan<byte> expected = [0xF0, 0x4D, 0x0F, 0xB1, 0x11];
        ReadOnlySpan<byte> wrongBaseRegister = [0xF0, 0x4C, 0x0F, 0xB1, 0x11];

        Assert.Equal(2, CountOccurrences(code, expected));
        Assert.Equal(0, CountOccurrences(code, wrongBaseRegister));
    }

    private static unsafe byte[] CreateTrampolineBytes()
    {
        var backend = (DirectExecutionBackend)RuntimeHelpers.GetUninitializedObject(
            typeof(DirectExecutionBackend));
        var lockField = typeof(DirectExecutionBackend).GetField(
            "_vehManagedEntryLock",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lockField);
        lockField.SetValue(backend, (nint)1);

        var createTrampoline = typeof(DirectExecutionBackend).GetMethod(
            "CreateExceptionHandlerTrampoline",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(createTrampoline);
        var trampoline = (nint)createTrampoline.Invoke(backend, [(nint)1])!;
        Assert.NotEqual(0, trampoline);

        try
        {
            return new ReadOnlySpan<byte>((void*)trampoline, 2048).ToArray();
        }
        finally
        {
            Assert.True(HostMemory.Free((void*)trampoline, 0, HostMemory.MEM_RELEASE));
        }
    }

    private static int CountOccurrences(ReadOnlySpan<byte> code, ReadOnlySpan<byte> expected)
    {
        var count = 0;
        var remaining = code;
        while (true)
        {
            var index = remaining.IndexOf(expected);
            if (index < 0)
            {
                return count;
            }

            count++;
            remaining = remaining[(index + expected.Length)..];
        }
    }
}
