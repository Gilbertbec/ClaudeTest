using System.Net;
using FluentAssertions;
using Xunit;

namespace KeyVaultFunction.Tests;

/// <summary>
/// Integration tests for the deployed GetSecret Azure Function.
/// These tests make real HTTP calls to the deployed function endpoint.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
public class GetSecretIntegrationTests
{
    private static readonly string FunctionBaseUrl =
        Environment.GetEnvironmentVariable("FUNCTION_BASE_URL")
        ?? "https://func-kvfuncapp.azurewebsites.net/api/GetSecret";

    private static readonly string FunctionKey =
        Environment.GetEnvironmentVariable("FUNCTION_KEY")
        ?? throw new InvalidOperationException(
            "Set the FUNCTION_KEY environment variable before running integration tests. " +
            "Get it with: az functionapp function keys list --name func-kvfuncapp --resource-group rg-kvfuncapp-cc --function-name GetSecret --query default -o tsv");

    private readonly HttpClient _httpClient;

    public GetSecretIntegrationTests()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSecret_WithValidSecretName_ReturnsExpectedValue()
    {
        // Arrange
        const string secretName = "MySecret";
        const string expectedValue = "Hello from Key Vault";
        var url = BuildUrl(secretName);

        // Act
        var response = await _httpClient.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be(expectedValue);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSecret_WithMissingSecretName_ReturnsBadRequest()
    {
        // Arrange - no secretName query parameter
        var url = $"{FunctionBaseUrl}?code={FunctionKey}";

        // Act
        var response = await _httpClient.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Please pass a 'secretName' query parameter");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSecret_WithEmptySecretName_ReturnsBadRequest()
    {
        // Arrange
        var url = BuildUrl("");

        // Act
        var response = await _httpClient.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Please pass a 'secretName' query parameter");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSecret_WithNonExistentSecret_ReturnsInternalServerError()
    {
        // Arrange
        const string nonExistentSecret = "ThisSecretDoesNotExist12345";
        var url = BuildUrl(nonExistentSecret);

        // Act
        var response = await _httpClient.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var content = await response.Content.ReadAsStringAsync();
        // The response should contain exception details
        content.Should().NotBeNullOrEmpty();
        // Typically Azure Key Vault returns a 404 which gets wrapped in RequestFailedException
        content.Should().Contain("RequestFailedException");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSecret_WithInvalidFunctionKey_ReturnsUnauthorized()
    {
        // Arrange
        const string secretName = "MySecret";
        var url = $"{FunctionBaseUrl}?code=InvalidKey123&secretName={secretName}";

        // Act
        var response = await _httpClient.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSecret_ReturnsJsonContentType()
    {
        // Arrange
        const string secretName = "MySecret";
        var url = BuildUrl(secretName);

        // Act
        var response = await _httpClient.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSecret_MultipleRequests_AllSucceed()
    {
        // Arrange
        const string secretName = "MySecret";
        const string expectedValue = "Hello from Key Vault";
        var url = BuildUrl(secretName);

        // Act - make 5 concurrent requests
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _httpClient.GetAsync(url))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert - all requests should succeed
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Be(expectedValue);
        }
    }

    /// <summary>
    /// Builds the complete URL with function key and secret name.
    /// </summary>
    private static string BuildUrl(string secretName)
    {
        return $"{FunctionBaseUrl}?code={FunctionKey}&secretName={Uri.EscapeDataString(secretName)}";
    }
}
