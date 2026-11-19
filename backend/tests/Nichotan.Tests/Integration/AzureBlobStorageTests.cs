using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nichotan.Infrastructure.Storage;

namespace Nichotan.Tests.Integration;

/// <summary>
/// The AzureBlob storage provider against the Azurite emulator (docker compose service "azurite", port 10010), so the code
/// production uses is exercised without an Azure account.
/// </summary>
public class AzureBlobStorageTests
{
    // Azurite's well-known development account; not a secret.
    private const string DevAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private static AzureBlobObjectStorage Create(string container) => new(
        Options.Create(new StorageOptions
        {
            Provider = "AzureBlob",
            Bucket = container,
            AzureBlob = new AzureBlobOptions
            {
                ConnectionString = $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={DevAccountKey};BlobEndpoint=http://127.0.0.1:10010/devstoreaccount1;",
            },
        }),
        NullLogger<AzureBlobObjectStorage>.Instance);

    [Fact]
    public async Task Stores_reads_signs_and_deletes_private_files()
    {
        var storage = Create($"test-{Guid.NewGuid():N}");

        (await storage.IsAvailableAsync()).Should().BeFalse("the container does not exist yet");
        await storage.EnsureBucketAsync();
        await storage.EnsureBucketAsync(); // safe to repeat
        (await storage.IsAvailableAsync()).Should().BeTrue();

        var bytes = "%PDF-1.4 hello"u8.ToArray();
        await storage.PutAsync("candidates/1/passport.pdf", new MemoryStream(bytes), "application/pdf");

        await using (var read = await storage.GetAsync("candidates/1/passport.pdf"))
        {
            var copy = new MemoryStream();
            await read.CopyToAsync(copy);
            copy.ToArray().Should().Equal(bytes);
        }

        var url = storage.GetDownloadUrl("candidates/1/passport.pdf", TimeSpan.FromMinutes(5), "My passport.pdf");
        url.Should().Contain("sig=").And.Contain("sp=r");

        using (var http = new HttpClient())
        {
            var signed = await http.GetAsync(url);
            signed.IsSuccessStatusCode.Should().BeTrue();
            signed.Content.Headers.ContentDisposition!.FileName.Should().Contain("My passport.pdf");

            // Without the signature the file is not reachable: the container is private.
            var anonymous = await http.GetAsync(url[..url.IndexOf('?')]);
            anonymous.IsSuccessStatusCode.Should().BeFalse();
        }

        await storage.DeleteAsync("candidates/1/passport.pdf");
        await storage.DeleteAsync("candidates/1/passport.pdf"); // deleting a missing file is not an error
        var gone = async () => await storage.GetAsync("candidates/1/passport.pdf");
        await gone.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void Needs_an_endpoint_or_a_connection_string()
    {
        var build = () => new AzureBlobObjectStorage(Options.Create(new StorageOptions { Provider = "AzureBlob" }), NullLogger<AzureBlobObjectStorage>.Instance);
        build.Should().Throw<InvalidOperationException>();
    }
}
