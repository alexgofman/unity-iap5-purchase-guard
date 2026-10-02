# IAP 5 Purchase Guard

Client-side purchase hardening for **Unity IAP 5** (`StoreController`, `PendingOrder`, `ConfirmPurchase`, `Orders`) on Unity 6.

It is a small layer between the SDK and your game that decides, for every order the store delivers, whether to grant it, hold it or refuse it, and in which order to grant, remember and confirm.

- **A receipt gate with four verdicts** (valid, invalid, deferred, unavailable) and a stated fail-open or fail-closed policy.
- **A persisted transaction ledger.** An order that was already granted is recognised when the store delivers it again: it is confirmed again, not granted again.
- **A fixed order of operations**: dedupe, identify, trust gate, grant, remember, confirm, UI, telemetry.
- **Explicit store naming**, so the store cannot be switched to the SDK's test store by editing an asset.
- **Refund detection** by comparing the owned set between fetches, built so that it is hard to take a real purchase away by mistake.
- **Install-source detection** on Android.
- **One `Awaitable` reconnect loop** tied to `destroyCancellationToken`, with a capped backoff.

All decisions live in a core assembly that has no `UnityEngine` reference and is covered by tests that run with plain `dotnet test`. The Unity side is a translator: SDK events in, SDK calls out, and the reconnect loop.

