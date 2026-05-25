# 🛒 Online Store API

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF%20Core-8.0-512BD4?logo=entity-framework)](https://docs.microsoft.com/en-us/ef/core/)
[![Swagger](https://img.shields.io/badge/API-Swagger-85EA2D?logo=swagger)](https://swagger.io/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

A modern online store API built with ASP.NET Core, Clean Architecture, Specification Pattern, and a unified dynamic query pipeline.

---

# 📸 Swagger Preview

![Swagger UI](images/swagger.png)

---

# 🚀 Features

- Clean Architecture
- Specification Pattern (`Spec<T>`)
- Dynamic Query Pipeline
- Type-safe Fluent Query DSL
- JWT Authentication + Refresh Tokens
- Projection-first querying
- FluentValidation
- Structured Logging with Serilog
- Memory Caching
- Query Metrics Middleware
- Persian (Shamsi) Date Support
- Generic Repository + Unit of Work
- AutoMapper
- Full async & CancellationToken support

---

# 🧱 Architecture

```text
┌─────────────────────────────────────────┐
│            Presentation (API)           │
│  Controllers, Middleware, Program.cs    │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│              BusinessLogic              │
│  Services, Mappings, DTOs, Validations  │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│               Application               │
│  Entities, Interfaces, Specifications   │
└────────────────────┬────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────┐
│                DataLayer                │
│  DbContext, Repository, UnitOfWork      │
└─────────────────────────────────────────┘
```

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
          .ToSpec()
            │
            ▼
           Spec<T>
            │
            ▼
      Repository / EF Core
```

---

# 📖 Query Examples

## Filter

```http
GET /api/products?filter=price gt 1000
```

## Sorting

```http
GET /api/products?sort=-price,name
```

## Paging

```http
GET /api/products?page=1&size=10
```

## Combined Query

```http
GET /api/products?filter=price gt 1000 and category.name eq 'electronics'&sort=-price&page=2&size=10
```

---

# ⚡ Performance Optimizations

- Projection-first querying
- AsNoTracking for read-only queries
- Expression-based projections
- Query normalization
- IMemoryCache
- Deferred IQueryable execution
- Reusable Specifications
- Avoiding N+1 queries

---

# 🧪 Testing

Integration tests cover critical user scenarios and ensure system stability.

## Test Results

```text
OnlineStore.Tests.Integration
  Tests in group: 29
  Total Duration: 5.5 sec

Outcomes: ✅ 29 Passed

---

# 🔒 Security

- JWT Authentication
- Refresh Token Rotation
- BCrypt Password Hashing
- Session Tracking
- Login Lockout
- FluentValidation Input Validation

---

# 📡 Error Response Example

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

# 🛠️ Technologies

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core 8
- SQL Server
- AutoMapper
- FluentValidation
- Serilog
- JWT Bearer Authentication
- BCrypt.Net
- Swagger / OpenAPI
- IMemoryCache

---

# 🚀 Getting Started

## Clone Repository

```bash
git clone https://github.com/Sasan-Boddouhi/OnlineStoreApi.git
```

---

## Configure Connection String

```json
"ConnectionStrings": {
  "SQLServer": "Data Source=YOUR_SERVER;Initial Catalog=ShopDB;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

---

## Run Migrations

```powershell
Add-Migration InitialCreate
Update-Database
```

---

## Run Project

```bash
dotnet run
```

Swagger:

```text
https://localhost:7076/swagger
```

---

# 🔮 Roadmap

- Redis Distributed Cache
- Docker Support
- Kubernetes Support
- Integration Tests
- CQRS + MediatR
- OpenTelemetry
- API Versioning
- Rate Limiting

---

# 🤝 Contributing

Pull requests and issues are welcome.

---

# 📄 License

MIT License

---

# 🙏 Acknowledgements

- EF Core
- AutoMapper
- FluentValidation
- Serilog

---

Built with ❤️ by Sasan Boddouhi