using NitroPulse;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

internal static class QueryTests
{
    private sealed class FakeHandler(HttpStatusCode status, string json = "") : HttpMessageHandler
    {
        public int Calls;
        public Action<HttpResponseMessage>? Headers;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var response = new HttpResponseMessage(status) { Content = new StringContent(json) };
            Headers?.Invoke(response);
            return Task.FromResult(response);
        }
    }

    public static async Task Run()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "fixtures-queries-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var now = DateTimeOffset.UtcNow;
        string json = "{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.0.16\",\"html_url\":\"https://github.com/test/NitroPulse/releases/tag/v1.0.16\"}";
        string state = Path.Combine(directory, "state.json");
        var first = new FakeHandler(HttpStatusCode.OK, json);
        var latest = await UpdateChecker.LatestCoreAsync("test/NitroPulse", default, first, state, now);
        if (first.Calls != 1 || latest?.Tag != "v1.0.16") throw new Exception("Initial query failed");
        var cached = new FakeHandler(HttpStatusCode.OK, json);
        await UpdateChecker.LatestCoreAsync("test/NitroPulse", default, cached, state, now.AddHours(5));
        if (cached.Calls != 0) throw new Exception("Persistent cache made a network request");
        var expired = new FakeHandler(HttpStatusCode.OK, json);
        await UpdateChecker.LatestCoreAsync("test/NitroPulse", default, expired, state, now.AddHours(6));
        if (expired.Calls != 1) throw new Exception("Expired cache did not refresh");

        async Task Blocked(FakeHandler initial, string file, DateTimeOffset before)
        {
            try { await UpdateChecker.LatestCoreAsync("test/NitroPulse", default, initial, file, now); throw new Exception("Denial accepted"); }
            catch (InvalidOperationException) { }
            var retry = new FakeHandler(HttpStatusCode.OK, json);
            try { await UpdateChecker.LatestCoreAsync("test/NitroPulse", default, retry, file, before); throw new Exception("Cooldown ignored"); }
            catch (InvalidOperationException) { }
            if (initial.Calls != 1 || retry.Calls != 0) throw new Exception("Cooldown made requests");
        }
        var limited = new FakeHandler(HttpStatusCode.TooManyRequests)
        { Headers = r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10)) };
        await Blocked(limited, Path.Combine(directory, "rate.json"), now.AddMinutes(9));
        await Blocked(new FakeHandler(HttpStatusCode.Forbidden), Path.Combine(directory, "denied.json"), now.AddHours(5));
        var quota = new FakeHandler(HttpStatusCode.Forbidden)
        { Headers = r => { r.Headers.Add("x-ratelimit-remaining", "0"); r.Headers.Add("x-ratelimit-reset", now.AddHours(2).ToUnixTimeSeconds().ToString()); } };
        await Blocked(quota, Path.Combine(directory, "quota.json"), now.AddHours(1));
        Console.WriteLine("PASS: cache persistente de 6 horas, vencimiento, Retry-After, cuota y denegación; sin conexiones reales.");
    }
}
