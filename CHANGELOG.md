# Changelog

Notable changes to this package. Versions follow [Semantic Versioning](https://semver.org/).

## 0.1.0 - 2026-10-02

First public version.

### Added

- `PurchaseFlow`: the order of operations for a pending order (dedupe, identify, trust gate, grant, remember, confirm, UI, telemetry) and for owned orders, written against small interfaces with no `UnityEngine` reference.
- `PurchaseSession`: the conversation with the store around the flow (which fetch answers are complete, restores, the purchase in flight, confirmations to send again), also without `UnityEngine`.
- `ReceiptVerdict` (valid, invalid, deferred, unavailable) and `VerdictPolicy` with a fail-open or fail-closed choice for the unavailable case.
- `TransactionLedger`: persisted, bounded, oldest entry dropped first.
- `OwnedSetDiff`: refund detection by owned-set comparison. An empty fetch never revokes, and a product has to be missing from two complete fetches in a row.
- `BackoffSchedule`: capped exponential delays for store reconnects.
- `PlayReceiptRules` and `PlayReceiptValidator`: Google Play signature check through Unity IAP's `CrossPlatformValidator`, with the licensing key supplied from code. The signed product and purchase token have to match the order.
- `ReceiptEnvelope`: strict reader for the store name stamped on a receipt.
- `StoreSelection`: the store is named explicitly on Android, iOS and tvOS.
- `InstallSource` and `InstallSourcePolicy`: installer check on Android release builds.
- `PurchasePipeline`: a `MonoBehaviour` that translates between the session and the Unity IAP 5 `StoreController`, with one `Awaitable` reconnect loop.
- EditMode tests, and a `net8.0` project (`DotnetTests~`) that runs the core tests with `dotnet test`.
- A basic setup sample.
