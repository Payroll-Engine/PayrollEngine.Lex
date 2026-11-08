using System.Net;
using System.Net.Http.Headers;

namespace PayrollEngine.Lex.Tests;

internal sealed class BodyHandler(HttpStatusCode status, string body, string mediaType) : HttpMessageHandler
{
    private int Requests { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return Task.FromResult(new HttpResponseMessage(status) { Content = content });
    }
}
