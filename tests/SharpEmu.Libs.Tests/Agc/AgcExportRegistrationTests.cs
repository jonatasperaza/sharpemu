// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.Libs.Tests.Agc;

public sealed class AgcExportRegistrationTests
{
    [Theory]
    [InlineData("HV4j+E0MBHE")]
    [InlineData("dbOlWdppb4o")]
    public void InterpolantMappingNidsResolveToAgcExport(string nid)
    {
        var manager = new ModuleManager();
        manager.RegisterExports(
            SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));

        Assert.True(manager.TryGetExport(nid, out var export));
        Assert.Equal("sceAgcCreateInterpolantMapping", export.Name);
        Assert.Equal("libSceAgc", export.LibraryName);
    }
}
