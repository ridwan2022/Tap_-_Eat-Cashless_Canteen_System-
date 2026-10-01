# Tap & Eat — Sprint 2 (ASP.NET Core / C#)

Sprint 2 is an **extension of Sprint 1** (same solution, same layering, same
conventions). It adds ordering, payments (bKash / Nagad / wallet), RFID-card
wallets with enterprise subsidies, queue tokens with prep-time estimates, and
RFID meal release at the counter with live (SSE) updates.

| Story (Sprint 2 plan) | What was built |
|---|---|
| **4 Payment Processing** | bKash + Nagad adapters (`Payments/`), config-switchable Mock/Live mode, payment UI, one-charge-per-order idempotency, verified digital token, failure/timeout handling |
| **5 RFID Wallet & Subsidy** | Wallet ledger + top-up via bKash/Nagad, RFID card ↔ wallet linking (admin), subsidy schedules + scheduler job, wallet UI |
| **6 RFID Tap Verification** | Tap event log, tap → paid-order match → release/reject with reasons, counter UI, **SSE** stream to counter/kitchen screens |
| **7 Queue Token & Prep Time** | Unique-per-day token per paid order, prep-time estimate, live kitchen board with status changes and recalculated ETAs |

> **Sprint 1 gaps closed here.** Sprint 1 had no order/cart entity and no
> payment code, so Sprint 2 also adds a minimal `Order` (stock is reserved on
> order, released on cancel/expiry) and implements Tasks **4.3** (idempotency)
> and **4.4** (verified payment token), which belong to the payment story but
> were not in the Sprint 2 sheet's task list.

## Run it

Requires the **.NET 8 SDK**.

```bash
cd src/TapAndEat.Api
dotnet run --urls "http://localhost:5299"
```

Open http://localhost:5299. Demo accounts (seeded on every start — change before any real deployment):

| Role | Login | Notes |
|---|---|---|
| Admin | `admin@tapandeat.local` / `Admin@12345` | Wallets & cards page, subsidy schedules |
| Kitchen staff | `kitchen@tapandeat.local` / `Kitchen@12345` | Kitchen board, Counter |
| Employee | `employee@tapandeat.local` / `Employee@12345` | Wallet ৳500, RFID card `04A1B2C3`, daily ৳100 subsidy |

**Try the whole flow (2 browser windows):**
1. *Employee window:* Menu → add items → Cart → **Place order** → pay with **bKash**. You land on the **mock bKash page** (no real money) → **Pay**. You get a token (e.g. `T-001`) and an ETA.
2. *Kitchen window:* **Kitchen** → the token appears live → **Start** → **Mark ready**.
3. *Kitchen window:* **Counter** → type `04A1B2C3` + Enter (a USB RFID reader types exactly this) → **Approved**. Tap again → *Already collected*. Try an unpaid order → *Order not paid yet*.
4. *Counter → "Tap to pay"* pays an unpaid order straight from the card holder's wallet.

## Tests

```bash
dotnet run --project tests/TapAndEat.Tests
```

**109 tests** (32 from Sprint 1 + 77 new), all passing at hand-over. They include
unit tests of every service, stubbed-HTTP tests of the bKash/Nagad adapters, concurrency
tests (double-submit, double-tap, overlapping subsidy runs), and an **end-to-end
suite that boots the real app and drives it over HTTP** — including the SSE
stream and the mock gateway round trip (`Suites/EndToEndTests.cs`).

The runner is still the hand-rolled one from Sprint 1 (no NuGet needed); on a
machine with NuGet access it ports to xUnit mechanically, as before.

## Project layout (new in Sprint 2 marked ★)

```
src/TapAndEat.Api/
  Startup/AppHost.cs          ★ DI wiring + pipeline (Program.cs is now Build → Seed → Run)
  Infrastructure/             ★ AppException, KeyedLock, EventBroadcaster (SSE), CardUid, options
  Models/                     ★ Order, Payment, Wallet(+Transaction, SubsidySchedule), QueueToken, RfidTapEvent
  Repositories/               ★ in-memory repos for the above (same pattern as Sprint 1)
  Payments/                   ★ IPaymentGateway, MockPaymentGateway, BkashGateway, NagadGateway(+Crypto), resolver
  Services/                   ★ Order, Payment, PaymentToken, Wallet, Subsidy, Queue, Counter services
  Jobs/ScheduledJobsService   ★ background job: subsidy credits + expiring unpaid orders
  Controllers/                ★ Orders, Payments, MockGateway, Wallet, AdminWallets, Kitchen, Counter, Stream
  wwwroot/                    ★ checkout, payment-result, orders, wallet, kitchen, counter, admin-wallets, mock-gateway
tests/TapAndEat.Tests/Suites/ ★ OrderAndQueue, Payment, WalletAndSubsidy, Counter, EndToEnd
```

## Key API endpoints

