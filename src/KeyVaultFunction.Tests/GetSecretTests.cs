using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KeyVaultFunction.Tests;

/// <summary>
/// Unit tests for the GetSecret Azure Function.
/// These tests use mocked dependencies to verify function behavior in isolation.
/// </summary>
public class GetSecretTests
{
    private readonly Mock<SecretClient> _mockSecretClient;
    private readonly Mock<ILogger<GetSecret>> _mockLogger;
    private readonly GetSecret _function;

    public GetSecretTests()
    {
        // Mock SecretClient - note: we need to pass null parameters to the constructor
        // since SecretClient doesn't have a parameterless constructor
        _mockSecretClient = new Mock<SecretClient>();
        _mockLogger = new Mock<ILogger<GetSecret>>();
        _function = new GetSecret(_mockSecretClient.Object, _mockLogger.Object);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_WithMissingSecretName_ReturnsBadRequest()
    {
        // Arrange
        var mockRequest = CreateMockHttpRequest(secretName: null);

        // Act
        var result = await _function.Run(mockRequest.Object);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequestResult = (BadRequestObjectResult)result;
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequestResult.Value.Should().Be("Please pass a 'secretName' query parameter.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_WithEmptySecretName_ReturnsBadRequest()
    {
        // Arrange
        var mockRequest = CreateMockHttpRequest(secretName: "");

        // Act
        var result = await _function.Run(mockRequest.Object);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequestResult = (BadRequestObjectResult)result;
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequestResult.Value.Should().Be("Please pass a 'secretName' query parameter.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_WithWhitespaceSecretName_ReturnsBadRequest()
    {
        // Arrange
        var mockRequest = CreateMockHttpRequest(secretName: "   ");

        // Act
        var result = await _function.Run(mockRequest.Object);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequestResult = (BadRequestObjectResult)result;
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequestResult.Value.Should().Be("Please pass a 'secretName' query parameter.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_WithValidSecretName_ReturnsOkWithSecretValue()
    {
        // Arrange
        const string secretName = "MySecret";
        const string secretValue = "SuperSecretValue123";
        var mockRequest = CreateMockHttpRequest(secretName);

        var mockSecret = SecretModelFactory.KeyVaultSecret(
            properties: SecretModelFactory.SecretProperties(name: secretName),
            value: secretValue
        );

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(secretName, null, default))
            .ReturnsAsync(Response.FromValue(mockSecret, Mock.Of<Response>()));

        // Act
        var result = await _function.Run(mockRequest.Object);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be(secretValue);

        // Verify SecretClient was called with correct parameter
        _mockSecretClient.Verify(
            x => x.GetSecretAsync(secretName, null, default),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_WhenSecretClientThrowsRequestFailedException_Returns500WithExceptionDetails()
    {
        // Arrange
        const string secretName = "NonExistentSecret";
        var mockRequest = CreateMockHttpRequest(secretName);

        var exception = new RequestFailedException(404, "Secret not found");
        _mockSecretClient
            .Setup(x => x.GetSecretAsync(secretName, null, default))
            .ThrowsAsync(exception);

        // Act
        var result = await _function.Run(mockRequest.Object);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)result;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        objectResult.Value.Should().NotBeNull();
        var errorMessage = objectResult.Value?.ToString() ?? string.Empty;
        errorMessage.Should().Contain("RequestFailedException");
        errorMessage.Should().Contain("Secret not found");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_WhenSecretClientThrowsGenericException_Returns500WithExceptionDetails()
    {
        // Arrange
        const string secretName = "ProblematicSecret";
        var mockRequest = CreateMockHttpRequest(secretName);

        var exception = new InvalidOperationException("Something went wrong");
        _mockSecretClient
            .Setup(x => x.GetSecretAsync(secretName, null, default))
            .ThrowsAsync(exception);

        // Act
        var result = await _function.Run(mockRequest.Object);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)result;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        objectResult.Value.Should().NotBeNull();
        var errorMessage = objectResult.Value?.ToString() ?? string.Empty;
        errorMessage.Should().Contain("InvalidOperationException");
        errorMessage.Should().Contain("Something went wrong");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_LogsInformationWhenRetrievingSecret()
    {
        // Arrange
        const string secretName = "MySecret";
        const string secretValue = "SecretValue";
        var mockRequest = CreateMockHttpRequest(secretName);

        var mockSecret = SecretModelFactory.KeyVaultSecret(
            properties: SecretModelFactory.SecretProperties(name: secretName),
            value: secretValue
        );

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(secretName, null, default))
            .ReturnsAsync(Response.FromValue(mockSecret, Mock.Of<Response>()));

        // Act
        await _function.Run(mockRequest.Object);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
#pragma warning disable CS8602 // Dereference of a possibly null reference - safe in test context
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Retrieving secret '{secretName}'")),
#pragma warning restore CS8602
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Run_LogsErrorWhenExceptionOccurs()
    {
        // Arrange
        const string secretName = "FailingSecret";
        var mockRequest = CreateMockHttpRequest(secretName);

        var exception = new InvalidOperationException("Test exception");
        _mockSecretClient
            .Setup(x => x.GetSecretAsync(secretName, null, default))
            .ThrowsAsync(exception);

        // Act
        await _function.Run(mockRequest.Object);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
#pragma warning disable CS8602 // Dereference of a possibly null reference - safe in test context
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Failed to retrieve secret '{secretName}'")),
#pragma warning restore CS8602
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Creates a mock HttpRequest with the specified secret name query parameter.
    /// </summary>
    private static Mock<HttpRequest> CreateMockHttpRequest(string? secretName)
    {
        var mockRequest = new Mock<HttpRequest>();
        var queryCollection = new QueryCollection(
            secretName == null
                ? new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()
                : new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    { "secretName", secretName }
                });

        mockRequest.Setup(x => x.Query).Returns(queryCollection);
        return mockRequest;
    }
}
