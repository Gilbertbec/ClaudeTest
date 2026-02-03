# KeyVaultFunction.Tests

Comprehensive test suite for the KeyVaultFunction Azure Function project.

## Test Coverage

### Unit Tests (8 tests)
Located in `GetSecretTests.cs` - tests the function logic in isolation using mocked dependencies.

**Test Categories:**
- **Input Validation** (3 tests):
  - Missing secretName parameter returns 400
  - Empty secretName parameter returns 400
  - Whitespace secretName parameter returns 400

- **Success Scenarios** (1 test):
  - Valid secretName returns 200 with secret value

- **Error Handling** (2 tests):
  - RequestFailedException returns 500 with exception details
  - Generic Exception returns 500 with exception details

- **Logging** (2 tests):
  - Information log when retrieving secret
  - Error log when exception occurs

### Integration Tests (7 tests)
Located in `GetSecretIntegrationTests.cs` - tests against the deployed Azure Function endpoint.

**Test Categories:**
- **Success Scenarios** (2 tests):
  - Valid secret returns expected value
  - Multiple concurrent requests succeed

- **Input Validation** (2 tests):
  - Missing secretName returns 400
  - Empty secretName returns 400

- **Error Handling** (2 tests):
  - Non-existent secret returns 500
  - Invalid function key returns 401

- **Response Format** (1 test):
  - Validates content type

## Running Tests

### Run All Tests
```bash
dotnet test
```

### Run Unit Tests Only
```bash
dotnet test --filter "Category!=Integration"
```

### Run Integration Tests Only
```bash
dotnet test --filter "Category=Integration"
```

### Run with Detailed Output
```bash
dotnet test --logger "console;verbosity=detailed"
```

## Test Configuration

### Integration Test Endpoint
- **URL**: `https://func-kvfuncapp.azurewebsites.net/api/GetSecret`
- **Function Key**: Stored in `GetSecretIntegrationTests.cs`
- **Test Secret**: `MySecret` (expected value: "Hello from Key Vault")

## Dependencies

- **xUnit**: Test framework
- **Moq**: Mocking library for unit tests
- **FluentAssertions**: Assertion library for readable test assertions
- **Microsoft.NET.Test.Sdk**: .NET test SDK

## Test Structure

```
KeyVaultFunction.Tests/
├── GetSecretTests.cs              # Unit tests (mocked dependencies)
├── GetSecretIntegrationTests.cs   # Integration tests (live endpoint)
├── KeyVaultFunction.Tests.csproj  # Test project file
└── README.md                      # This file
```

## CI/CD Recommendations

In CI/CD pipelines, you may want to run unit and integration tests separately:

1. **Build & Unit Tests** (fast feedback):
   ```bash
   dotnet build
   dotnet test --filter "Category!=Integration" --no-build
   ```

2. **Integration Tests** (after deployment):
   ```bash
   dotnet test --filter "Category=Integration" --no-build
   ```

## Test Results

All 15 tests currently pass:
- 8 unit tests
- 7 integration tests

Total execution time: ~6-7 seconds (including integration test network calls)
