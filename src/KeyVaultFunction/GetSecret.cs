using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace KeyVaultFunction;

public class GetSecret
{
    private readonly SecretClient _secretClient;
    private readonly ILogger<GetSecret> _logger;

    public GetSecret(SecretClient secretClient, ILogger<GetSecret> logger)
    {
        _secretClient = secretClient;
        _logger = logger;
    }

    [Function("GetSecret")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
    {
        string? secretName = req.Query["secretName"];

        if (string.IsNullOrWhiteSpace(secretName))
        {
            return new BadRequestObjectResult("Please pass a 'secretName' query parameter.");
        }

        try
        {
            _logger.LogInformation("Retrieving secret '{SecretName}' from Key Vault.", secretName);
            KeyVaultSecret secret = await _secretClient.GetSecretAsync(secretName);
            return new OkObjectResult(secret.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret '{SecretName}'.", secretName);
            return new ObjectResult(ex.ToString()) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }
}
