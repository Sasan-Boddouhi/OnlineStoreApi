# 🛒 Online Store API

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF%20Core-8.0-512BD4?logo=entity-framework)](https://docs.microsoft.com/ef/core/)
[![Swagger](https://img.shields.io/badge/API-Swagger-85EA2D?logo=swagger)](https://swagger.io/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

A modern, scalable and maintainable e-commerce REST API built with **ASP.NET Core 8**, **Clean Architecture**, **Specification Pattern**, and a unified dynamic query pipeline.

The project demonstrates enterprise-grade backend development practices including authentication, authorization, validation, structured logging, caching, testing, and flexible querying.

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

# 🏗️ Architecture

```text
┌─────────────────────────────────────────┐
│              Presentation               │
│   Controllers, Middleware, Program.cs   │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│             BusinessLogic               │
│ Services, DTOs, Validators, Mappings    │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│              Application                │
│ Entities, Contracts, Specifications     │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│               DataLayer                 │
│ DbContext, Repositories, Persistence    │
└─────────────────────────────────────────┘
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

Dependencies always flow inward. Outer layers may depend on inner layers, but never the opposite.

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

This pipeline enables strongly-typed filtering, sorting and paging while keeping controllers and services clean.

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
├── BusinessLogic
├── DataLayer
└── OnlineStore.API

tests/
├── OnlineStore.Tests.Unit
├── OnlineStore.Tests.Integration
└── OnlineStore.Tests.Shared
```

---

# 🧪 Testing

The solution contains a complete testing infrastructure.

| Project                       | Purpose                                  |
| ----------------------------- | ---------------------------------------- |
| OnlineStore.Tests.Unit        | Fast isolated unit tests                 |
| OnlineStore.Tests.Integration | End-to-end application integration tests |
| OnlineStore.Tests.Shared      | Shared test utilities and fixtures       |

### Testing Stack

* xUnit
* FluentAssertions
* Moq
* WebApplicationFactory
* SQLite In-Memory
* ReportGenerator

For detailed testing documentation see:

```text
TESTING.md
```

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

Example validation error:

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

### Backend

* .NET 8
* ASP.NET Core Web API
* Entity Framework Core 8
* SQL Server

### Libraries

* AutoMapper
* FluentValidation
* Serilog
* BCrypt.Net
* JWT Bearer Authentication

### Documentation

* Swagger / OpenAPI

### Caching

* IMemoryCache

### Testing

* xUnit
* Moq
* FluentAssertions
* SQLite In-Memory

---

# 🚀 Getting Started

## Clone Repository

```bash
git clone https://github.com/Sasan-Boddouhi/OnlineStoreApi.git
cd OnlineStoreApi
```

---

## Restore Packages

```bash
dotnet restore
```

---

## Configure Database

Update your connection string:

```json
{
  "ConnectionStrings": {
    "SQLServer": "Data Source=YOUR_SERVER;Initial Catalog=ShopDB;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

---

## Apply Migrations

```bash
dotnet ef database update
```

---

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

Future improvements may include:

* Redis Distributed Cache
* Docker Support
* Kubernetes Deployment
* CQRS + MediatR
* OpenTelemetry
* API Versioning
* Rate Limiting
* Distributed Tracing
* Health Checks Dashboard

---

# 🤝 Contributing

Contributions, issues and feature requests are welcome.

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
