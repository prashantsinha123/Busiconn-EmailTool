using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Busiconn.EmailTool;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("BusiconnApi"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("SendGrid"));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHttpClient<BusiconnApiClient>(h => h.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<SendGridEmailService>(h => h.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<CampaignService>();
var app = builder.Build();
app.UseCors();
app.Use(async (context, next) =>
{
    var origin = context.Request.Headers.Origin.ToString();
    if (origin.Length > 0 && origin != "http://localhost:5173")
    { context.Response.StatusCode = 403; return; }
    var key = builder.Configuration["Tool:AdminKey"];
    var supplied = context.Request.Headers["X-Tool-Key"].ToString();
    if (string.IsNullOrWhiteSpace(key) || !CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
    { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "Enter the local tool access key. Configure Tool:AdminKey on the server." }); return; }
    try { await next(); }
    catch (ApiException ex) { context.Response.StatusCode = 502; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (ArgumentException ex) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (Exception) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "The operation failed. Check server configuration and connectivity." }); }
});
app.MapGet("/api/config", (IOptions<EmailOptions> options) => new { options.Value.FromEmail, options.Value.FromName });
app.MapGet("/api/users", async (BusiconnApiClient api, CancellationToken ct) => await api.GetUsers(ct));
app.MapPost("/api/campaign/test", async (TestRequest request, CampaignService campaigns, CancellationToken ct) =>
    await campaigns.Test(request, ct));
app.MapPost("/api/campaign/send", async (CampaignRequest request, CampaignService campaigns, CancellationToken ct) =>
    await campaigns.Send(request, ct));
app.Run();

namespace Busiconn.EmailTool
{
    public class ApiOptions { public string BaseUrl { get; set; } = ""; public string AccessToken { get; set; } = ""; }
    public class EmailOptions { public string ApiKey { get; set; } = ""; public string FromEmail { get; set; } = "updates@busiconn.com"; public string FromName { get; set; } = "Busiconn"; }
    public record CampaignUser(Guid Id, string? Name, string? Email, string? MobileNumber, string[] Roles, bool IsActive, bool ValidEmail);
    public record AdminUser(Guid Id, string DisplayName, string Email, bool IsActive, string[] Roles);
    public record UserDetail(Guid Id, string EmailOrPhone);
    public record UserPage(List<AdminUser> Documents, int Total, int Page, int PageSize, int TotalPages);
    public record CampaignRequest(List<Guid> UserIds, string Subject, string HtmlContent, string TestToken);
    public record TestRequest(string Email, string Subject, string HtmlContent);
    public record RecipientResult(Guid? UserId, string? Email, string Status, string? Reason);
    public record SendResult(int Selected, int ValidRecipients, int Sent, int Failed, int Skipped, List<RecipientResult> Results);
    public class ApiException(string message) : Exception(message);

