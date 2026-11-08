using System.Net;

namespace PayrollEngine.Lex.Tests;

internal sealed class StatusHandler(Func<HttpRequestMessage, HttpStatusCode> status) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(status(request)));
}
