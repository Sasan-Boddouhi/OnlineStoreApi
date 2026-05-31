# 🛒 Online Store API

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF%20Core-8.0-512BD4?logo=entity-framework)](https://docs.microsoft.com/ef/core/)
[![Swagger](https://img.shields.io/badge/API-Swagger-85EA2D?logo=swagger)](https://swagger.io/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![CI](https://github.com/Sasan-Boddouhi/OnlineStoreApi/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Sasan-Boddouhi/OnlineStoreApi/actions/workflows/dotnet.yml)

A production-style e-commerce REST API built with **ASP.NET Core 8**, **Clean Architecture**, and the **Specification Pattern**, showcasing advanced querying, authentication, testing, and maintainable enterprise application design.

The project demonstrates modern backend engineering practices including authentication, authorization, validation, structured logging, caching, testing, and a reusable dynamic query pipeline.

---

# 📸 Swagger UI

![Swagger UI](images/swagger.png)

---

# ✨ Features

## Architecture

* Clean Architecture
* Layered Separation of Concerns
* Dependency Inversion
* Repository Pattern
* Unit of Work Pattern
* Specification Pattern

## Querying

* Dynamic Query Pipeline
* Type-safe Fluent Query DSL
* Advanced Filtering
* Sorting
* Pagination
* Projection-first Querying

## Security

* JWT Authentication
* Refresh Token Rotation
* BCrypt Password Hashing
* Session Management
* Login Lockout Protection
* Input Validation

## Performance

* Projection-first Queries
* AsNoTracking Read Operations
* Memory Caching
* Query Normalization
* Deferred IQueryable Execution
* Reusable Specifications
* N+1 Query Prevention

## Observability

* Structured Logging with Serilog
* Query Metrics Middleware
* Request Monitoring

## Developer Experience

* Swagger / OpenAPI
* FluentValidation
* AutoMapper
* Full Async Support
* CancellationToken Support
* Comprehensive Testing Infrastructure

---

# 📊 Key Highlights

* Clean Architecture implementation
* Dynamic Query DSL
* Specification Pattern
* JWT Authentication
* Refresh Token Rotation
* Comprehensive Testing
* GitHub Actions CI
* Structured Logging
* SQLite-backed Integration Testing
* Reusable Query Infrastructure

---

# 🏗️ Architecture

```text
┌─────────────────────────────┐
│        Presentation         │
│ Controllers, Middleware     │
└──────────────┬──────────────┘
               │
               ▼
┌─────────────────────────────┐
│       BusinessLogic         │
│ Services, DTOs, Validators  │
└──────────────┬──────────────┘
               │
               ▼
┌─────────────────────────────┐
│        Application          │
│ Entities, Contracts, Specs  │
└──────────────┬──────────────┘
               │
               ▼
┌─────────────────────────────┐
│         DataLayer           │
│ EF Core, Repositories       │
└─────────────────────────────┘
```

### Dependency Flow

```text
Presentation
     ↓
BusinessLogic
     ↓
Application
     ↓
DataLayer
```

All dependencies flow inward to preserve separation of concerns, maintainability, and testability.

---

# 🔄 Unified Query Pipeline

```text
Fluent DSL / Query String
            │
            ▼
     StringQueryParser
            │
            ▼
      QueryContract<T>
            │
            ▼
         QueryPolicy
            │
            ▼
          ToSpec()
            │
            ▼
           Spec<T>
            │
            ▼
      Repository / EF Core
```

The query pipeline converts query-string expressions into strongly typed specifications, enabling reusable filtering, sorting, paging, and projection while keeping controllers and services free from query logic.

---

# 📖 Query Examples

## Filtering

```http
GET /api/products?filter=price gt 1000
```

## Sorting

```http
GET /api/products?sort=-price,name
```

## Pagination

```http
GET /api/products?page=1&size=10
```

## Combined Query

```http
GET /api/products?filter=price gt 1000 and category.name eq 'electronics'&sort=-price&page=2&size=10
```

---

# 📂 Project Structure

```text
src/
├── Application
│   ├── Entities
│   ├── Contracts
│   └── Specifications
│
├── BusinessLogic
│   ├── Services
│   ├── DTOs
│   ├── Validators
│   └── Mappings
│
├── DataLayer
│   ├── Context
│   ├── Repositories
│   └── Persistence
│
└── OnlineStore.API
    ├── Controllers
    ├── Middleware
    └── Configuration

tests/
├── OnlineStore.Tests.Unit
├── OnlineStore.Tests.Integration
└── OnlineStore.Tests.Shared
```

---

# 🧪 Testing

The solution includes a comprehensive automated testing infrastructure.

| Project                       | Purpose                       |
| ----------------------------- | ----------------------------- |
| OnlineStore.Tests.Unit        | Fast isolated unit tests      |
| OnlineStore.Tests.Integration | End-to-end integration tests  |
| OnlineStore.Tests.Shared      | Shared fixtures and utilities |

### Testing Highlights

* 300+ automated tests
* GitHub Actions CI validation
* WebApplicationFactory integration suite
* SQLite In-Memory testing
* Coverage reporting via ReportGenerator

For detailed testing architecture and coverage workflow see:

[TESTING.md](TESTING.md)

---

# 🔄 Continuous Integration

GitHub Actions automatically:

* Restore dependencies
* Build the solution
* Execute all tests
* Generate coverage artifacts
* Fail the pipeline on test failures

Every pull request is validated before merging.

---

# ⚡ Performance Considerations

The application includes several performance optimizations:

* Projection-first querying
* AsNoTracking read operations
* Query normalization
* Memory caching
* Deferred execution
* Reusable specifications
* Efficient LINQ translation
* Prevention of N+1 database queries

---

# 🔒 Security

Implemented security features include:

* JWT Authentication
* Refresh Token Rotation
* Session Tracking
* BCrypt Password Hashing
* Login Lockout Protection
* Request Validation
* Secure Token Lifecycle Management

---

# 📡 Error Response Format

```json
{
  "success": false,
  "message": "Validation failed",
  "errors": [
    {
      "code": "invalid_phone_number",
      "target": "phoneNumber",
      "message": "شماره موبایل معتبر نیست"
    }
  ]
}
```

---

# 🛠️ Technology Stack

## Platform

* .NET 8
* ASP.NET Core Web API
* Entity Framework Core 8
* SQL Server

## Architecture

* Clean Architecture
* Repository Pattern
* Unit of Work Pattern
* Specification Pattern

## Security

* JWT Bearer Authentication
* Refresh Token Rotation
* BCrypt Password Hashing

## Validation & Mapping

* FluentValidation
* AutoMapper

## Observability

* Serilog

## Testing

* xUnit
* Moq
* FluentAssertions
* WebApplicationFactory
* SQLite In-Memory
* ReportGenerator

## Documentation

* Swagger / OpenAPI

---

# 🚀 Getting Started

## Clone Repository

```bash
git clone https://github.com/Sasan-Boddouhi/OnlineStoreApi.git
cd OnlineStoreApi
```

## Restore Packages

```bash
dotnet restore
```

## Configure Database

```json
{
  "ConnectionStrings": {
    "SQLServer": "Data Source=YOUR_SERVER;Initial Catalog=ShopDB;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

## Apply Migrations

```bash
dotnet ef database update
```

## Run Application

```bash
dotnet run
```

---

# 📚 API Documentation

After starting the application:

```text
https://localhost:7076/swagger
```

Swagger/OpenAPI documentation is generated automatically.

---

# 🔮 Roadmap

Planned future enhancements:

* Redis Distributed Cache
* Docker Containerization
* Kubernetes Deployment
* CQRS + MediatR
* OpenTelemetry
* API Versioning
* Rate Limiting
* Distributed Tracing
* Health Checks
* Background Processing

---

# 🤝 Contributing

Contributions, issues, and feature requests are welcome.

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Open a Pull Request

---

# 📄 License

This project is licensed under the MIT License.

See the LICENSE file for details.

---

# 🙏 Acknowledgements

Special thanks to the teams behind:

* ASP.NET Core
* Entity Framework Core
* AutoMapper
* FluentValidation
* Serilog
* Swagger

---

Built with ❤️ by Sasan Boddouhi