    public static class EmailRules
    {
        public static bool Valid(string? value) => !string.IsNullOrWhiteSpace(value) &&
            MailAddress.TryCreate(value, out var address) && address.Address == value &&
            address.Host.Contains('.') && !address.Host.EndsWith("busiconn.local", StringComparison.OrdinalIgnoreCase);
        public static void ValidateContent(string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(subject) || subject.Length > 200 || subject.Contains('\r') || subject.Contains('\n') ||
                string.IsNullOrWhiteSpace(body) || body.Length > 100_000)
                throw new ArgumentException("Provide a subject (up to 200 characters) and body (up to 100,000 characters).");
        }
        public static string Personalize(string html, string? name, string email) =>
            html.Replace("{{name}}", WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "there" : name))
                .Replace("{{email}}", WebUtility.HtmlEncode(email));
    }

    public class BusiconnApiClient(HttpClient http, IOptions<ApiOptions> options)
    {
        private HttpRequestMessage Request(HttpMethod method, string path)
        {
            var config = options.Value;
            if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != "https" && !(baseUri.Scheme == "http" && baseUri.IsLoopback)) ||
                string.IsNullOrWhiteSpace(config.AccessToken))
                throw new ApiException("Configure BusiconnApi:BaseUrl (HTTPS or local HTTP) and a valid Admin/System access token.");
            var request = new HttpRequestMessage(method, new Uri(baseUri.ToString().TrimEnd('/') + "/" + path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
            return request;
        }
        private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
        {
            if (!response.IsSuccessStatusCode)
                throw new ApiException($"Busiconn API returned HTTP {(int)response.StatusCode}. Verify the admin token and API URL.");
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new ApiException("Busiconn API returned an empty response.");
        }
        public async Task<List<CampaignUser>> GetUsers(CancellationToken ct)
        {
            var users = new List<AdminUser>();
            for (var page = 1; ; page++)
            {
                using var request = Request(HttpMethod.Get, $"api/v1/admin/users?page={page}&pageSize=100");
                using var response = await http.SendAsync(request, ct);
                var data = await Read<UserPage>(response, ct);
                users.AddRange(data.Documents);
                if (page >= data.TotalPages) break;
                if (page > 10_000 || data.Documents.Count == 0) throw new ApiException("Unexpected user pagination response.");
            }
            var details = new Dictionary<Guid, UserDetail>();
            foreach (var batch in users.Where(x => x.IsActive).Select(x => x.Id).Distinct().Chunk(100))
            {
                using var request = Request(HttpMethod.Post, "api/v1/users/by-ids");
                request.Content = JsonContent.Create(new { userIds = batch });
                using var response = await http.SendAsync(request, ct);
                foreach (var user in await Read<List<UserDetail>>(response, ct)) details[user.Id] = user;
            }
            return users.DistinctBy(x => x.Id).Select(x => {
                var contact = details.GetValueOrDefault(x.Id)?.EmailOrPhone;
                var mobile = contact is not null && !contact.Contains('@') &&
                    contact.Count(char.IsDigit) >= 7 && contact.All(c => char.IsDigit(c) || "+ -()".Contains(c)) ? contact : null;
                return new CampaignUser(x.Id, x.DisplayName, x.Email, mobile, x.Roles, x.IsActive, EmailRules.Valid(x.Email));
            }).ToList();
        }
    }

    public class SendGridEmailService(HttpClient http, IOptions<EmailOptions> options)
    {
        public async Task<string?> Send(string email, string subject, string html, CancellationToken ct)
        {
            var config = options.Value;
            if (string.IsNullOrWhiteSpace(config.ApiKey) || !EmailRules.Valid(config.FromEmail))
                return "Configure the SendGrid key and verified sender on the server.";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
                request.Content = JsonContent.Create(new {
                    personalizations = new[] { new { to = new[] { new { email } } } },
                    from = new { email = config.FromEmail, name = config.FromName },
                    subject, content = new[] { new { type = "text/html", value = html } }
                });
                using var response = await http.SendAsync(request, ct);
                return response.StatusCode == HttpStatusCode.Accepted ? null : $"SendGrid HTTP {(int)response.StatusCode}";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return "SendGrid timeout; acceptance unknown. Check SendGrid before retrying."; }
            catch (HttpRequestException) { return "SendGrid connection failed; acceptance unknown."; }
        }
    }

    public class CampaignService(IServiceScopeFactory scopes)
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Hash, DateTimeOffset Expires)> tests = new();
        private static string Hash(string subject, string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { subject, body }))));
        public async Task<object> Test(TestRequest request, CancellationToken ct)
        {
            EmailRules.ValidateContent(request.Subject, request.HtmlContent);
            if (!EmailRules.Valid(request.Email)) throw new ArgumentException("Enter a valid test email address.");
            using var scope = scopes.CreateScope();
            var error = await scope.ServiceProvider.GetRequiredService<SendGridEmailService>().Send(
                request.Email, request.Subject, EmailRules.Personalize(request.HtmlContent, "there", request.Email), ct);
            if (error is not null) throw new ApiException(error);
            foreach (var entry in tests.Where(x => x.Value.Expires < DateTimeOffset.UtcNow)) tests.TryRemove(entry.Key, out _);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            tests[token] = (Hash(request.Subject, request.HtmlContent), DateTimeOffset.UtcNow.AddMinutes(30));
            return new { sent = true, testToken = token, message = "Test accepted by SendGrid." };
        }
        public async Task<SendResult> Send(CampaignRequest request, CancellationToken ct)
        {
            EmailRules.ValidateContent(request.Subject, request.HtmlContent);
            if (request.UserIds is null || request.UserIds.Count == 0 || request.UserIds.Count > 100_000)
                throw new ArgumentException("Select between 1 and 100,000 users.");
            if (!await gate.WaitAsync(0, ct)) throw new ArgumentException("Another campaign is running. Wait for it to finish.");
            try
            {
                if (string.IsNullOrEmpty(request.TestToken) || !tests.TryGetValue(request.TestToken, out var test) ||
                    test.Expires < DateTimeOffset.UtcNow || test.Hash != Hash(request.Subject, request.HtmlContent))
                    throw new ArgumentException("Send a successful test for this exact subject and body before sending the campaign.");
                using var scope = scopes.CreateScope();
                var users = (await scope.ServiceProvider.GetRequiredService<BusiconnApiClient>().GetUsers(ct)).ToDictionary(x => x.Id);
                if (!tests.TryRemove(request.TestToken, out _)) throw new ArgumentException("This test authorization was already used.");
                var results = new List<RecipientResult>();
                var recipients = new List<CampaignUser>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in request.UserIds.Distinct())
                {
                    var user = users.GetValueOrDefault(id);
                    var reason = user is null ? "User no longer exists." : !user.IsActive ? "Inactive user." :
                        !user.ValidEmail ? "Missing, invalid, or synthetic email." : !seen.Add(user.Email!) ? "Duplicate email." : null;
                    if (reason is not null) results.Add(new(id, user?.Email, "Skipped", reason));
                    else recipients.Add(user!);
                }
                // Once accepted, complete the in-process campaign even if the browser disconnects.
                // Host shutdown/process failure still interrupts work; no automatic retries.
                var emailService = scope.ServiceProvider.GetRequiredService<SendGridEmailService>();
                foreach (var batch in recipients.Chunk(25))
                {
                    await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = 4 },
                        async (user, token) => {
                            var error = await emailService.Send(user.Email!, request.Subject,
                                EmailRules.Personalize(request.HtmlContent, user.Name, user.Email!), token);
                            lock (results) results.Add(new(user.Id, user.Email, error is null ? "Sent" : "Failed", error));
                        });
                    await Task.Delay(200);
                }
                return new(request.UserIds.Distinct().Count(), recipients.Count,
                    results.Count(x => x.Status == "Sent"), results.Count(x => x.Status == "Failed"),
                    results.Count(x => x.Status == "Skipped"), results);
            }
            finally { gate.Release(); }
        }
    }
}

