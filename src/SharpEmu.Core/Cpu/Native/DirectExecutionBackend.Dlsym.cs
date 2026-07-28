// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Core.Cpu.Native;

public sealed unsafe partial class DirectExecutionBackend
{
	private const ulong ImportStubRegionCanonicalBase = 0x0000_7000_0000_0000UL;
	private const ulong ImportStubRegionAddressStride = 0x0000_0000_0100_0000UL;
	private const ulong LazyImportStubSlotSize = 0x10;
	private const ulong ImportStubRegionPageSize = 0x1000;
	private const string KernelDynlibDlsymAerolibNid = "LwG8g3niqwA";

	private readonly object _lazyDlsymStubGate = new();
	private readonly Dictionary<string, ulong> _lazyDlsymStubCache =
		new(StringComparer.Ordinal);
	private ulong _lazyImportStubPoolBase;
	private ulong _lazyImportStubNextSlot;
	private ulong _lazyImportStubPoolLimit;
	private bool _lazyImportStubPoolMapped;

	internal static void NormalizeKernelDynlibDlsymArguments(
		CpuContext cpuContext,
		out ulong symbolNameAddress,
		out ulong outputAddress)
	{
		var handle = cpuContext[CpuRegister.Rdi];
		symbolNameAddress = cpuContext[CpuRegister.Rsi];
		outputAddress = cpuContext[CpuRegister.Rdx];

		// Standalone bootstrap loaders can call through the bridge with
		// (symbol_ptr, handle, out) instead of (handle, symbol_ptr, out).
		if (symbolNameAddress < 0x10000 &&
			IsPlausibleDynlibSymbolPointer(handle))
		{
			symbolNameAddress = handle;
			cpuContext[CpuRegister.Rdi] = cpuContext[CpuRegister.Rsi];
			cpuContext[CpuRegister.Rsi] = handle;
		}
	}

	private static bool IsPlausibleDynlibSymbolPointer(ulong address) =>
		address >= 0x10000 && address < 0x0000_8000_0000_0000UL;

	private OrbisGen2Result CompleteKernelDynlibDlsymFailure(
		CpuContext cpuContext,
		ulong outputAddress)
	{
		if (outputAddress != 0)
		{
			_ = TryWriteUInt64Compat(outputAddress, 0);
		}

		cpuContext[CpuRegister.Rax] = ulong.MaxValue;
		return OrbisGen2Result.ORBIS_GEN2_OK;
	}

	private void ResetLazyDlsymStubState()
	{
		lock (_lazyDlsymStubGate)
		{
			_lazyDlsymStubCache.Clear();
			_lazyImportStubPoolMapped = false;
			_lazyImportStubPoolBase = 0;
			_lazyImportStubNextSlot = 0;
			_lazyImportStubPoolLimit = 0;
		}
	}

