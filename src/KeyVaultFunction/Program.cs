using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        var vaultUri = Environment.GetEnvironmentVariable("KEY_VAULT_URI")
            ?? throw new InvalidOperationException("KEY_VAULT_URI environment variable is not set.");

        services.AddSingleton(new SecretClient(new Uri(vaultUri), new DefaultAzureCredential()));
    })
    .Build();

host.Run();
