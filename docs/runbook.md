# Runbook — Day-2 Operations

Operational procedures for a running `OnlineStoreApi` instance. Every step here
assumes the reader has database access and can restart the API container.

> If a procedure here does not work, that is a documentation bug. Fix the
> document, not the memory of whoever ran it last time.

---

## 1. Migration order

Migrations are additive-by-default and must run **in order**. The full M6 chain:

```text
InitialCreate
MakeUserDateOfBirthNullable
UpdateEmployeeType
AddReplacedByTokenId
Phase0_InventoryRowVersion
Phase1_RbacFoundation
Phase2_OwnershipFoundation
Phase3_FinancialsAndSplitting
Phase4_CartAndCheckout
Phase5_ReviewsAndMedia
Phase6_VariantsAndAttributes
Phase8_PaymentAndDeliveredAt        ← data backfill
Phase9_DropPaymentStatus            ← drops a column
Phase9_RatingCache                  ← data backfill
```

The three marked phases rewrite or delete data. **Take a database backup before
applying any of them**, and read `docs/deployment.md` §4 first.

### Verify a migration before applying it

```bash
dotnet ef migrations script \
  --project DataLayer/DataLayer.csproj \
  --startup-project "Online Store Application/Online Store Application(API).csproj" \
  --idempotent --output migrate.sql
```

Never pass `--no-build` to `migrations script`; it silently uses a stale assembly
and produces a script that does not match the code you are about to deploy.

### Verify migrations are tested

```bash
dotnet test OnlineStore.Tests.Migrations
```

These run against a **real SQL Server (LocalDB)** and skip nothing. If LocalDB is
missing they fail loudly, on purpose.

---

## 2. How to add a new seller

There is no self-service seller onboarding yet; a seller is a `User` + `Seller`
row.

1. **Create the user** through the normal registration endpoint
   (`POST /api/v1/auth/register`). Note the returned `userId`.
2. **Create the seller profile** as an admin:

   ```http
   POST /api/v1/sellers
   Authorization: Bearer <admin-token>
   Content-Type: application/json

   { "userId": 42, "storeName": "My Shop" }
   ```

3. The seller starts as `Pending` (`SellerStatus.Pending` = 0). Approve it:

   ```http
   PUT /api/v1/sellers/{sellerId}
   { "sellerId": 7, "status": "Approved", "isActive": true }
   ```

4. The seller now creates listings (`POST /api/v1/sellers/{sellerId}/listings`).
   A listing references a `VariantId`, not a `ProductId`.
5. **Stock must exist before the first order.** `Inventory` is keyed by
   `ListingId`; without a row there, checkout fails with an availability error.

Verify with:

```sql
SELECT s.SellerId, s.StoreName, s.Status, u.PhoneNumber
FROM Seller s JOIN [User] u ON u.UserId = s.UserId
WHERE s.SellerId = 7;
```

---

## 3. How to refund a payment

**Automatic refunds are not implemented.** `IPaymentGateway.RefundPaymentAsync`
exists, but the Zarinpal adapter returns "unsupported" and the platform only
records the state change. Money moves outside the system.

### What the system does automatically

When an order that has a `Succeeded` payment is cancelled, `OrderService`
transitions each such payment to `Refunded` and writes an `AuditLog` row with
action `Payment.Refunded`. That is a **bookkeeping** entry.

### Manual procedure

> **Phase 9 added an admin endpoint for step 3 below.** Before it, the only way to
> record a refund was to cancel the order — which is impossible once it has
> shipped. Now:

```http
POST /api/v1/payments/{paymentId}/refund
Authorization: Bearer <admin-token>
Content-Type: application/json

{ "reason": "customer returned the item" }
```

It transitions `Succeeded → Refunded`, moves the order to `Returned` (delivered)
or `Cancelled` (not yet delivered), cancels the seller's **pending** payouts,
and writes an `AuditLog` row containing `MONEY_MOVED_MANUALLY=true`.

Two things it deliberately does **not** do:

