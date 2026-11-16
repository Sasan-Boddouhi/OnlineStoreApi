# API contract (OpenAPI)

`openapi/v1.json` is the machine-readable contract of API v1. It is generated from the code and committed; CI fails when the committed file differs from the generated one. `openapi/error-codes.json` lists every machine-readable error `code` (Batch 53).

## Regenerate

```powershell
pwsh scripts/generate-openapi.ps1          # rewrite openapi/v1.json
pwsh scripts/generate-openapi.ps1 -Check   # exit 1 if the committed file is stale (used by CI)
```

The script builds the API, starts it in the `Testing` environment with placeholder settings (no database or Redis is contacted) and runs `dotnet swagger tofile` (Swashbuckle CLI, pinned in `.config/dotnet-tools.json`). The CLI needs the .NET 9 runtime next to .NET 8 (`DOTNET_ROLL_FORWARD=LatestMajor` is set by the script).

## Conventions in the document

- **Operation ids** are `{Controller}_{Action}`. An action reachable by a second route gets the suffix `_Alias` on that route (`SellerPayouts_Pay` and `SellerPayouts_Pay_Alias`).
- **Errors.** Every operation lists 400, 404, 409, 422, 429 and 500, plus 401 and 403 when it needs authentication, all as `application/problem+json` with the shared `ProblemDetails` schema (`ValidationProblemDetails` for 400 and 422). Each error response carries `x-error-codes`, the registry codes that use that status.
- **Required members.** A non-nullable C# member is `required`; `string?` and `int?` are optional and `nullable`.
- **Headers.** `X-Cart-Session` on cart operations, `Idempotency-Key` on `payments/initiate`, optional `X-Client-Platform` and `X-App-Version` on every operation.
- **Enums** are numbers on the wire today. The schema stays numeric and lists the names in `x-enum-varnames`.
- `/version` (deployment check) is not part of the contract.
