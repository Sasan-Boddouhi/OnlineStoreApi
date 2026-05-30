# Testing Guide

This document describes the testing strategy, project structure, execution commands, code coverage workflow, and conventions used in the OnlineStore solution.

---

# Test Projects

The solution contains three dedicated testing projects:

| Project                           | Purpose                                                                                                          |
| --------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| **OnlineStore.Tests.Unit**        | Fast unit tests with mocked dependencies. No database or external infrastructure is involved.                    |
| **OnlineStore.Tests.Integration** | Integration tests using a real ASP.NET Core pipeline, `WebApplicationFactory`, and an in-memory SQLite database. |
| **OnlineStore.Tests.Shared**      | Shared test utilities, builders, constants, fixtures, and reusable helpers.                                      |

---

# Running Tests

Run all tests:

```bash
dotnet test
```

Run only unit tests:

```bash
dotnet test OnlineStore.Tests.Unit
```

Run only integration tests:

```bash
dotnet test OnlineStore.Tests.Integration
```

---

# Code Coverage

A helper script named **RunCoverage.ps1** is available in the solution root.

The script automatically:

1. Removes old coverage results.
2. Executes all tests with coverage collection enabled.
3. Generates an HTML coverage report.
4. Opens the report in the default browser.

Run the script:

```powershell
.\RunCoverage.ps1
```

If PowerShell blocks script execution:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

---

# Coverage Script

```powershell
# RunCoverage.ps1

Write-Host "Cleaning old coverage data..." -ForegroundColor Cyan
Get-ChildItem -Recurse -Directory -Filter "TestResults" |
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Running tests with coverage..." -ForegroundColor Cyan
dotnet test --collect:"XPlat Code Coverage"

Write-Host "Generating report..." -ForegroundColor Cyan
reportgenerator `
    -reports:"**/coverage.cobertura.xml" `
    -targetdir:"coveragereport" `
    -reporttypes:Html

Write-Host "Coverage report generated." -ForegroundColor Green
start coveragereport/index.html
```

Install ReportGenerator if it is not already available:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
```

---

# Testing Stack

The following tools and libraries are used throughout the test suite:

* **xUnit** – Test framework
* **FluentAssertions** – Readable and expressive assertions
* **Moq** – Dependency mocking
* **WebApplicationFactory** – ASP.NET Core integration testing
* **SQLite In-Memory** – Relational database for integration tests
* **ReportGenerator** – Coverage report generation

---

# Naming Conventions

## Test Classes

Use:

```csharp
<SUT>Tests
```

Examples:

```csharp
ProductServiceTests
OrderServiceTests
AuthControllerTests
```

---

## Test Methods

Use:

```csharp
MethodName_Scenario_ExpectedBehavior
```

Examples:

```csharp
CreateProduct_EmptyName_ThrowsValidationException

Login_InvalidCredentials_ReturnsUnauthorized

UpdateProduct_ValidRequest_UpdatesEntity
```

---

# Adding New Tests

## Unit Tests

1. Create a test class inside `OnlineStore.Tests.Unit`.
2. Mock all external dependencies using Moq.
3. Instantiate the System Under Test (SUT).
4. Verify behavior using FluentAssertions.

## Integration Tests

1. Create the test class inside `OnlineStore.Tests.Integration`.
2. Inherit from:

   * `BaseIntegrationTest` for service tests.
   * `ControllerIntegrationTestBase` for API/controller tests.
3. Add required seed data if necessary.
4. Execute requests against the test server.
5. Verify both response behavior and persisted data.

---

# Common Issues

| Problem                               | Cause                                            | Solution                                                                       |
| ------------------------------------- | ------------------------------------------------ | ------------------------------------------------------------------------------ |
| `CS9035 Required member`              | Required properties were not initialized.        | Populate all required members.                                                 |
| `RowVersion` failures                 | SQLite does not auto-generate row versions.      | Ensure `TestAppDbContext` and `SqliteRowVersionFixInterceptor` are registered. |
| `TransactionIgnoredWarning`           | InMemory provider does not support transactions. | Use SQLite in-memory provider.                                                 |
| `Scope not initialized`               | Service resolution performed inside constructor. | Resolve services lazily after scope creation.                                  |
| `ArgumentNullException` for `Jwt:Key` | JWT configuration missing in tests.              | Use `IntegrationTestFactory` default configuration.                            |

---

# Architecture Notes

## IntegrationTestFactory

`IntegrationTestFactory<Program>` replaces the production database configuration with an in-memory SQLite database and configures the application for testing.

## TestAppDbContext

A specialized DbContext used only during testing. It disables automatic RowVersion generation behavior that is unsupported by SQLite.

## SqliteRowVersionFixInterceptor

Automatically assigns a default RowVersion value before entity persistence.

## TestDataSeed

Provides initial test data such as:

* Administrator user
* Customer user
* Product categories
* Shared reference data

---

# CI/CD Example

```yaml
- name: Run Tests
  run: dotnet test --collect:"XPlat Code Coverage"

- name: Generate Coverage Report
  run: |
    reportgenerator \
      -reports:"**/coverage.cobertura.xml" \
      -targetdir:coveragereport \
      -reporttypes:Html

- name: Upload Coverage Report
  uses: actions/upload-artifact@v4
  with:
    name: coverage-report
    path: coveragereport
```

---

# Best Practices

* Keep unit tests isolated and deterministic.
* Prefer integration tests for validating application behavior across layers.
* Test observable behavior rather than implementation details.
* Avoid unnecessary mocking.
* Seed only the data required for the specific test scenario.
* Ensure tests can run independently and in any order.
* Maintain high coverage for critical business logic.

---

# Useful Resources

* FluentAssertions Documentation
  https://fluentassertions.com/

* Moq Documentation
  https://github.com/moq/moq

* ASP.NET Core Integration Testing
  https://learn.microsoft.com/aspnet/core/test/integration-tests

* ReportGenerator
  https://reportgenerator.io/
