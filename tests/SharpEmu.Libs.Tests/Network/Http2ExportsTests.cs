// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Network;
using Xunit;

namespace SharpEmu.Libs.Tests.Network;

[CollectionDefinition("Http2Exports", DisableParallelization = true)]
public sealed class Http2ExportsCollectionDefinition;

[Collection("Http2Exports")]
public sealed class Http2ExportsTests : IDisposable
{
    private const int Http2ErrorInvalidId = unchecked((int)0x80436004);
    private const int Http2ErrorInvalidArgument = unchecked((int)0x80436016);
    private const ulong MemoryBase = 0x0000_7FFF_5000_0000;
    private const ulong MethodAddress = MemoryBase + 0x1000;
    private const ulong UrlAddress = MemoryBase + 0x2000;
    private const ulong AlternateUrlAddress = MemoryBase + 0x4000;
    private const ulong HeaderNameAddress = MemoryBase + 0x6000;
    private const ulong HeaderValueAddress = MemoryBase + 0x7000;
    private readonly FakeCpuMemory _memory = new(MemoryBase, 0x10_000);
    private readonly CpuContext _context;

    public Http2ExportsTests()
    {
        Http2Exports.ResetForTests();
        _context = new CpuContext(_memory, Generation.Gen5);
    }

    [Fact]
    public void CreateRequestStoresStringsAndContentLengthWithUniqueIds()
    {
        var templateId = CreateTemplate(CreateContext());
        _memory.WriteCString(MethodAddress, "POST");
        _memory.WriteCString(UrlAddress, "https://example.invalid/first");
        _memory.WriteCString(AlternateUrlAddress, "https://example.invalid/second");

        var firstRequestId = CreateRequest(
            templateId,
            MethodAddress,
            UrlAddress,
            0xFEDC_BA98_7654_3210UL);
        var secondRequestId = CreateRequest(
            templateId,
            MethodAddress,
            AlternateUrlAddress,
            17);

        Assert.True(firstRequestId > 0);
        Assert.NotEqual(firstRequestId, secondRequestId);
        Assert.True(Http2Exports.TryGetRequestState(firstRequestId, out var first));
        Assert.Equal(templateId, first.TemplateId);
        Assert.Equal("POST", first.Method);
        Assert.Equal("https://example.invalid/first", first.Url);
        Assert.Equal(0xFEDC_BA98_7654_3210UL, first.ContentLength);
        Assert.True(Http2Exports.TryGetRequestState(secondRequestId, out var second));
        Assert.Equal(17UL, second.ContentLength);
    }

