using System.Net;
using System.Text;

namespace PayrollEngine.Lex.Tests;

/// <summary>Answers every request with the page of the URL or the status</summary>
internal sealed class PageHandler(Dictionary<string, (HttpStatusCode Status, string Html)> pages) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (status, html) = pages.TryGetValue(request.RequestUri!.ToString(), out var page) ? page : (HttpStatusCode.NotFound, null);
        var response = new HttpResponseMessage(status);
        if (html != null)
        {
            response.Content = new StringContent(html, Encoding.UTF8, "text/html");
        }
        return Task.FromResult(response);
    }
}