	private bool TryResolveDlsymGuestAddress(
		int moduleHandle,
		string symbolName,
		out ulong guestAddress)
	{
		guestAddress = 0;
		if (string.IsNullOrWhiteSpace(symbolName))
		{
			return false;
		}

		// The PS5 payload SDK compares the result of dlsym("sceKernelDlsym")
		// with payload_args.sys_dynlib_dlsym. Preserve that identity so its
		// bootstrap selects the dlsym-based syscall path.
		if (IsKernelDynlibDlsymIdentifier(symbolName) &&
			TryFindBootstrapBridgeGuestAddress(out guestAddress))
		{
			return true;
		}

		if (TryResolveModuleSymbolAddress(moduleHandle, symbolName, out guestAddress) ||
			TryResolveRuntimeSymbolAddress(symbolName, out guestAddress) ||
			TryResolveRuntimeSymbolAddress(ComputePsNid(symbolName), out guestAddress) ||
			TryResolveRuntimeSymbolAlias(symbolName, out guestAddress) ||
			TryFindImportStubGuestAddress(symbolName, out guestAddress))
		{
			return true;
		}

		var hasAerolibSymbol =
			Aerolib.Instance.TryGetByExportName(symbolName, out var hleSymbol);
		if (hasAerolibSymbol)
		{
			if (HleDataSymbols.TryGetAddress(hleSymbol.Nid, out _))
			{
				return false;
			}

			if (TryResolveRuntimeSymbolAddress(hleSymbol.Nid, out guestAddress) ||
				TryResolveRuntimeSymbolAddress(hleSymbol.ExportName, out guestAddress) ||
				TryFindImportStubGuestAddress(hleSymbol.Nid, out guestAddress) ||
				TryFindImportStubGuestAddress(hleSymbol.ExportName, out guestAddress))
			{
				return true;
			}
		}
		else if (Aerolib.Instance.TryGetByNid(symbolName, out hleSymbol))
		{
			if (HleDataSymbols.TryGetAddress(hleSymbol.Nid, out _))
			{
				return false;
			}

			if (TryFindImportStubGuestAddress(hleSymbol.Nid, out guestAddress) ||
				TryFindImportStubGuestAddress(hleSymbol.ExportName, out guestAddress))
			{
				return true;
			}

			hasAerolibSymbol = true;
		}

		if (!TryResolveLazyDispatchTarget(
				symbolName,
				hasAerolibSymbol,
				in hleSymbol,
				out var dispatchNid,
				out var export))
		{
			return false;
		}

		return TryGetOrCreateLazyImportStub(
			dispatchNid,
			symbolName,
			hasAerolibSymbol ? hleSymbol : null,
			export,
			out guestAddress);
	}

	private bool TryFindImportStubGuestAddress(
		string identifier,
		out ulong guestAddress)
	{
		guestAddress = 0;
		if (string.IsNullOrWhiteSpace(identifier))
		{
			return false;
		}

		var importEntries = _importEntries;
		for (var index = 0; index < importEntries.Length; index++)
		{
			var entry = importEntries[index];
			if (!ImportStubEntryMatchesIdentifier(entry, identifier))
			{
				continue;
			}

			if (entry.Address >= 0x10000)
			{
				guestAddress = entry.Address;
				return true;
			}
		}

		return false;
	}

	private bool TryFindBootstrapBridgeGuestAddress(out ulong guestAddress)
	{
		guestAddress = 0;
		var importEntries = _importEntries;
		for (var index = 0; index < importEntries.Length; index++)
		{
			var entry = importEntries[index];
			if (!string.Equals(
					entry.Nid,
					RuntimeStubNids.BootstrapBridge,
					StringComparison.Ordinal) ||
				entry.Address < 0x10000)
			{
				continue;
			}

			if (guestAddress == 0 || entry.Address < guestAddress)
			{
				guestAddress = entry.Address;
			}
		}

		return guestAddress != 0;
	}

	private static bool ImportStubEntryMatchesIdentifier(
		in ImportStubEntry entry,
		string identifier)
	{
		if (string.Equals(entry.Nid, identifier, StringComparison.Ordinal))
		{
			return true;
		}

		if (entry.Export is not { } export)
		{
			return false;
		}

		return string.Equals(export.Name, identifier, StringComparison.Ordinal) ||
			string.Equals(export.Nid, identifier, StringComparison.Ordinal);
	}

	internal static bool IsKernelDynlibDlsymIdentifier(string identifier) =>
		string.Equals(identifier, "sceKernelDlsym", StringComparison.Ordinal) ||
		string.Equals(identifier, KernelDynlibDlsymAerolibNid, StringComparison.Ordinal) ||
		string.Equals(
			identifier,
			RuntimeStubNids.KernelDynlibDlsym,
			StringComparison.Ordinal);

	private bool TryResolveLazyDispatchTarget(
		string symbolName,
		bool hasAerolibSymbol,
		in SysAbiSymbol hleSymbol,
		out string dispatchNid,
		out ExportedFunction? export)
	{
		export = null;
		dispatchNid = string.Empty;

		if (IsKernelDynlibDlsymIdentifier(symbolName))
		{
			dispatchNid = KernelDynlibDlsymAerolibNid;
			_ = _moduleManager.TryGetExport(dispatchNid, out export);
			return true;
		}

		if (!hasAerolibSymbol ||
			HleDataSymbols.TryGetAddress(hleSymbol.Nid, out _) ||
			!_moduleManager.TryGetExport(hleSymbol.Nid, out export))
		{
			return false;
		}

		dispatchNid = hleSymbol.Nid;
		return true;
	}

