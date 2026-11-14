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
exists, but the Zarinpal adapter returns "unsupported". Money moves outside the
system; the system keeps a queue of refunds that are owed and records the bank
reference once the money has gone back (Batches 41 and 42).

### How a refund becomes owed

A `Succeeded` payment moves to **`RefundPending`** (never straight to `Refunded`) when:

- the customer cancels a paid order before any parcel ships (after that the
  customer gets `409 ORDER_PARTLY_SHIPPED` and must ask for a return);
- an admin cancels or returns a paid order;
- an admin requests a refund (below).

Each of these writes `Payment.RefundRequested` to the audit log with
`MONEY_NOT_RETURNED_YET=true`, and goes through one routine
(`OrderReversalService`): parcels, stock, coupon quota, pending payouts and
claw-back debts for paid payouts.

### Procedure

1. **Request** (only when no cancel/return did it already):

   ```http
   POST /api/v1/payments/{paymentId}/refund
   Authorization: Bearer <admin-token>
   Content-Type: application/json

   { "reason": "customer returned the item" }
   ```

   `Succeeded → RefundPending`; the order becomes `Returned` (delivered) or
   `Cancelled` (not shipped); an order whose parcels are all in transit is left
   as it is. A reason is required (`422` without one). A payment that is not
   `Succeeded` — including one that is already `RefundPending` — answers
   `409 PAYMENT_NOT_REFUNDABLE`. If the order is still paid by another payment
   (a duplicate capture), only this payment is refunded and the order is left
   alone.

2. **Work the queue:**

   ```http
   GET /api/v1/payments/refunds?pageNumber=1&pageSize=20
   ```

   Oldest request first. `includeRefunded=true` also lists confirmed refunds.

3. **Pay the customer back in the gateway or bank panel** using
   `TransactionId` (authority) or `GatewayReference`.

4. **Confirm** with the bank reference:

   ```http
   POST /api/v1/payments/{paymentId}/refund/confirm
   { "bankReference": "SATNA-778899", "refundedAtUtc": "2026-11-14T08:30:00Z", "note": "optional" }
   ```

   `RefundPending → Refunded`, stores the reference and the bank date (now when
   omitted, never in the future), and writes `Payment.Refunded` with
   `MONEY_MOVED_MANUALLY=true`. Without a reference: `422`. A payment that is
   not `RefundPending`: `409 PAYMENT_NOT_REFUND_PENDING`.

Do **not** update `Payment.State` with raw SQL — the state machine and the audit
trail are the only guard against double refunds.

What the system deliberately does **not** do: it does not move money, and it
does not create a negative `SellerPayout` (a pending payout is cancelled; a paid
one gets a `SellerDebt` claw-back that is offset against the seller's next payout).

### Payments that need a human (review queue)

```http
GET  /api/v1/payments/review?pageNumber=1&pageSize=20          # open reviews, oldest first
GET  /api/v1/payments/review?includeResolved=true
POST /api/v1/payments/{paymentId}/review/resolve   { "note": "what was checked and done" }
```

A payment is flagged (`ReviewRequiredAtUtc`, `ReviewNote`) when money was
captured for an order that is no longer payable (`refundNeeded=true` in the
note — request a refund), when the order could not be synchronised after a
capture, or when a verify started but never got a definitive answer (the
sweeper and the abandoned-order job flag it once; verify the payment again).
Resolving needs a note (`422` without one) and answers
`409 PAYMENT_REVIEW_NOT_OPEN` when there is nothing open. The jobs do not reopen
a resolved review; a new problem after a capture does.

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
Initiated ──▶ Pending ──▶ Succeeded ──▶ RefundPending ──▶ Refunded
    │            │                    (refund owed)       (bank reference recorded)
    ├──▶ Failed  ├──▶ Failed
    └──▶ Cancelled
                 └──▶ Cancelled   (abandoned-payment cleanup)
```

There is no `Succeeded → Refunded` shortcut (Batch 41): a payment is only shown
as refunded after an admin confirms the bank transfer (Batch 42). A `Pending`
payment with `VerifyStartedAtUtc` set has an unknown verify outcome and is never
cancelled silently. The review flag (`ReviewRequiredAtUtc`) is independent of the
state.

Illegal edges throw `BusinessRuleViolationException` (`PAYMENT_INVALID_TRANSITION`)
from `Payment.TransitionTo` and surface as HTTP 409. Seeing one means a code path
bypassed the machine — treat it as a defect, not as data corruption to patch by hand.

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
---

## 7. Backups and restore test (Batch 52)

Scripts and what they produce: `docs/deployment.md` Appendix B.5.

### Schedule

On the Docker host (`crontab -e` for a user that may run `docker`):

```cron
# Daily backup at 02:30 UTC; log to a file that logrotate picks up.
30 2 * * * cd /opt/onlinestore && scripts/backup.sh >> /var/log/onlinestore-backup.log 2>&1
```

Copy each new `$BACKUP_ROOT/<stamp>/` directory to storage outside the machine (another server or an object store) after the job; a backup on the same disk does not survive that disk.

### Restore test

At least once a month, and after every change to the database or the backup script:

```bash
scripts/restore-test.sh                      # newest backup
scripts/restore-test.sh /var/backups/onlinestore/<stamp>
```

It must end with `RESTORE TEST PASSED`. Paste the summary into the log below. A backup that has never been restored is not counted as a backup.

### Restore test log

| Date (UTC) | Backup stamp | Restore time | Last migration | Upload files | By |
|---|---|---|---|---|---|
| – | – | – | – | – | – |

### Restoring production (real incident)

1. Stop the API: `docker compose -f docker-compose.prod.yml stop api`.
2. Copy the `.bak` into the SQL container and restore it as `sa`:
   `RESTORE DATABASE [ShopDB] FROM DISK = N'/var/opt/mssql/backup/<file>.bak' WITH CHECKSUM, REPLACE, RECOVERY`.
3. Run `docker compose -f docker-compose.prod.yml --profile setup run --rm db-init` — it re-maps the application and migration users to their logins (a restored database has orphaned users).
4. Unpack the uploads archive into the uploads volume (`docker run --rm -v onlinestoreapi_uploads_data:/data -v <dir>:/backup alpine:3.20 tar -xzf /backup/<uploads>.tar.gz -C /data`).
5. `docker compose -f docker-compose.prod.yml up -d` (the migrator applies any migration newer than the backup), then check `/health/ready` and `/version`.

## 8. SQL Server Express size limit (Batch 52, N15)

Express limits every database to 10 GB of data (log files excluded) and uses at most about 1.4 GB of memory for its buffer pool. `scripts/backup.sh` writes the data size to `manifest.txt` and prints a warning at 8 GB. Check by hand:

```sql
SELECT SUM(CAST(size AS bigint)) * 8 / 1024 AS data_mb FROM ShopDB.sys.database_files WHERE type = 0;
```

At 8 GB plan the move to Standard: buy the licence, set `MSSQL_PID=Standard` (or the product key) in `.env`, take a backup, recreate the `sqlserver` container, and confirm `SELECT SERVERPROPERTY('Edition')`.
