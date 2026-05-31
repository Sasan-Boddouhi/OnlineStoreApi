# Testing Guide

[![CI](https://github.com/Sasan-Boddouhi/OnlineStoreApi/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Sasan-Boddouhi/OnlineStoreApi/actions/workflows/dotnet.yml)

This document describes the testing architecture, execution workflow, coverage reporting, and testing conventions used throughout the OnlineStore solution.

---

# Testing Philosophy

The testing strategy follows a layered approach:

```text
Unit Tests
    ↓
Integration Tests
    ↓
End-to-End Application Behavior
```

The goal is to verify:

* Business rules in isolation
* Service interactions
* Persistence behavior
* API endpoints
* Authentication and authorization flows
* Complete request pipelines

---

# Test Projects

The solution contains three dedicated testing projects.

| Project                       | Responsibility                                                       |
| ----------------------------- | -------------------------------------------------------------------- |
| OnlineStore.Tests.Unit        | Fast isolated unit tests using mocked dependencies                   |
| OnlineStore.Tests.Integration | Full application integration tests using ASP.NET Core infrastructure |
| OnlineStore.Tests.Shared      | Shared fixtures, builders, helpers, constants, and test utilities    |

---

# Testing Stack

The following technologies are used across the test suite:

| Tool                  | Purpose                     |
| --------------------- | --------------------------- |
| xUnit                 | Test framework              |
| FluentAssertions      | Readable assertions         |
| Moq                   | Dependency mocking          |
| WebApplicationFactory | Integration testing         |
| SQLite In-Memory      | Relational database testing |
| ReportGenerator       | Coverage reporting          |

---

# Test Architecture

## Unit Tests

Unit tests validate business logic independently from infrastructure concerns.

Characteristics:

* No database access
* No web server
* Mocked dependencies
* Fast execution
* Deterministic behavior

Example:

```text
Service
 ├─ Repository (Mock)
 ├─ Cache (Mock)
 └─ Logger (Mock)
```

---

## Integration Tests

Integration tests execute against a real ASP.NET Core application pipeline.

Characteristics:

* Real dependency injection
* Real middleware pipeline
* Real EF Core execution
* SQLite in-memory database
* Authentication testing
* End-to-end request validation

Example:

```text
HTTP Request
      ↓
Middleware
      ↓
Controller
      ↓
Service
      ↓
Repository
      ↓
SQLite In-Memory
```

---

# Running Tests

Run the entire test suite:

```bash
dotnet test
```

Run unit tests only:

```bash
dotnet test tests/OnlineStore.Tests.Unit
```

Run integration tests only:

```bash
dotnet test tests/OnlineStore.Tests.Integration
```

Run tests with detailed output:

```bash
dotnet test --logger "console;verbosity=detailed"
```

---

# Code Coverage

Coverage reports are generated using the built-in .NET coverage collector together with ReportGenerator.

Install ReportGenerator:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
```

Generate coverage:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Create HTML report:

```bash
reportgenerator \
-reports:"**/coverage.cobertura.xml" \
-targetdir:"coveragereport" \
-reporttypes:Html
```

Open:

```text
coveragereport/index.html
```

---

# Coverage Automation

A helper script is provided:

```powershell
.\RunCoverage.ps1
```

The script:

1. Removes old coverage artifacts
2. Executes all tests
3. Collects coverage
4. Generates HTML reports
5. Opens the generated report

---

# Naming Conventions

## Test Classes

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

```csharp
MethodName_Scenario_ExpectedResult
```

Examples:

```csharp
CreateProduct_InvalidName_ThrowsValidationException

Login_InvalidCredentials_ReturnsUnauthorized

UpdateProduct_ValidRequest_UpdatesEntity
```

---

# Shared Testing Infrastructure

## IntegrationTestFactory

Provides a customized application host for integration testing.

Responsibilities:

* Creates the test server
* Configures test services
* Replaces production database configuration
* Seeds required data
* Provides authenticated test clients

---

## Test Database

Integration tests use SQLite In-Memory.

Benefits:

* Fast execution
* Relational behavior
* Transaction support
* Deterministic results
* No external dependencies

---

## Shared Utilities

The shared testing project contains:

* Test builders
* Common assertions
* Seed data
* Test constants
* Reusable fixtures
* Authentication helpers

---

# Continuous Integration

All tests execute automatically through GitHub Actions.

CI pipeline responsibilities:

1. Restore dependencies
2. Build solution
3. Execute all tests
4. Generate coverage artifacts
5. Fail the build on test failures

---

# Best Practices

* Keep unit tests focused on a single behavior.
* Prefer testing observable behavior over implementation details.
* Avoid unnecessary mocking.
* Use integration tests for cross-layer validation.
* Seed only required data.
* Ensure tests are independent and repeatable.
* Keep test names descriptive and intention-revealing.
* Verify both successful and failure scenarios.

---

# Common Issues

| Issue                        | Solution                                |
| ---------------------------- | --------------------------------------- |
| Missing configuration values | Use IntegrationTestFactory defaults     |
| Required member exceptions   | Initialize all required properties      |
| Service resolution failures  | Resolve dependencies inside test scopes |
| Database state leakage       | Create isolated test data per test      |

---

# Resources

### xUnit

https://xunit.net

### FluentAssertions

https://fluentassertions.com

### Moq

https://github.com/moq/moq

### ASP.NET Core Integration Testing

https://learn.microsoft.com/aspnet/core/test/integration-tests

### ReportGenerator

https://reportgenerator.io

---

Testing is treated as a first-class concern in the OnlineStore solution to ensure correctness, maintainability, and long-term confidence during development.
