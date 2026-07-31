// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Agc;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class AgcIndirectRegisterPatchTests
{
    [Theory]
    [InlineData(0x10000000u, true, 0x0191u)]
    [InlineData(0x1000000Fu, true, 0x01A0u)]
    [InlineData(0x1000001Fu, true, 0x01B0u)]
    [InlineData(0x10000020u, true, 0x10000020u)]
    [InlineData(0x00000318u, true, 0x0318u)]
    [InlineData(0x10000000u, false, 0x10000000u)]
    [InlineData(0x000000C8u, false, 0x00C8u)]
    [InlineData(0x0000025Bu, false, 0x025Bu)]
    public void DecodeIndirectRegisterOffset_ResolvesOnlyVirtualContextBank(
        uint encodedRegister,
        bool contextRegister,
        uint expectedOffset)
    {
        Assert.Equal(
            expectedOffset,
            AgcExports.DecodeIndirectRegisterOffset(
                encodedRegister,
                contextRegister));
    }
}
