// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Core.Cpu.Native;
using SharpEmu.HLE;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace SharpEmu.Libs.Tests.Cpu;

public sealed class ImportResultLogSamplingTests
{
    private const string SaveDataDialogInitializeNid = "s9e3+YpRnzw";
    private const int SaveDataDialogNotInitialized = unchecked((int)0x80B80004);

    [Fact]
    public void UnexpectedRepeatedFailureIsSampledAfterInitialDiagnostics()
    {
        var backend = CreateBackend();
        var result = (OrbisGen2Result)SaveDataDialogNotInitialized;

        for (var call = 1; call <= 10_000; call++)
        {
            var expected = call <= 8 || call == 10_000;
            Assert.Equal(
                expected,
                InvokeShouldLogImportResult(backend, SaveDataDialogInitializeNid, result));
        }
    }

    private static DirectExecutionBackend CreateBackend()
    {
        var backend = (DirectExecutionBackend)RuntimeHelpers.GetUninitializedObject(
            typeof(DirectExecutionBackend));
        SetField(backend, "_importResultLogSampleGate", new object());

        var samplesField = typeof(DirectExecutionBackend).GetField(
            "_importResultLogSamples",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(samplesField);
        SetField(backend, samplesField.Name, Activator.CreateInstance(samplesField.FieldType)!);
        return backend;
    }

    private static bool InvokeShouldLogImportResult(
        DirectExecutionBackend backend,
        string nid,
        OrbisGen2Result result)
    {
        var method = typeof(DirectExecutionBackend).GetMethod(
            "ShouldLogImportResult",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method.Invoke(backend, [nid, result])!;
    }

    private static void SetField(DirectExecutionBackend backend, string name, object value)
    {
        var field = typeof(DirectExecutionBackend).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(backend, value);
    }
}
