using System.Buffers;
using SliceFx.Wasi;
using ITypes = ProxyWorld.wit.Imports.wasi.http.v0_2_0.ITypesImports;

// CA1707/CA1711: WIT-bindgen generates versioned namespaces (e.g. v0_2_0) containing underscores;
// this implementation must match those names to satisfy the generated partial/interface contracts.
#pragma warning disable CA1707, CA1711
namespace ProxyWorld.wit.Exports.wasi.http.v0_2_0;

public class IncomingHandlerExportsImpl : IIncomingHandlerExports
{
    private const int MaxRequestBodyBytes = 1024 * 1024;
    private const int MaxResponseWriteChunkBytes = 4096;

    // B2: _app is now shared with the cron trigger via InboxApp.App (same DI container).
    // Construction and DI wiring moved to InboxApp to avoid duplicating per-trigger setup.
    private static readonly WasiApp _app = Inbox.Server.InboxApp.App;

    public static void Handle(ITypes.IncomingRequest request, ITypes.ResponseOutparam responseOut)
    {
        var method = GetMethod(request.Method());
        WasiHttpMarshalling.SplitPathAndQuery(request.PathWithQuery() ?? "/", out var path, out var query);
        var headers = ReadHeaders(request.Headers());

        WasiResponse workerResp;
        try
        {
            var body = ReadBody(request, headers);
            var workerReq = new WasiRequest(method, path, headers, query, body);
            workerResp = _app.DispatchAsync(workerReq).GetAwaiter().GetResult();
        }
        catch (RequestBodyTooLargeException)
        {
            workerResp = global::SliceFx.Wasi.WasiResults.Problem(413, "Payload Too Large", $"Request body exceeds {MaxRequestBodyBytes} bytes.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            workerResp = global::SliceFx.Wasi.WasiResults.Problem(500, "Internal Server Error", "An unexpected error occurred.");
        }

        SendResponse(responseOut, workerResp);
    }

    private static string GetMethod(ITypes.Method method) => method.Tag switch
    {
        ITypes.Method.Tags.Get => "GET",
        ITypes.Method.Tags.Post => "POST",
        ITypes.Method.Tags.Put => "PUT",
        ITypes.Method.Tags.Delete => "DELETE",
        ITypes.Method.Tags.Patch => "PATCH",
        ITypes.Method.Tags.Head => "HEAD",
        ITypes.Method.Tags.Options => "OPTIONS",
        ITypes.Method.Tags.Connect => "CONNECT",
        ITypes.Method.Tags.Trace => "TRACE",
        ITypes.Method.Tags.Other => method.AsOther,
        _ => "GET",
    };

    private static Dictionary<string, string> ReadHeaders(ITypes.Fields fields)
        => WasiHttpMarshalling.ParseHeaders(fields.Entries());

    private static byte[]? ReadBody(ITypes.IncomingRequest request, Dictionary<string, string> headers)
    {
        if (!WasiHttpMarshalling.IsBodySizeWithinLimit(headers, MaxRequestBodyBytes))
            throw new RequestBodyTooLargeException();

        ITypes.IncomingBody inBody;
        try
        {
            inBody = request.Consume();
        }
        catch (ProxyWorld.WitException)
        {
            return null;
        }

        using var body = inBody;
        using var stream = body.Stream();
        var writer = new ArrayBufferWriter<byte>();
        try
        {
            while (true)
            {
                var chunk = stream.BlockingRead(65536);
                if (chunk.Length == 0)
                {
                    break;
                }

                if (writer.WrittenCount + chunk.Length > MaxRequestBodyBytes)
                {
                    throw new RequestBodyTooLargeException();
                }

                writer.Write(chunk);
            }
        }
        catch (ProxyWorld.WitException)
        {
            // Stream ended (EOF represented as a WitException from the WASI runtime).
        }

        if (writer.WrittenCount == 0)
        {
            return null;
        }

        return writer.WrittenSpan.ToArray();
    }

    private static void SendResponse(ITypes.ResponseOutparam responseOut, WasiResponse workerResp)
    {
        var headerList = WasiHttpMarshalling.FormatResponseHeaders(workerResp.Headers).ToList();

        ITypes.Fields fields;
        try
        {
            fields = ITypes.Fields.FromList(headerList);
        }
        catch (ProxyWorld.WitException<ITypes.HeaderError>)
        {
            fields = new ITypes.Fields();
        }

        var response = new ITypes.OutgoingResponse(fields);
        response.SetStatusCode((ushort)workerResp.Status);

        var outBody = response.Body();
        ITypes.ResponseOutparam.Set(responseOut,
            Result<ITypes.OutgoingResponse, ITypes.ErrorCode>.Ok(response));

        if (workerResp.Body.Length > 0)
        {
            using var stream = outBody.Write();
            var remaining = workerResp.Body.AsSpan();
            while (!remaining.IsEmpty)
            {
                var writable = stream.CheckWrite();
                if (writable == 0)
                {
                    stream.BlockingFlush();
                    continue;
                }

                var count = (int)Math.Min((ulong)Math.Min(remaining.Length, MaxResponseWriteChunkBytes), writable);
                stream.Write(remaining[..count]);
                remaining = remaining[count..];
            }

            stream.BlockingFlush();
        }

        ITypes.OutgoingBody.Finish(outBody, null);
    }

    private sealed class RequestBodyTooLargeException : Exception
    {
    }
}
#pragma warning restore CA1707, CA1711
