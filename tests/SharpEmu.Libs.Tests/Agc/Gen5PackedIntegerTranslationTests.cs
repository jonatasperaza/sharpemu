// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

// Regression coverage for packed-integer operations that were lost when the
// shader compiler moved to backend-specific projects. All programs are synthetic
// GFX10 words assembled from the decoder tables.
public sealed class Gen5PackedIntegerTranslationTests
{
    private const ulong ShaderAddress = 0x1_0000_0000;

    private static readonly uint[] PackedIntegerProgram =
    [
        0x600A0501, // v_cvt_pk_u16_u32 v5, v1, v2
        0x620C0501, // v_cvt_pk_i16_i32 v6, v1, v2
        0x96820100, // s_absdiff_i32 s2, s0, s1
    ];

    [Fact]
    public void PackedIntegerOperations_CompileToSpirv()
    {
        var (ctx, state) = CreateState(PackedIntegerProgram, [0x80000000u, 1u]);

        Assert.Contains(state.Program.Instructions, item => item.Opcode == "VCvtPkU16U32");
        Assert.Contains(state.Program.Instructions, item => item.Opcode == "VCvtPkI16I32");
        Assert.Contains(state.Program.Instructions, item => item.Opcode == "SAbsdiffI32");
        Assert.True(
            Gen5ShaderScalarEvaluator.TryEvaluate(ctx, state, out var evaluation, out var error),
            error);
        Assert.True(
            Gen5SpirvTranslator.TryCompileComputeShader(
                state,
                evaluation,
                1,
                1,
                1,
                out var shader,
                out error),
            error);

        var opcodes = ReadSpirvOpcodes(shader.Spirv);
        Assert.Contains((ushort)SpirvOp.BitwiseAnd, opcodes);
        Assert.Contains((ushort)SpirvOp.ShiftLeftLogical, opcodes);
        Assert.Contains((ushort)SpirvOp.BitwiseOr, opcodes);
        Assert.Contains((ushort)SpirvOp.ShiftRightArithmetic, opcodes);
        Assert.Contains((ushort)SpirvOp.BitwiseXor, opcodes);
    }

    [Theory]
    [InlineData(0x00000002u, 0x00000005u, 0x00000003u)]
    [InlineData(0xFFFFFFFFu, 0x00000000u, 0x00000001u)]
    [InlineData(0x80000000u, 0x00000000u, 0x80000000u)]
    [InlineData(0x80000000u, 0x00000001u, 0x7FFFFFFFu)]
    [InlineData(0x80000000u, 0xFFFFFFFFu, 0x7FFFFFFFu)]
    public void ScalarEvaluator_AbsdiffMatchesRdna2WrappingSemantics(
        uint left,
        uint right,
        uint expected)
    {
        var (ctx, state) = CreateState([0x96820100], [left, right]);

        Assert.True(
            Gen5ShaderScalarEvaluator.TryEvaluate(ctx, state, out var evaluation, out var error),
            error);
        Assert.Equal(expected, evaluation.ScalarRegisters[2]);
    }

    private static (CpuContext Context, Gen5ShaderState State) CreateState(
        uint[] words,
        uint[] userData)
    {
        var memory = new FakeCpuMemory(ShaderAddress, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        Gen5ShaderAtomicDecodeTests.WriteProgram(memory, ShaderAddress, words);
        Assert.True(
            Gen5ShaderTranslator.TryDecodeProgram(
                context,
                ShaderAddress,
                out var program,
                out var error),
            error);
        return (context, new Gen5ShaderState(program!, userData, Metadata: null));
    }

    private static HashSet<ushort> ReadSpirvOpcodes(byte[] spirv)
    {
        var opcodes = new HashSet<ushort>();
        for (var offset = 5 * sizeof(uint); offset + sizeof(uint) <= spirv.Length;)
        {
            var word = BinaryPrimitives.ReadUInt32LittleEndian(
                spirv.AsSpan(offset, sizeof(uint)));
            opcodes.Add((ushort)word);
            offset += Math.Max((int)(word >> 16), 1) * sizeof(uint);
        }

        return opcodes;
    }
}