Read [Status and limits](#status-and-limits) before you rely on it. This is hardening, not proof of purchase.

## Requirements

- Unity 6000.0 or newer.
- Unity IAP `com.unity.purchasing` 5.4.3 or a newer 5.x release.
- Google Play and the App Store. Other stores are not covered.
- On Apple platforms, iOS or tvOS 15 or newer. Below that Unity IAP falls back to StoreKit 1, which this package does not cover (see the limits).

## Install

Package Manager, **Add package from git URL**:

```
https://github.com/alexgofman/unity-iap5-purchase-guard.git
```

## Quick start

```csharp
using Iap5PurchaseGuard;
using UnityEngine;

public sealed class Shop : MonoBehaviour
{
    // Play Console > Monetize with Play > Monetization setup > Licensing.
    // Keep it in code. See "The key belongs in code" below.
    private const string PlayLicensingKey = "REPLACE_WITH_YOUR_PLAY_LICENSING_PUBLIC_KEY";

    private PurchasePipeline _purchases;

    private void Start()
    {
        _purchases = PurchasePipeline.Create(new PurchaseGuardSetup
        {
            Catalog = new PurchaseCatalog(new[]
            {
                new CatalogEntry("lantern_oil_100", ProductKind.Consumable),
                new CatalogEntry("map_expansion", ProductKind.NonConsumable),
                new CatalogEntry("explorer_club_monthly", ProductKind.Subscription),
            }),
            Granter = new MyEntitlements(),      // IEntitlementGranter: your game applies the product
            Ui = new MyPurchaseDialogs(),        // IPurchaseUi: optional
            Telemetry = new MyAnalytics(),       // IIapTelemetry: optional, logs to the console by default
            PlayLicensingPublicKeyBase64 = PlayLicensingKey,
            WhenValidationUnavailable = UnavailablePolicy.FailOpen,
        });
    }

    public void BuyMapExpansion() => _purchases.Buy("map_expansion");

    public void Restore() => _purchases.RestorePurchases();
}
```

You implement three small interfaces:

| Interface | What it does |
|---|---|
| `IEntitlementGranter` | `Grant`, `Revoke` and `IsEntitlementActive` for a product id. The package decides when they are called and knows nothing about what a product unlocks. `Grant` is given the transaction id, so a game can make its own grants idempotent. |
| `IPurchaseUi` | Player-facing feedback: succeeded, refused, pending or failed. Every purchase that `Buy` starts ends in at least one call, so a paywall can use the first one to release its busy state. |
| `IIapTelemetry` | Events to forward to your analytics. `PurchaseGranted` is raised at most once per transaction and never for an order the trust gate refused, so it is the event to count as revenue. |

`PurchasePipeline.Create` puts the pipeline on its own object that survives scene loads. Create one for the lifetime of the app: a second live pipeline is refused, because two would each grant the same order.

The surface of `PurchasePipeline` is small:

| Member | Purpose |
|---|---|
| `Buy(productId)` | Starts a purchase. Returns false, and tells the UI why, when it cannot start. |
| `RestorePurchases()` | Call from a Restore button. |
| `EnsureConnected()` | Restarts the reconnect loop. `Buy` and `RestorePurchases` call it. |
| `IsReady` | True while the SDK reports a live store connection. |
| `FindProduct(productId)` | The store's product data, for prices and titles. |
| `InstallReport` | The installer of this copy and whether it is an expected store. |
| `ProductsUpdated`, `OwnedPurchasesSynced`, `RestoreFailed` | Events for shop screens and the Restore button. |
| `Shutdown()` | Stops the pipeline at once. Destroying its object does the same at the end of the frame. |

A working example is in `Samples~/BasicSetup` (Package Manager, Samples tab).

## The verdict decision table

The trust gate reduces everything it knows about an order to one of four verdicts. The pipeline acts on the verdict and nothing else.

| Verdict | Meaning | Granted | Confirmed | Player is told | Telemetry |
|---|---|---|---|---|---|
| `Valid` | Backed by the store for this app, this product and this transaction, and paid | yes | yes, after the grant | succeeded | `PurchaseGranted`, once per transaction |
| `Invalid` | Forged, tampered with, for another app, product or transaction, or missing something every real order has | no | no | could not be verified | `OrderRefused` |
| `Deferred` | A genuine order that is not paid: payment pending, cancelled or refunded | no | no | payment pending | none |
| `Unavailable` | The check could not run because of the app's own configuration | `FailOpen`: yes. `FailClosed`: no | only if granted | as granted or refused | `ValidationUnavailable`, once per session |

`Invalid` always fails closed, because it is a statement about a receipt, and the other side writes the receipt. `Unavailable` is a statement about your own build (no key, or a key that does not load), so you choose:

- **`FailOpen`** (default): grant, and raise the alarm. A missing key is your mistake and should not stop every paying player. Until it is fixed, purchases are granted without the signature check.
- **`FailClosed`**: refuse. Real purchases stay unconfirmed until a build with a working key ships, and Google Play refunds a purchase that is not acknowledged within three days.

On Android the verdict comes from three groups of checks, run top to bottom. The first check that fails decides.

| Group | Check | If it fails |
|---|---|---|
| Structural | The receipt is not stamped by the SDK test store (release builds) | `Invalid` (TestStoreReceipt) |
| Structural | The order has a transaction id (Google Play always supplies a purchase token) | `Invalid` (TransactionIdMissing) |
| Structural | A receipt is present | `Invalid` (ReceiptMissing) |
| Structural | The receipt is stamped by Google Play | `Invalid` (WrongStore) |
| Structural | The order names a product | `Invalid` (ProductUnknown) |
| Key | A licensing key was supplied | `Unavailable` (KeyNotConfigured) |
| Key | The licensing key loads | `Unavailable` (KeyUnusable) |
| Signature | The RSA signature matches the purchase data | `Invalid` (SignatureMismatch) |
| Signature | The signed package name, when present, is this app | `Invalid` (WrongApplication) |
| Signature | The signed data contains a purchase | `Invalid` (NoStoreReceipt) |
| Signature | That purchase is for the ordered product | `Invalid` (WrongProduct) |
| Signature | Its purchase token is the order's transaction id | `Invalid` (WrongTransaction) |
| Signature | Its state is purchased | `Deferred` (NotPurchased) |
| | Everything passed | `Valid` (SignatureVerified) |

The structural checks come first and need no key. They still hold when the signature check is unavailable, and nothing written into a receipt can steer an order around them into the verdict that may fail open. `Unavailable` depends on the key alone.

The token check is what ties the ledger to the signature. Without it, purchase data from one real purchase could be presented again and again under invented transaction ids: the signature would verify every time, and every new id would get past the ledger.

The store stamp is read from the receipt envelope by a strict reader (`ReceiptEnvelope`): one top-level `Store` member with a plain string value. Anything else, including a repeated member or text after the closing brace, yields no store name, and an order with no store name fails the Google Play check.

The signature check itself is Unity IAP's `CrossPlatformValidator`. On iOS and tvOS the package adds no check of its own: from version 15 Unity IAP 5 uses StoreKit 2, which verifies each signed transaction on the device, and the gate returns `Valid`. In the Editor there is nothing to check, and the gate returns `Valid`.

## Pipeline order

```
 pending order (a new purchase, or the redelivery of an unconfirmed one)
      |
      v
 1. dedupe -------- already in the ledger? ------ yes --> confirm again, stop
      |                                                   (no grant, no UI, no sale reported)
      v
 2. identify ------ one product, in the catalogue? - no --> leave unconfirmed, report
      |
      v
 3. trust gate ---- Invalid ----------------------------> leave unconfirmed, tell the player, report
      |             Deferred ---------------------------> leave unconfirmed, the store delivers it again
      v
 4. grant --------- the game's grant throws? ----- yes --> leave unconfirmed, the grant is retried
      |                                                    on the next delivery
      v
 5. remember        transaction id into the persisted ledger
      |
      v
 6. confirm         ConfirmPurchase
      |
      v
 7. UI, 8. telemetry    wrapped: a failure here cannot undo steps 4 to 6
```

Why this order:

- **Dedupe before the trust gate.** A redelivered order has already been judged and granted. Judging it again can only produce a second refusal report, or a "not verified" message about something the player already has, and leaving it unconfirmed would make the store deliver it on every launch. Skipping the gate here gives a forger nothing: the ledger only holds transactions this device has already granted, and this branch never grants.
- **Nothing before the trust gate grants or reports a sale.** A forged order leaves no mark on the game economy or on revenue figures.
- **Confirm only after a successful grant.** Confirming first ends the transaction at the store. A crash or an exception before the grant would then leave a player who paid and received nothing, with no redelivery to repair it. An unconfirmed order is the safe state: the store delivers it again, and Google Play refunds it if it is never acknowledged.
- **Remember between grant and confirm.** A confirmation that fails or never reaches the store is repaired by the next delivery, without a second grant.
- **An order that names no known product is left unconfirmed.** Confirming it would end the transaction with nothing granted. `Buy` refuses products outside the catalogue for the same reason.
- **UI and telemetry last.** They are the steps most likely to throw, and by then the order is safe.

An order with no transaction id is refused: it cannot be deduplicated, Unity IAP will not confirm it, and no real store produces one.

## Owned purchases, restore and refunds

The orders the store reports as owned take a different path, because they arrive on every fetch:

- The same trust gate applies. An owned order that fails it is skipped.
- Consumables are never restored.
- A durable product is granted only when `IsEntitlementActive` says it is not active, with `GrantSource.Restore`. Use that to unlock the product without handing out one-time contents again. The ledger is deliberately not used here: it would block a restore after the game's own save data was lost.
- On Android owned purchases are fetched and processed after every store connection. On Apple platforms they are processed only when the player asks for a restore. `PurchaseGuardSetup.OwnedSync` changes either.

**Refund detection.** The set of owned durable products is remembered between fetches. A product that was owned and is no longer reported (refunded, charged back, or an expired subscription) is passed to `Revoke`.

Taking an entitlement away is held to a stricter standard than granting one:

- **Two fetches in a row.** A product has to be missing from two complete fetches before it is revoked. One answer is not enough.
- **Complete fetches only.** Only the answer to a fetch the pipeline itself asked for counts. The SDK can also pass a single order through the same callback; such a list is granted from and never revoked from.
- **The empty-fetch guard.** A fetch that returns nothing is not evidence that everything was refunded. A signed-out or switched store account, a store cache that was just cleared and a tampered billing client all give the same empty answer. An empty result revokes nothing and does not replace the remembered set, however often it repeats.
- **Unfinished orders count as owned.** Until an order is confirmed the store lists it as pending, not as owned. A product whose pending order passes the trust gate is treated as owned, so a renewal that arrived while the app was closed does not get its product revoked in the same pass. A forged or unpaid pending order does not count.
- **The catalogue is not evidence.** A product that has been removed from the catalogue is never revoked.
- A failed fetch never reaches this code at all.

A fetch that twice reports a different, non-empty set is taken at its word. Switching the store account on the device therefore revokes the previous account's products and grants the new account's.

## Explicit store naming

When no store name is given on Android, Unity IAP 5 reads the store choice from the `BillingMode.json` asset packaged with the game. Anything other than Google Play in that file, or no file, selects the SDK's built-in test store, which completes purchases without a real store behind it. Editing that one asset in a repacked build is enough to make every purchase "succeed".

The pipeline passes the store name from code (`GooglePlay.Name` on Android, `AppleAppStore.Name` on iOS and tvOS), so the asset is not consulted. As a second line, release builds refuse any order whose receipt is stamped by the test store. In the Editor and in development builds the test store keeps working, and that distinction is made at compile time rather than read from a file.

## The key belongs in code

The Play licensing key is public by design: it only lets the app check a signature. What matters is where the app gets it from.

Pass it from a constant in compiled code. A key loaded from a ScriptableObject, `Resources`, `StreamingAssets` or a remote setting can be blanked in a repacked build without touching code, and a blank key means `Unavailable`, the one verdict that can fail open.

A well-formed key that belongs to another app fails closed: every genuine receipt is refused as a signature mismatch, the orders are never confirmed and Google Play refunds them. Check the key with one licence-tester purchase from a Play testing track before release, and watch for `ValidationUnavailable` and `OrderRefused` in your telemetry after it.

## Install source

`PurchasePipeline.InstallReport` says whether Android reports an expected store (Google Play by default) as the installer of this copy. It is a signal for analytics and for pointing a player to the official listing (`InstallSource.PlayStoreListingUrl()` builds the link from the running app's identifier). It is not proof of anything: see the limits below.

`BlockPurchasesFromUnexpectedInstaller` refuses to start a purchase on such a copy. It is off by default, and restores are never blocked, so a legitimately migrated install keeps what it owns.

## Storage

Three PlayerPrefs entries, with a configurable prefix (`purchase_guard.` by default):

- `purchase_guard.ledger`: granted transaction ids, one per line, at most 256, oldest dropped first.
- `purchase_guard.owned`: the durable product ids owned at the last complete fetch.
- `purchase_guard.owned.missing`: the product ids that were missing from that fetch for the first time.

Supply your own `IStringStore` to keep them elsewhere, for example inside a cloud save. A storage read that fails is retried and is never taken for an empty ledger.

## What these guards respond to

The package grew out of dealing with fake-billing abuse on Android. The pattern is common enough to describe without specifics:

1. A repacked build of a game appears on third-party download sites, advertised as having free purchases.
2. Purchase events in the game's analytics climb far above what the store's own order reports show. A small number of installs produce most of them.
3. On inspection, several different things turn out to be happening at once:
   - The store selection asset inside the package has been edited, so the SDK loads its test store and every purchase completes. The test store's UI showing up in crash reports from release builds is the tell.
   - A patched billing client hands the game orders with no store transaction id, or with purchase data the store never signed.
   - The same orders arrive again on every launch: owned purchases, and orders whose confirmation never took effect. A "seen" set that lives only in memory forgets them between sessions, so each launch grants and reports them again.
4. No money moves in any of this. The damage is to the game economy and to revenue analytics that can no longer be trusted, along with every decision made from them.

Each guard answers one of those causes: explicit store naming and the test-store refusal for the edited asset, the transaction id requirement and the signature check for the patched client, the persisted ledger and the active-entitlement check for the repeats, and "report a sale only after the trust gate" for the analytics.

## Layout

```
Runtime/Core     decisions, no UnityEngine: verdicts, policy, ledger, backoff, owned-set diff, receipt rules, purchase flow, store session
Runtime/Unity    Unity IAP 5 adapter: PurchasePipeline, PlayReceiptValidator, StoreSelection, InstallSource
Tests/Editor     NUnit tests; the ones in Core also run under dotnet
DotnetTests~     net8.0 project that links Runtime/Core and Tests/Editor/Core
Samples~         basic setup
```

## Tests

```
dotnet test DotnetTests~
```

runs the 223 core tests against a fake store, with no Unity installation and no Unity licence. `DotnetTests~` is a plain `net8.0` NUnit project that links `Runtime/Core` and `Tests/Editor/Core`; GitHub Actions runs the same command.

Covered there, among other things: a redelivered transaction is confirmed but not granted again, also after a restart; an invalid verdict is neither granted nor confirmed; a grant that throws leaves the order unconfirmed and is retried; an order without a product is not confirmed; the ledger's bound, its eviction order and its behaviour when storage fails; the backoff schedule; an empty fetch does not revoke; one missing fetch does not revoke; a list the pipeline did not ask for does not revoke; a verified receipt with no purchase in it, or with another transaction's token, is refused; a failed confirmation is sent again; a started purchase that the store answers always ends in a UI call.

One of them is a seeded random-sequence test. It mixes purchases, deliveries, lost and failing confirmations, failing grants and restarts, and checks after every delivery that no transaction was granted twice, that nothing was confirmed without having been granted, and that nothing forged, unpaid or unidentified was granted or confirmed.

The same tests, plus a few for the Unity layer in `Tests/Editor/Unity`, run in the Unity Test Runner (EditMode) once the package is listed under `testables` in your project manifest.

## Status and limits

- **This is client-side hardening against off-the-shelf fake-billing patchers and one-file repacks.** It raises the cost of the cheap attacks. It does not make a purchase trustworthy.
- **A repack that patches the validator defeats it.** Every check here runs on a device the attacker controls. Anyone willing to patch compiled code can remove the gate, flip the policy or call the grant directly.
- **The installer-name check is spoofable.** The installer name is whatever the tool that installed the package said it was. A repack can report Google Play, and a legitimate copy can report something else after a device migration.
- **Server-side receipt validation and Play Integrity are the real fix.** Validate purchases on your own server against the store's API, use Play Integrity (and App Attest on iOS) to judge the client, and grant from the server. `PurchaseGuardSetup.ReceiptGate` is where a server-backed gate plugs in.
- **On Apple platforms the package adds no receipt check.** It relies on StoreKit 2 and the SDK.
- **StoreKit 1 is not covered.** Below iOS and tvOS 15 Unity IAP falls back to StoreKit 1. Nothing verifies a transaction on the device there, and a restore delivers owned products as new orders with new transaction ids, which the ledger cannot recognise, so they are granted and reported as purchases. The pipeline reports a warning through telemetry when it starts on such a system. Set the deployment target to 15.
- **Grant and ledger write are two steps.** If the app is killed between them, the order is delivered again and granted a second time. `Grant` receives the transaction id so that a game can record it in the same save operation and ignore a repeat.
- **Refund detection is a heuristic, and a slow one.** It needs two fetches, so on Android a refund shows at the second launch after it. Because an empty fetch never revokes, the refund of a player's only durable product, or the expiry of their only subscription, is not noticed. It cannot tell a refund from an expired subscription, and it only runs when purchases are fetched. Store server notifications are the reliable source.
- **The ledger is local.** With the default storage it lives in PlayerPrefs and does not survive a reinstall. An order that was granted but not yet confirmed when the app was removed is granted again afterwards.
- **Redelivery is the SDK's job.** The pipeline leaves an order unconfirmed and counts on the store to deliver it again. On Android it fetches purchases after every connection, which is when Google Play hands unconfirmed orders back. On Apple platforms it does not fetch at launch by default and relies on Unity IAP's StoreKit transaction listener to deliver unfinished transactions. Within one session a failed confirmation is only sent again after a reconnect or an "already owned" answer.
- **The reconnect loop waits for the SDK.** Each attempt ends when Unity IAP's connect call completes. If that call never completes, the loop does not time it out.
- **One product per order, quantity one.** That is what the Google Play and App Store carts in Unity IAP 5 produce. An order with several products is treated as unidentified and left unconfirmed. Quantities are not read, so leave multi-quantity purchases off in Play Console.
- **`PurchaseGranted` follows transactions, not billing periods.** A subscription renewal is a new transaction on the App Store and none on Google Play, so the event sees every iOS renewal and no Android renewal.
- **A restore that the store never answers raises no event.** Give the Restore button its own time limit.
- **What has and has not been run.** The code was extracted and refactored from production purchase code. In this packaged form the core has unit tests with a fake store, run with `dotnet test`, and the whole package compiles against the Unity 6000.0 engine assemblies and the Unity IAP 5.4.3 assemblies (no other IAP version was tried). The refactored package has not itself been run against the real stores, and nothing has been executed inside the Unity Editor: `PurchasePipeline` was compiled only, and the tests in `Tests/Editor/Unity` were run outside the engine against the same assemblies. Test a purchase, a redelivery, a restore and a refund on both stores before you ship it.
- **Version 0.1.0.** The API may change.

## Licence

MIT. See [LICENSE](LICENSE).
