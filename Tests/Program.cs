using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Busiconn.EmailTool;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
Check(EmailRules.Valid("one@example.com") && !EmailRules.Valid("bad") && !EmailRules.Valid("x@oauth.busiconn.local"), "Email validation and synthetic exclusion");
Check(EmailRules.Personalize("{{name}} {{email}}", "<script>", "one@example.com") == "&lt;script&gt; one@example.com", "Personalization HTML encoding");
Check(EmailRules.Personalize("Hi {{name}}", null, "one@example.com") == "Hi there", "Missing name fallback");
var handler = new FakeHandler();
var services = new ServiceCollection();
services.AddSingleton<IOptions<ApiOptions>>(Options.Create(new ApiOptions { BaseUrl="http://localhost:5999", AccessToken="fake-admin" }));
services.AddSingleton<IOptions<EmailOptions>>(Options.Create(new EmailOptions { ApiKey="fake-secret", FromEmail="updates@busiconn.com" }));
services.AddTransient<BusiconnApiClient>(p => new(new HttpClient(handler), p.GetRequiredService<IOptions<ApiOptions>>()));
services.AddTransient<SendGridEmailService>(p => new(new HttpClient(handler), p.GetRequiredService<IOptions<EmailOptions>>()));
services.AddSingleton<CampaignService>();
using var provider = services.BuildServiceProvider();
var users = await provider.GetRequiredService<BusiconnApiClient>().GetUsers(default);
Check(users.Count == 6 && handler.Pages == 2 && users[0].MobileNumber == "+919876543210", "Pagination and phone enrichment through HTTP");
var campaigns = provider.GetRequiredService<CampaignService>();
var test = JsonSerializer.SerializeToElement(await campaigns.Test(new("test@example.com","Subject","Hi {{name}}"),default));
var token = test.GetProperty("testToken").GetString()!;
Check(handler.Sends == 1, "Test sends exactly one message");
try { await campaigns.Send(new(users.Select(x=>x.Id).ToList(),"Changed","Hi {{name}}",token), default); throw new Exception("Missing rejection"); }
catch (ArgumentException) { Console.WriteLine("PASS Changed composer rejected"); }
var result = await campaigns.Send(new(users.Select(x=>x.Id).ToList(),"Subject","Hi {{name}}",token),default);
Check(result.Selected == 6 && result.ValidRecipients == 2 && result.Sent == 1 && result.Failed == 1 && result.Skipped == 4, "Failure isolation, inactive/invalid/synthetic/duplicate skips and totals");
Check(handler.Pages == 4, "Campaign re-fetches authoritative users");
try { await campaigns.Send(new([users[0].Id],"Subject","Hi {{name}}",token), default); throw new Exception("Missing rejection"); }
catch (ArgumentException) { Console.WriteLine("PASS Test authorization cannot be reused"); }
Console.WriteLine("All checks passed; no real backend or SendGrid requests were made.");

class FakeHandler : HttpMessageHandler
{
    public int Pages, Sends;
    private readonly Guid[] ids = Enumerable.Range(0,6).Select(_=>Guid.NewGuid()).ToArray();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if(request.RequestUri!.Host == "api.sendgrid.com") {
            if(request.Headers.Authorization?.Parameter != "fake-secret") throw new Exception("SendGrid authorization");
            var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var email = data.RootElement.GetProperty("personalizations")[0].GetProperty("to")[0].GetProperty("email").GetString();
            Interlocked.Increment(ref Sends);
            return new(email == "fail@example.com" ? HttpStatusCode.BadRequest : HttpStatusCode.Accepted);
        }
        if(request.Headers.Authorization?.Parameter != "fake-admin") throw new Exception("Backend authorization");
        if(request.Method == HttpMethod.Get) {
            Pages++;
            var second = request.RequestUri.Query.Contains("page=2&");
            var emails = new[]{"one@example.com","fail@example.com","bad","one@example.com","inactive@example.com","x@oauth.busiconn.local"};
            var indices = second ? new[]{3,4,5} : new[]{0,1,2};
            return Json(new { documents=indices.Select(i=>new {id=ids[i],displayName="User "+i,email=emails[i],isActive=i!=4,roles=new[]{"Buyer"}}),total=6,page=second?2:1,pageSize=100,totalPages=2 });
        }
        var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        return Json(body.RootElement.GetProperty("userIds").EnumerateArray().Select(x=>new {id=x.GetGuid(),emailOrPhone="+919876543210"}));
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content=JsonContent.Create(value) };
}

