// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

// End-to-end coverage for the GFX10 unsigned sum-of-absolute-differences family.
// The program is synthetic: it is assembled from the public instruction encoding
// and does not contain data captured from a game.
public sealed class Gen5SadSpirvTests
{
    private const ulong ShaderAddress = 0x1_0000_0000;

    [Fact]
    public void UnsignedSadFamily_EmitsPackedAndScalarOperations()
    {
        var spirv = Compile(
        [
            0xD55A0005, 0x040E0501, // v_sad_u8    v5, v1, v2, v3
            0xD55B0006, 0x040E0501, // v_sad_hi_u8 v6, v1, v2, v3
            0xD55C0007, 0x040E0501, // v_sad_u16   v7, v1, v2, v3
            0xD55D0008, 0x040E0501, // v_sad_u32   v8, v1, v2, v3
        ]);
        var opcodes = CollectOpcodes(spirv);

        Assert.Contains((ushort)SpirvOp.ExtInst, opcodes);
        Assert.Contains((ushort)SpirvOp.ISub, opcodes);
        Assert.Contains((ushort)SpirvOp.IAdd, opcodes);
        Assert.Contains((ushort)SpirvOp.ShiftRightLogical, opcodes);
        Assert.Contains((ushort)SpirvOp.ShiftLeftLogical, opcodes);
        Assert.Contains((ushort)SpirvOp.BitwiseAnd, opcodes);
    }

    private static byte[] Compile(uint[] programWords)
    {
        var memory = new FakeCpuMemory(ShaderAddress, 0x2000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        Gen5ShaderAtomicDecodeTests.WriteProgram(memory, ShaderAddress, programWords);
        var shaderRegisters = new Dictionary<uint, uint>
        {
            [Gen5ShaderAtomicDecodeTests.ComputePgmRsrc2Register] = 16u << 1,
        };

        Assert.True(
            Gen5ShaderTranslator.TryCreateState(
                ctx,
                ShaderAddress,
                0,
                shaderRegisters,
                Gen5ShaderAtomicDecodeTests.ComputeUserDataRegister,
                out var state,
                out var error),
            error);
        Assert.True(
            Gen5ShaderScalarEvaluator.TryEvaluate(ctx, state, out var evaluation, out error),
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
        return shader.Spirv;
    }

    private static HashSet<ushort> CollectOpcodes(byte[] spirv)
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