- **It does not move money.** Refund in the gateway panel first.
- **It does not create a negative `SellerPayout`.** The entity forbids negative
  amounts and that constraint is correct: a negative payout row would corrupt
  every seller earnings report. Cancelling the pending payout is the equivalent
  financial effect.

A payout that is already `Paid` cannot be reversed automatically; the log
records a warning and the money must be reclaimed from the seller out of band.

1. Find the payment:

   ```sql
   SELECT p.PaymentId, p.State, p.Amount, p.TransactionId, p.GatewayReference, o.OrderId
   FROM Payment p
   JOIN [Order] o ON o.OrderId = p.OrderId
   WHERE o.OrderId = 12345;
   ```

2. Refund the customer **in the gateway's own panel** using `TransactionId`
   (the authority) or `GatewayReference` (the gateway's ref id).
3. Let the system record it with `POST /api/v1/payments/{paymentId}/refund`.
   Do **not** update `Payment.State` with raw SQL — the state machine and the
   audit trail are the only guard against double refunds.

### If `State` is already `Refunded` but the money did not move

That means step 2 was skipped. The system has no way to detect this. Reconcile
manually against the gateway statement; this is exactly the failure mode that
`docs/deployment.md` §5 warns about.

---

## 4. How to handle a stuck order

`Order.Status` is the aggregate; `OrderSellerGroup.Status` is per-seller. A stuck
order is almost always a group that never advanced.

### Diagnose

```sql
SELECT o.OrderId, o.Status AS OrderStatus, g.OrderSellerGroupId, g.SellerId,
       g.Status AS GroupStatus, g.DeliveredAt, p.State AS PaymentState
FROM [Order] o
LEFT JOIN OrderSellerGroup g ON g.OrderId = o.OrderId
LEFT JOIN Payment p ON p.OrderId = o.OrderId
WHERE o.OrderId = 12345;
```

| Symptom | Meaning | Action |
|---|---|---|
| `OrderStatus=Pending`, `PaymentState=Pending` | Customer abandoned the gateway | Nothing. The cleanup job cancels the payment after 30 minutes; the order stays `Pending` so the customer can retry. |
| `OrderStatus=Pending`, `PaymentState=Succeeded` | Payment landed but the order was never confirmed | Re-run `POST /api/v1/payments/verify?authority=<TransactionId>`. It is idempotent. |
| `GroupStatus=Shipped`, no movement for days | Carrier problem, not a system problem | Contact the seller; the group is waiting for `MarkDeliveredAsync`. |
| `OrderStatus=Delivered`, `DeliveredAt` is NULL | **Bug.** Phase 8 sets `DeliveredAt` on every delivery. | Report it; the sales-summary date filter relies on this column. |
| Payment stuck `Pending` for > 30 min | Cleanup job is not running | Check the host logs for `AbandonedPaymentCleanupService` startup line and for `IAbandonedPaymentSweeper` resolution errors. |

### Force a transition (last resort)

Only through the API, only as an admin. Direct `UPDATE` on `Order.Status` skips
inventory effects and leaves the audit log lying.

---

## 5. Payment state machine reference

```text
Initiated ──▶ Pending ──▶ Succeeded ──▶ Refunded
    │            │
    ├──▶ Failed  ├──▶ Failed
    └──▶ Cancelled
                 └──▶ Cancelled   (abandoned-payment cleanup)
```

Illegal edges throw `InvalidOperationException` from `Payment.TransitionTo` and
surface as HTTP 500. Seeing one means a code path bypassed the machine — treat it
as a defect, not as data corruption to patch by hand.

---

## 6. When Zarinpal is enabled for the first time

`ZarinpalPaymentGateway` has never executed against the real API; it fails closed
until `PaymentGatewaySettings:EnableRealGateway=true` and a `MerchantId` is
present. Treat the first enabled run as an unproven integration:

1. Set the merchant id and webhook secret from the environment, not from
   `appsettings.json`.
2. Run one real payment for the smallest allowed amount.
3. Confirm three things: `Payment.State` reaches `Succeeded`, `GatewayReference`
   is populated, and an `AuditLog` row exists with action `Payment.Succeeded`.
4. Only then raise the amount limits.