    [Fact]
    public void CreateRequestRejectsUnknownTemplate()
    {
        _memory.WriteCString(MethodAddress, "GET");
        _memory.WriteCString(UrlAddress, "https://example.invalid/");
        SetCreateRequestArguments(int.MaxValue, MethodAddress, UrlAddress, 0);

        Assert.Equal(
            Http2ErrorInvalidId,
            Http2Exports.Http2CreateRequestWithUrl(_context));
        Assert.Equal(unchecked((ulong)Http2ErrorInvalidId), _context[CpuRegister.Rax]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CreateRequestRejectsNullStringPointers(bool nullMethod, bool nullUrl)
    {
        var templateId = CreateTemplate(CreateContext());
        _memory.WriteCString(MethodAddress, "GET");
        _memory.WriteCString(UrlAddress, "https://example.invalid/");
        SetCreateRequestArguments(
            templateId,
            nullMethod ? 0 : MethodAddress,
            nullUrl ? 0 : UrlAddress,
            0);

        Assert.Equal(
            Http2ErrorInvalidArgument,
            Http2Exports.Http2CreateRequestWithUrl(_context));
    }

    [Fact]
    public void TermRemovesChildRequests()
    {
        var contextId = CreateContext();
        var templateId = CreateTemplate(contextId);
        _memory.WriteCString(MethodAddress, "GET");
        _memory.WriteCString(UrlAddress, "https://example.invalid/");
        var requestId = CreateRequest(templateId, MethodAddress, UrlAddress, 0);

        _context[CpuRegister.Rdi] = unchecked((ulong)contextId);
        Assert.Equal(0, Http2Exports.Http2Term(_context));
        Assert.False(Http2Exports.TryGetRequestState(requestId, out _));
    }

    [Fact]
    public void AddRequestHeaderStoresAndOverwritesCaseInsensitively()
    {
        var templateId = CreateTemplate(CreateContext());
        _memory.WriteCString(MethodAddress, "GET");
        _memory.WriteCString(UrlAddress, "https://example.invalid/");
        var requestId = CreateRequest(templateId, MethodAddress, UrlAddress, 0);
        _memory.WriteCString(HeaderNameAddress, "User-Agent");
        _memory.WriteCString(HeaderValueAddress, "SharpEmu/1");

        SetAddHeaderArguments(requestId, HeaderNameAddress, HeaderValueAddress, 1);
        Assert.Equal(0, Http2Exports.Http2AddRequestHeader(_context));
        Assert.True(Http2Exports.TryGetRequestHeader(requestId, "user-agent", out var first));
        Assert.Equal("SharpEmu/1", first);

        _memory.WriteCString(HeaderNameAddress, "USER-AGENT");
        _memory.WriteCString(HeaderValueAddress, "SharpEmu/2");
        SetAddHeaderArguments(requestId, HeaderNameAddress, HeaderValueAddress, 1);
        Assert.Equal(0, Http2Exports.Http2AddRequestHeader(_context));
        Assert.True(Http2Exports.TryGetRequestHeader(requestId, "User-Agent", out var second));
        Assert.Equal("SharpEmu/2", second);
    }

    [Fact]
    public void AddRequestHeaderRejectsUnknownRequest()
    {
        _memory.WriteCString(HeaderNameAddress, "Accept");
        _memory.WriteCString(HeaderValueAddress, "*/*");
        SetAddHeaderArguments(int.MaxValue, HeaderNameAddress, HeaderValueAddress, 1);

        Assert.Equal(Http2ErrorInvalidId, Http2Exports.Http2AddRequestHeader(_context));
    }

    [Fact]
    public void SetRequestContentLengthUpdatesExistingRequest()
    {
        var templateId = CreateTemplate(CreateContext());
        _memory.WriteCString(MethodAddress, "POST");
        _memory.WriteCString(UrlAddress, "https://example.invalid/upload");
        var requestId = CreateRequest(templateId, MethodAddress, UrlAddress, 0);
        _context[CpuRegister.Rdi] = unchecked((ulong)requestId);
        _context[CpuRegister.Rsi] = 0x2E8B;

        Assert.Equal(0, Http2Exports.Http2SetRequestContentLength(_context));
        Assert.True(Http2Exports.TryGetRequestState(requestId, out var request));
        Assert.Equal(0x2E8BUL, request.ContentLength);
    }

    [Fact]
    public void SendRequestAsyncMarksKnownRequestWithoutTouchingGuestCallbackState()
    {
        var templateId = CreateTemplate(CreateContext());
        _memory.WriteCString(MethodAddress, "POST");
        _memory.WriteCString(UrlAddress, "https://example.invalid/upload");
        var requestId = CreateRequest(templateId, MethodAddress, UrlAddress, 16);
        var dataAddress = MemoryBase + 0x8000;
        var asyncParameterAddress = MemoryBase + 0x9000;
        _context[CpuRegister.Rdi] = unchecked((ulong)requestId);
        _context[CpuRegister.Rsi] = dataAddress;
        _context[CpuRegister.Rdx] = 16;
        _context[CpuRegister.Rcx] = asyncParameterAddress;

        Assert.Equal(0, Http2Exports.Http2SendRequestAsync(_context));
        Assert.True(Http2Exports.IsRequestSent(requestId));
    }

    [Fact]
    public void PublicNidsRegisterAsHttp2Exports()
    {
        var manager = new ModuleManager();
        manager.RegisterExports(
            SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));

        AssertExport(manager, "+wCt7fCijgk", "sceHttp2CreateTemplate");
        AssertExport(manager, "mmyOCxQMVYQ", "sceHttp2CreateRequestWithURL");
        AssertExport(manager, "nrPfOE8TQu0", "sceHttp2AddRequestHeader");
        AssertExport(manager, "FSAFOzi0FpM", "sceHttp2SetRequestContentLength");
        AssertExport(manager, "A+NVAFu4eCg", "sceHttp2SendRequestAsync");
    }

    public void Dispose() => Http2Exports.ResetForTests();

    private int CreateContext()
    {
        _context[CpuRegister.Rdi] = 1;
        _context[CpuRegister.Rsi] = 2;
        _context[CpuRegister.Rdx] = 0x20_000;
        _context[CpuRegister.Rcx] = 8;
        Assert.Equal(0, Http2Exports.Http2Init(_context));
        return checked((int)_context[CpuRegister.Rax]);
    }

    private int CreateTemplate(int contextId)
    {
        _context[CpuRegister.Rdi] = unchecked((ulong)contextId);
        _context[CpuRegister.Rsi] = 0;
        _context[CpuRegister.Rdx] = 2;
        _context[CpuRegister.Rcx] = 0;
        Assert.Equal(0, Http2Exports.Http2CreateTemplate(_context));
        return checked((int)_context[CpuRegister.Rax]);
    }

    private int CreateRequest(
        int templateId,
        ulong methodAddress,
        ulong urlAddress,
        ulong contentLength)
    {
        SetCreateRequestArguments(templateId, methodAddress, urlAddress, contentLength);
        Assert.Equal(0, Http2Exports.Http2CreateRequestWithUrl(_context));
        return checked((int)_context[CpuRegister.Rax]);
    }

    private void SetCreateRequestArguments(
        int templateId,
        ulong methodAddress,
        ulong urlAddress,
        ulong contentLength)
    {
        _context[CpuRegister.Rdi] = unchecked((ulong)templateId);
        _context[CpuRegister.Rsi] = methodAddress;
        _context[CpuRegister.Rdx] = urlAddress;
        _context[CpuRegister.Rcx] = contentLength;
    }

    private void SetAddHeaderArguments(
        int requestId,
        ulong nameAddress,
        ulong valueAddress,
        int mode)
    {
        _context[CpuRegister.Rdi] = unchecked((ulong)requestId);
        _context[CpuRegister.Rsi] = nameAddress;
        _context[CpuRegister.Rdx] = valueAddress;
        _context[CpuRegister.Rcx] = unchecked((ulong)mode);
    }

    private static void AssertExport(ModuleManager manager, string nid, string name)
    {
        Assert.True(manager.TryGetExport(nid, out var export));
        Assert.Equal(name, export.Name);
        Assert.Equal("libSceHttp2", export.LibraryName);
    }
}
