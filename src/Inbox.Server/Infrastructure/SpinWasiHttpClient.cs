// WASI-only: uses WIT-generated types from wasi:http/outgoing-handler@0.2.0.
// Excluded from non-WASI builds via csproj <Compile Remove> condition.
//
// Handle ownership (all verified against generated bindings in obj/):
//   OutgoingRequest(Fields) ctor  → consumes Fields  (sets fields.Handle = 0)
//   OutgoingHandlerInterop.Handle → consumes OutgoingRequest (sets request.Handle = 0)
//   OutgoingBody.Finish(body,..)  → consumes OutgoingBody (sets body.Handle = 0)
// Do NOT Dispose these three after they have been moved; the Dispose guard (Handle != 0)
// makes it a no-op, but touching them post-move is use-after-move.
// DO `using` everything on the response side and the write OutputStream.
using System.Globalization;
using System.Text;
using SliceFx.Wasi;
using SliceFx.Wasi.HttpClient;
using ITypes = ProxyWorld.wit.imports.wasi.http.v0_2_0.ITypes;
using OutgoingHandlerInterop = ProxyWorld.wit.imports.wasi.http.v0_2_0.OutgoingHandlerInterop;

namespace Inbox.Server.Infrastructure;

internal sealed class SpinWasiHttpClient : IWasiHttpClient
{
    // Maximum response body bytes we will buffer (8 MB).
    // Mirrors the incoming-side MaxRequestBodyBytes guard in IncomingHandlerImpl.
    private const int MaxResponseBodyBytes = 8 * 1024 * 1024;

    public ValueTask<WasiResponse> SendAsync(WasiHttpRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        var uri = new Uri(req.Url, UriKind.Absolute);

        // Build request headers. Never set 'host' or 'content-length' — the host derives those.
        // Fields.FromList may throw WitException<HeaderError> on forbidden names.
        var headerList = new List<(string, byte[])>
        {
            ("user-agent", Encoding.UTF8.GetBytes("slicefx-inbox/0.1 (Spin WASI)")),
            ("accept", Encoding.UTF8.GetBytes("application/rss+xml, application/atom+xml, text/xml, */*")),
        };
        if (req.Headers is not null)
        {
            foreach (var (name, value) in req.Headers)
                headerList.Add((name.ToLowerInvariant(), Encoding.UTF8.GetBytes(value)));
        }

        ITypes.Fields fields;
        try
        {
            fields = ITypes.Fields.FromList(headerList);
        }
        catch (ProxyWorld.WitException ex)
        {
            throw new WasiHttpException($"Invalid request header: {ex.Message}", ex);
        }

        // OutgoingRequest(fields) consumes fields — do not touch fields after this point.
        var request = new ITypes.OutgoingRequest(fields);

        var method = req.Method.ToUpperInvariant() switch
        {
            "GET"    => ITypes.Method.Get(),
            "POST"   => ITypes.Method.Post(),
            "PUT"    => ITypes.Method.Put(),
            "DELETE" => ITypes.Method.Delete(),
            "HEAD"   => ITypes.Method.Head(),
            "PATCH"  => ITypes.Method.Patch(),
            _        => ITypes.Method.Other(req.Method),
        };
        request.SetMethod(method);
        request.SetScheme(string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            ? ITypes.Scheme.Https()
            : ITypes.Scheme.Http());
        request.SetAuthority(uri.Authority);          // host[:port], no scheme
        request.SetPathWithQuery(uri.PathAndQuery);   // "/path?query", "/" for root

        // Write the request body (even when empty, Finish is mandatory before Handle).
        // request.Body() must be called exactly once; Finish consumes the body handle.
        var outBody = request.Body();
        if (req.Body is { Length: > 0 } bodyBytes)
        {
            using var writeStream = outBody.Write();
            var offset = 0;
            while (offset < bodyBytes.Length)
            {
                var capacity = (int)writeStream.CheckWrite();
                if (capacity == 0) capacity = 4096;
                var chunk = Math.Min(capacity, bodyBytes.Length - offset);
                writeStream.Write(bodyBytes.AsSpan(offset, chunk));
                writeStream.BlockingFlush();
                offset += chunk;
            }
        }
        ITypes.OutgoingBody.Finish(outBody, null); // consumes outBody

        // Send. Handle consumes request — do not touch request after this point.
        ITypes.FutureIncomingResponse future;
        try
        {
            future = OutgoingHandlerInterop.Handle(request, null);
        }
        catch (ProxyWorld.WitException ex)
        {
            throw new WasiHttpException($"Outgoing HTTP send failed: {ex.Message}", ex);
        }

        // Block synchronously until the response is ready.
        // WASI single-thread constraint: DispatchAsync is called via GetAwaiter().GetResult()
        // in IncomingHandlerImpl, so all Task/ValueTask continuations must complete synchronously.
        using var pollable = future.Subscribe();
        pollable.Block();

        // Unwrap the doubly-nested Result:
        //   null                         → not ready (re-block; shouldn't happen after Block())
        //   Ok(Ok(IncomingResponse))     → success
        //   Ok(Err(ErrorCode))           → transport error
        //   Err(None)                    → already consumed (should never happen)
        var result = future.Get();
        while (result is null)
        {
            pollable.Block();
            result = future.Get();
        }

        if (!result.Value.IsOk)
            throw new WasiHttpException("FutureIncomingResponse already consumed.");

        var innerResult = result.Value.AsOk;
        if (!innerResult.IsOk)
            throw new WasiHttpException($"Outgoing HTTP error: {innerResult.AsErr}");

        using var response = innerResult.AsOk;

        var status = (int)response.Status();

        // Read response headers.
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var respFields = response.Headers();
        foreach (var (name, value) in respFields.Entries())
            headers[name] = Encoding.UTF8.GetString(value);

        // Read response body with an 8 MB cap.
        using var inBody = response.Consume();
        using var bodyStream = inBody.Stream();
        var bodyParts = new List<byte[]>();
        var totalBodyBytes = 0;
        try
        {
            while (true)
            {
                var chunk = bodyStream.BlockingRead(65536);
                if (chunk.Length == 0) break;
                totalBodyBytes += chunk.Length;
                if (totalBodyBytes > MaxResponseBodyBytes)
                    throw new WasiHttpException(
                        $"Response body exceeds the {MaxResponseBodyBytes / (1024 * 1024)} MB limit.");
                bodyParts.Add(chunk);
            }
        }
        catch (ProxyWorld.WitException)
        {
            // EOF — normal end of stream.
        }

        byte[] bodyArray;
        if (bodyParts.Count == 0)
        {
            bodyArray = [];
        }
        else if (bodyParts.Count == 1)
        {
            bodyArray = bodyParts[0];
        }
        else
        {
            bodyArray = new byte[totalBodyBytes];
            var pos = 0;
            foreach (var part in bodyParts)
            {
                part.CopyTo(bodyArray, pos);
                pos += part.Length;
            }
        }

        return ValueTask.FromResult(new WasiResponse(status, headers, bodyArray));
    }
}