	private bool TryGetOrCreateLazyImportStub(
		string dispatchNid,
		string symbolName,
		SysAbiSymbol? hleSymbol,
		ExportedFunction? export,
		out ulong guestAddress)
	{
		guestAddress = 0;
		if (string.IsNullOrWhiteSpace(dispatchNid))
		{
			return false;
		}

		lock (_lazyDlsymStubGate)
		{
			if (TryGetCachedLazyDlsymAddress(
					dispatchNid,
					symbolName,
					out guestAddress))
			{
				return true;
			}

			if (!EnsureLazyImportStubPoolMapped())
			{
				LogLazyImportStubMaterializationFailure(
					"import stub region unresolved",
					dispatchNid,
					symbolName);
				return false;
			}

			if (!TryAllocateLazyImportStubSlot(out guestAddress))
			{
				LogLazyImportStubMaterializationFailure(
					"lazy import stub pool exhausted",
					dispatchNid,
					symbolName);
				return false;
			}

			var previousEntries = _importEntries;
			var importIndex = previousEntries.Length;
			var newEntries = new ImportStubEntry[importIndex + 1];
			if (importIndex != 0)
			{
				Array.Copy(previousEntries, newEntries, importIndex);
			}

			newEntries[importIndex] = new ImportStubEntry(
				guestAddress,
				dispatchNid,
				export,
				IsLeafImport(dispatchNid),
				IsNoBlockLeafImport(dispatchNid),
				ShouldSuppressStrlenTrace(dispatchNid),
				IsImportLoopGuardBoundary(dispatchNid),
				StableHash64(dispatchNid));

			var hostTrampoline = CreateImportHandlerTrampoline(importIndex);
			if (hostTrampoline == 0 ||
				!PatchImportStub((nint)(long)guestAddress, hostTrampoline))
			{
				guestAddress = 0;
				LogLazyImportStubMaterializationFailure(
					"failed to patch lazy import stub trampoline",
					dispatchNid,
					symbolName);
				return false;
			}

			_importEntries = newEntries;
			RegisterDlsymCacheKeys(
				symbolName,
				dispatchNid,
				hleSymbol,
				guestAddress);
			return true;
		}
	}

	private bool TryGetCachedLazyDlsymAddress(
		string dispatchNid,
		string symbolName,
		out ulong guestAddress)
	{
		if (_lazyDlsymStubCache.TryGetValue(dispatchNid, out guestAddress) ||
			_lazyDlsymStubCache.TryGetValue(symbolName, out guestAddress))
		{
			return guestAddress >= 0x10000;
		}

		return false;
	}

	private void RegisterDlsymCacheKeys(
		string symbolName,
		string dispatchNid,
		SysAbiSymbol? hleSymbol,
		ulong guestAddress)
	{
		RegisterDlsymCacheKey(symbolName, guestAddress);
		RegisterDlsymCacheKey(dispatchNid, guestAddress);

		if (IsKernelDynlibDlsymIdentifier(symbolName) ||
			IsKernelDynlibDlsymIdentifier(dispatchNid))
		{
			RegisterDlsymCacheKey(
				RuntimeStubNids.KernelDynlibDlsym,
				guestAddress);
			RegisterDlsymCacheKey(
				KernelDynlibDlsymAerolibNid,
				guestAddress);
			RegisterDlsymCacheKey("sceKernelDlsym", guestAddress);
		}

		if (hleSymbol.HasValue)
		{
			RegisterDlsymCacheKey(hleSymbol.Value.Nid, guestAddress);
			RegisterDlsymCacheKey(hleSymbol.Value.ExportName, guestAddress);
		}
	}

	private void RegisterDlsymCacheKey(string key, ulong guestAddress)
	{
		if (!string.IsNullOrWhiteSpace(key) &&
			IsRuntimeSymbolAddressUsable(guestAddress))
		{
			_lazyDlsymStubCache[key] = guestAddress;
		}
	}

