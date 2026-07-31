// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Core.Runtime;
using Xunit;

namespace SharpEmu.Libs.Tests.Runtime;

public sealed class AdjacentModuleDiscoveryTests
{
    [Fact]
    public void RuntimeSearchesForNativeModulesBesideEbootAndAvoidsDuplicateNames()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sharpemu-module-discovery-{Guid.NewGuid():N}");
        var sceModule = Path.Combine(root, "sce_module");

        try
        {
            Directory.CreateDirectory(sceModule);
            File.WriteAllBytes(Path.Combine(root, "libcohtml.Prospero.prx"), [1]);
            File.WriteAllBytes(Path.Combine(root, "libc.prx"), [2]);
            File.WriteAllBytes(Path.Combine(sceModule, "libc.prx"), [3]);
            File.WriteAllBytes(Path.Combine(root, "eboot.bin"), [4]);

            var modules = SharpEmuRuntime.DiscoverAdjacentModuleFiles(root);

            Assert.Equal(2, modules.Count);
            Assert.Contains(
                modules,
                module => string.Equals(
                    module.Path,
                    Path.Combine(root, "libcohtml.Prospero.prx"),
                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                modules,
                module => string.Equals(
                    module.Path,
                    Path.Combine(sceModule, "libc.prx"),
                    StringComparison.OrdinalIgnoreCase));
            Assert.All(modules, module => Assert.True(module.StartAtBoot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