| Endpoint | Who | Purpose |
|---|---|---|
| `POST /api/orders` · `GET /api/orders/mine` · `POST /api/orders/{id}/cancel` | signed-in | place / track / cancel |
| `POST /api/payments/order` (`Idempotency-Key` header) | signed-in | pay with `bKash` \| `Nagad` \| `Wallet` |
| `GET /api/payments/callback/{bkash\|nagad}?pid=` | gateway redirect | confirms **server-to-server** before trusting anything |
| `GET/POST /api/payments/{id}` · `/verify` · `/cancel` | owner | status, re-check after timeout, abandon |
| `GET /api/wallet` · `/transactions` · `POST /api/wallet/topup` | signed-in | balance, ledger, top-up (rejects ≤ 0) |
| `PUT/DELETE /api/admin/wallets/{userId}/card` · `PUT …/subsidy` · `POST /api/admin/subsidies/run` | Admin | issue cards, subsidy schedule, run job now |
| `GET /api/kitchen/board` · `PUT /api/kitchen/tokens/{id}/status` | Kitchen/Admin | live queue, mark Preparing/Ready |
| `POST /api/counter/tap` · `/verify` · `/tap-to-pay` · `GET /recent` | Kitchen/Admin | RFID release, manual token lookup, pay-by-tap |
| `GET /api/stream/kitchen?access_token=` | Kitchen/Admin | Server-Sent Events (`tap`, `token`) |

## How the important rules are enforced

- **No double charge.** `Idempotency-Key` replays return the original payment; one in-flight payment per order; an already-paid order is refused; all guarded by a per-order lock. Wallet postings carry a unique idempotency key too.
- **Redirects are never trusted.** The callback only *asks the gateway* (bKash execute/query, Nagad verify); the confirmed amount must match the order.
- **Timeouts ≠ failures.** If the gateway can't be reached while confirming, the payment stays `Initiated` (the customer may have paid) and can be re-verified; if it can't be reached to *start* a payment it is `Failed — nothing was charged`, retryable.
- **Late money isn't lost.** Money that arrives after its order expired/was cancelled is credited to the wallet as a Refund.
- **Meals are released only when the order is PAID *and* the token is READY.** Every tap (approved or rejected) is logged and pushed over SSE. Concurrent taps release once.
- **Subsidy credits are idempotent** (per user + period, in business time UTC+6): scheduler ticks, restarts and manual "run now" can't double-credit.
- **Prep-time estimate** = `ceil(queued work ÷ stations) + slowest line of the order`, recomputed live for every waiting token (`Kitchen:Stations`, default 2; per-item `PrepTimeMinutes`, default 5).

## Going live with bKash / Nagad

Both gateways default to **Mock** (`Payments:Bkash:Mode` / `Payments:Nagad:Mode` = `Mock`). To use the real adapters set the mode to `Live` and supply credentials **via user-secrets or environment variables — never in `appsettings.json`**:

```bash
cd src/TapAndEat.Api
dotnet user-secrets set "Payments:Bkash:Mode" "Live"
dotnet user-secrets set "Payments:Bkash:AppKey" "..."      # + AppSecret, Username, Password, BaseUrl (sandbox/prod)
dotnet user-secrets set "Payments:Nagad:Mode" "Live"
dotnet user-secrets set "Payments:Nagad:MerchantId" "..."  # + MerchantPrivateKey (base64 PKCS#8), PgPublicKey (base64 X.509), BaseUrl
dotnet user-secrets set "Payments:TokenSecret" "<long random string>"
dotnet user-secrets set "Payments:PublicBaseUrl" "https://your-public-host"   # gateways must be able to reach the callback
```

> ⚠️ **The Live adapters were written from bKash's tokenized-checkout and Nagad's merchant API and are covered by stubbed-HTTP tests, but they have NOT been run against real sandbox credentials** (the build environment has no network access to the gateways). Before demo/deploy, run one sandbox payment per gateway end to end and check field names against your merchant documentation — the adapters are small (`Payments/BkashGateway.cs`, `NagadGateway.cs`) and isolated behind `IPaymentGateway`.

## Decisions & limitations to know about

1. **In-memory storage (unchanged from Sprint 1).** Data resets on restart, and the demo accounts are re-seeded. The repository interfaces are the swap points for EF Core (see Sprint 1's upgrade path); EF wasn't added because the build environment can't reach NuGet and it can't be tested there. Set `Payments:TokenSecret` when you add persistence, otherwise payment tokens die with the process.
2. **Cards are issued by an Admin**, not self-linked, so nobody can attach a card they don't hold. Open it up in `WalletController` if the plan intends self-service.
3. **A meal is only released when its token is *Ready*.** A tap on a paid order that's still cooking is rejected with the token and remaining time.
4. **Unpaid orders hold stock for 15 min** (`Orders:PaymentWindowMinutes`), then expire and release it.
5. **`PUT /api/menu/{id}/stock` is now staff-only** (Sprint 1 left it open to any signed-in user as a demo hook, with a note to lock it down once ordering existed).
6. **Admin menu form has no prep-time field yet.** The API accepts `prepTimeMinutes` on create/update/override (default 5); add an input in `admin.html` if the kitchen wants to set it from the UI.
7. **The browser pages were syntax-checked and every page is served by the running app, and the full journey is exercised over HTTP by the end-to-end tests, but the UI was not clicked through in a real browser** in the build environment — please do the walkthrough above once.
8. Forgot-password still returns its token in the response (Sprint 1 dev shortcut; no email/SMS gateway yet).
