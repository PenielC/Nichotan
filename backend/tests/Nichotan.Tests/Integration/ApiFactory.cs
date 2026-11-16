using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nichotan.Application.Common;
using Nichotan.Infrastructure.Persistence;
using Nichotan.Infrastructure.Services;

namespace Nichotan.Tests.Integration;

/// <summary>
/// The real API in-process, against a throwaway PostgreSQL database (the docker-compose server, database
/// "nichotan_test", recreated per run). Email and file storage are replaced with in-memory fakes. Staff MFA
/// is required, as in production.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@nichotan.test";
    public const string AdminPassword = "Test-Admin-Pass-1";

    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("NICHOTAN_TEST_DB") ?? "Host=localhost;Port=5438;Database=nichotan_test;Username=nichotan;Password=nichotan_local_dev";

    public FakeEmailSender Emails { get; } = new();

    public ApiFactory()
    {
        // Start from an empty database every run.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options, new SystemClock());
        db.Database.EnsureDeleted();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = ConnectionString,
            ["Jwt:Secret"] = "integration-tests-signing-key-0123456789abcdef",
            ["Security:RequireMfaForStaff"] = "true",
            ["App:WebBaseUrl"] = "https://web.test",
            ["Bootstrap:MigrateOnStartup"] = "true",
            ["Bootstrap:AdminEmail"] = AdminEmail,
            ["Bootstrap:AdminPassword"] = AdminPassword,
            ["Storage:CreateBucketIfMissing"] = "false",
            // The worker is off in tests; they drive the reminder service directly with a time they choose.
            ["Reminders:Enabled"] = "false",
            ["RateLimits:AuthPerMinute"] = "10000",
            ["RateLimits:EmailPer15Minutes"] = "10000",
        }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IEmailSender>(Emails);
            services.AddSingleton<IObjectStorage, FakeObjectStorage>();
        });
    }

    /// <summary>A client that keeps cookies (the refresh token), over https so Secure cookies are sent.</summary>
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The token from the newest email to this address whose link contains the path.</summary>
    public string LinkToken(string to, string path)
    {
        var mail = Sent.Reverse().FirstOrDefault(m => m.To == to && m.TextBody.Contains(path))
            ?? throw new InvalidOperationException($"No email to {to} with a {path} link.");
        return Regex.Match(mail.TextBody, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
    }
}

public class FakeObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new();

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        _objects[key] = ms.ToArray();
    }

    public Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(_objects[key]));
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_objects.TryRemove(key, out _));
    public string GetDownloadUrl(string key, TimeSpan lifetime, string? downloadFileName = null) => $"https://storage.test/{key}";
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}

/// <summary>RFC 6238 codes, as an authenticator app would show them.</summary>
public static class Totp
{
    public static string Code(string formattedKey, DateTime? atUtc = null)
    {
        var key = Base32Decode(formattedKey.Replace(" ", string.Empty).ToUpperInvariant());
        var step = new DateTimeOffset(atUtc ?? DateTime.UtcNow).ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in input.TrimEnd('='))
        {
            value = (value << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}

// ─────────────────────────── Response shapes used by the tests ───────────────────────────

public record SessionUserDto(Guid Id, string Email, string FirstName, string LastName, List<string> Roles, bool IsStaff, bool EmailVerified, bool MfaEnabled);
public record SessionDto(string AccessToken, DateTime AccessTokenExpiresAtUtc, SessionUserDto User, bool MfaSatisfied, bool MfaSetupRequired);
public record LoginDto(bool MfaRequired, string? MfaChallengeToken, SessionDto? Session);
public record MeDto(Guid Id, string Email, string FirstName, string LastName, List<string> Roles, bool IsStaff, bool EmailVerified, bool MfaEnabled, bool MfaSatisfied, bool MfaSetupRequired, Guid? CandidateProfileId);
public record MfaSetupDto(string SharedKey, string AuthenticatorUri);
public record RecoveryCodesDto(List<string> RecoveryCodes);
public record StaffDto(Guid Id, string Email, List<string> Roles, bool MfaEnabled, bool IsDisabled, bool InvitationPending);
public record ProfileDto(Guid Id, Guid UserId, string FirstName, string LastName, string Email, string? Phone);
public record PageDto<T>(List<T> Items, int Page, int PageSize, int Total);
public record AuditDto(long Id, string Action, string Outcome, Guid? ActorUserId, string? ActorEmail, string? EntityType, string? EntityId, string? Details);
public record ProblemDto(int Status, string? Title, string? Detail);

public static class ClientExtensions
{
    public static void UseToken(this HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static async Task<T> Read<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static async Task<string?> Detail(this HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<ProblemDto>())?.Detail;
}