	private static void LogLazyImportStubMaterializationFailure(
		string reason,
		string dispatchNid,
		string symbolName)
	{
		Console.Error.WriteLine(
			$"[LOADER][WARN] Lazy import stub materialization failed ({reason}): " +
			$"nid={dispatchNid} symbol='{symbolName}'");
	}

	private bool TryResolveImportStubRegionBounds(
		ImportStubEntry[] importEntries,
		out ulong regionBase,
		out ulong regionLimit)
	{
		regionBase = 0;
		regionLimit = 0;

		for (var candidateIndex = 0; candidateIndex < 64; candidateIndex++)
		{
			var candidateBase = ImportStubRegionCanonicalBase -
				(ulong)candidateIndex * ImportStubRegionAddressStride;
			if (VirtualQuery(
					(void*)candidateBase,
					out var memoryInfo,
					(nuint)sizeof(MEMORY_BASIC_INFORMATION64)) == 0 ||
				memoryInfo.RegionSize == 0 ||
				memoryInfo.State != 4096)
			{
				continue;
			}

			var candidateLimit = candidateBase + memoryInfo.RegionSize;
			var hasStub = false;
			for (var index = 0; index < importEntries.Length; index++)
			{
				var entryAddress = importEntries[index].Address;
				if (entryAddress >= candidateBase &&
					entryAddress < candidateLimit &&
					(entryAddress - candidateBase) % LazyImportStubSlotSize == 0)
				{
					hasStub = true;
					break;
				}
			}

			if (!hasStub)
			{
				continue;
			}

			regionBase = candidateBase;
			regionLimit = candidateLimit;
			return true;
		}

		ulong maxStubEnd = 0;
		for (var index = 0; index < importEntries.Length; index++)
		{
			var entryAddress = importEntries[index].Address;
			if (entryAddress < ImportStubRegionCanonicalBase ||
				(entryAddress - ImportStubRegionCanonicalBase) %
				LazyImportStubSlotSize != 0)
			{
				continue;
			}

			var entryEnd = entryAddress + LazyImportStubSlotSize;
			if (entryEnd > maxStubEnd)
			{
				maxStubEnd = entryEnd;
				regionBase = ImportStubRegionCanonicalBase;
			}
		}

		if (regionBase == 0 || maxStubEnd <= regionBase)
		{
			return false;
		}

		var spanBytes = maxStubEnd - regionBase;
		regionLimit =
			regionBase + AlignUp(spanBytes, ImportStubRegionPageSize);
		return regionLimit > regionBase;
	}

	private bool EnsureLazyImportStubPoolMapped()
	{
		if (_lazyImportStubPoolMapped && _lazyImportStubPoolBase != 0)
		{
			return true;
		}

		var importEntries = _importEntries;
		if (!TryResolveImportStubRegionBounds(
				importEntries,
				out var importStubRegionBase,
				out var importStubRegionLimit))
		{
			return false;
		}

		var nextSlot = importStubRegionBase;
		for (var index = 0; index < importEntries.Length; index++)
		{
			var entryAddress = importEntries[index].Address;
			if (entryAddress < importStubRegionBase ||
				entryAddress >= importStubRegionLimit)
			{
				continue;
			}

			var entryEnd = entryAddress + LazyImportStubSlotSize;
			if (entryEnd > nextSlot)
			{
				nextSlot = entryEnd;
			}
		}

		if (nextSlot >= importStubRegionLimit)
		{
			return false;
		}

		_lazyImportStubPoolBase = importStubRegionBase;
		_lazyImportStubNextSlot = nextSlot;
		_lazyImportStubPoolLimit = importStubRegionLimit;
		_lazyImportStubPoolMapped = true;
		return true;
	}

	private bool TryAllocateLazyImportStubSlot(out ulong guestAddress)
	{
		guestAddress = 0;
		if (!_lazyImportStubPoolMapped ||
			_lazyImportStubNextSlot < _lazyImportStubPoolBase ||
			_lazyImportStubNextSlot + LazyImportStubSlotSize >
				_lazyImportStubPoolLimit)
		{
			return false;
		}

		guestAddress = _lazyImportStubNextSlot;
		_lazyImportStubNextSlot += LazyImportStubSlotSize;
		return true;
	}
}
