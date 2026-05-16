using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Shield.Feeds.Osv.Tests;

public sealed class OsvServerFixture : IDisposable
{
    public WireMockServer Server { get; }
    public HttpClient Client { get; }

    public OsvServerFixture()
    {
        Server = WireMockServer.Start();
        Client = new HttpClient { BaseAddress = new Uri(Server.Urls[0]) };
    }

    public static string ReadFixture(string filename)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", filename);
        return File.ReadAllText(path);
    }

    public void StubBatch(string responseBody)
    {
        Server
            .Given(Request.Create().WithPath("/v1/querybatch").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(responseBody));
    }

    public void StubVuln(string id, string responseBody, int statusCode = 200)
    {
        Server
            .Given(Request.Create().WithPath($"/v1/vulns/{id}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(statusCode).WithHeader("Content-Type", "application/json").WithBody(responseBody));
    }

    public void StubBatchSuccess(string successBody) => StubBatch(successBody);

    public int CountBatchCalls() =>
        Server.LogEntries.Count(entry =>
            entry.RequestMessage.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && entry.RequestMessage.AbsolutePath.EndsWith("/v1/querybatch", StringComparison.Ordinal));

    public void Dispose()
    {
        Client.Dispose();
        Server.Stop();
        Server.Dispose();
    }
}
