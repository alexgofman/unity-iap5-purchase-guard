using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>How the flow dealt with one pending order.</summary>
    public enum PendingOutcome
    {
        /// <summary>Refused by the trust gate. Nothing granted; the order is left unconfirmed.</summary>
        Refused = 0,

        /// <summary>Granted now, recorded in the ledger and sent for confirmation.</summary>
        Granted,

        /// <summary>A redelivery of a transaction this device already granted. Confirmed again, not granted again.</summary>
        AlreadyGranted,

        /// <summary>A genuine order that is not paid yet. Nothing granted; the order is left unconfirmed.</summary>
        Waiting,

        /// <summary>The game's grant threw. The order is left unconfirmed so the store delivers it again.</summary>
        GrantFailed,

        /// <summary>The order names no product from the catalogue. Nothing granted; the order is left unconfirmed.</summary>
        Unidentified
    }

    /// <summary>Settings for <see cref="PurchaseFlow"/>.</summary>
    public sealed class PurchaseFlowOptions
    {
        /// <summary>The store name Unity IAP 5 stamps on receipts from its built-in test store.</summary>
        public const string SdkTestStoreName = "fake";

        /// <summary>
        /// Refuse any order whose receipt is stamped by the SDK test store. Leave it on for release
        /// builds, where such an order can only mean the test store was forced on. Turn it off in
        /// the Editor and in development builds, where the test store is the normal way to try a
        /// purchase.
        /// </summary>
        public bool RejectTestStoreOrders { get; set; } = true;

        public string TestStoreName { get; set; } = SdkTestStoreName;
    }

    /// <summary>What one pass over the owned orders did.</summary>
    public sealed class OwnedSyncReport
    {
        private static readonly string[] Nothing = new string[0];

        /// <summary>Entitlements that were not active and were granted.</summary>
        public int Granted { get; internal set; }

        /// <summary>Owned products whose entitlement was already active. Nothing was done.</summary>
        public int AlreadyActive { get; internal set; }

        /// <summary>Orders the trust gate refused.</summary>
        public int Refused { get; internal set; }

        /// <summary>Genuine orders that are not in the purchased state.</summary>
        public int Waiting { get; internal set; }

        /// <summary>Orders whose grant or entitlement check threw.</summary>
        public int Failed { get; internal set; }

        /// <summary>Orders that name no durable product from the catalogue.</summary>
        public int Skipped { get; internal set; }

        /// <summary>
        /// Owned products that were missing from this fetch for the first time. They are not
        /// revoked yet; a second complete fetch has to confirm it.
        /// </summary>
        public int MissingOnce { get; internal set; }

        /// <summary>Products revoked because two fetches in a row no longer reported them as owned.</summary>
        public IReadOnlyList<string> Revoked { get; internal set; } = Nothing;

        /// <summary>True when the empty-fetch guard stopped a revocation. See <see cref="OwnedSetDiff"/>.</summary>
        public bool EmptyFetchIgnored { get; internal set; }
    }

    /// <summary>
    /// The order of operations for store orders, written against small interfaces so it can be
    /// driven by a fake store.
    ///
    /// A pending order goes through these steps, and stops at the first one that says so:
    ///
    /// <code>
    /// 1. dedupe      a transaction this device already granted is confirmed, and that is all
    /// 2. identify    the order has to name one product from the catalogue
    /// 3. trust gate  not from the SDK test store, transaction id present, receipt verdict
    /// 4. grant       the game applies the entitlement
    /// 5. remember    the transaction id goes into the persisted ledger
    /// 6. confirm     the store is told the order is fulfilled
    /// 7. UI          the player is told
    /// 8. telemetry   the sale is reported
    /// </code>
    ///
    /// Why this order:
    ///
    /// Dedupe comes before the trust gate because a redelivered order has already been judged and
    /// granted. Judging it again can only produce a second refusal report, or a "not verified"
    /// message about something the player already has, and leaving it unconfirmed would make the
    /// store deliver it on every launch. Skipping the gate here gives a forger nothing: the ledger
    /// only holds transactions this device has already granted, and this branch never grants.
    ///
    /// Nothing before the trust gate grants or reports a sale, so a forged order leaves no mark on
    /// the game economy or on revenue figures.
    ///
    /// Confirm comes only after a successful grant. Confirming first would end the transaction at
    /// the store, and a crash or an exception before the grant would leave a player who paid and
    /// received nothing, with no redelivery to repair it. An unconfirmed order is the safe state:
    /// the store delivers it again, and Google Play refunds it if it is never acknowledged.
    ///
    /// The ledger is written between grant and confirm, so a confirmation that fails or never
    /// reaches the store is repaired by the next delivery without a second grant.
    ///
    /// UI and telemetry come last and are wrapped. They are the steps most likely to throw, and by
    /// then the order is already safe.
    /// </summary>
    /// <remarks>
    /// Not thread-safe. Call it from the thread that receives the store callbacks.
    /// </remarks>
    public sealed class PurchaseFlow
    {
        private readonly PurchaseCatalog _catalog;
        private readonly TransactionLedger _ledger;
        private readonly OwnedSetBaseline _ownedBaseline;
        private readonly IReceiptGate _gate;
        private readonly VerdictPolicy _policy;
        private readonly IEntitlementGranter _granter;
        private readonly IOrderConfirmer _confirmer;
        private readonly IPurchaseUi _ui;
        private readonly IIapTelemetry _telemetry;
        private readonly PurchaseFlowOptions _options;
        private bool _unavailableReported;

        public PurchaseFlow(
            PurchaseCatalog catalog,
            TransactionLedger ledger,
            OwnedSetBaseline ownedBaseline,
            IReceiptGate gate,
            VerdictPolicy policy,
            IEntitlementGranter granter,
            IOrderConfirmer confirmer,
            IPurchaseUi ui = null,
            IIapTelemetry telemetry = null,
            PurchaseFlowOptions options = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _ownedBaseline = ownedBaseline ?? throw new ArgumentNullException(nameof(ownedBaseline));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _granter = granter ?? throw new ArgumentNullException(nameof(granter));
            _confirmer = confirmer ?? throw new ArgumentNullException(nameof(confirmer));
            _ui = ui ?? NullPurchaseUi.Instance;
            _telemetry = telemetry ?? new NullIapTelemetry();
            _options = options ?? new PurchaseFlowOptions();
        }

        /// <summary>
        /// Handles a pending order: a purchase the player just made, or one the store is delivering
        /// again because it was never confirmed.
        /// </summary>
        public PendingOutcome ProcessPending(IStoreOrder order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            string transactionId = order.TransactionId ?? string.Empty;
            string productId = order.ProductId ?? string.Empty;

            // 1. Dedupe against the persisted ledger.
            if (transactionId.Length > 0 && _ledger.Contains(transactionId))
            {
                Confirm(order);
                return PendingOutcome.AlreadyGranted;
            }

            // 2. Identify. An order that cannot be matched to a product is left unconfirmed rather
            //    than confirmed with nothing granted: confirming would end the transaction for a
            //    player who paid and received nothing.
            if (!_catalog.Contains(productId))
            {
                Refuse(productId, VerdictReason.ProductUnknown, OrderPath.Purchase);
                return PendingOutcome.Unidentified;
            }

            // 3. Trust gate.
            ReceiptCheck check = Judge(order, OrderPath.Purchase);
            switch (_policy.Decide(check.Verdict))
            {
                case GateDecision.Grant:
                    break;
                case GateDecision.Hold:
                    Notify(ui => ui.PurchasePending(productId));
                    return PendingOutcome.Waiting;
                default:
                    Refuse(productId, check.Reason, OrderPath.Purchase);
                    return PendingOutcome.Refused;
            }

            // 4. Grant.
            try
            {
                _granter.Grant(productId, transactionId, GrantSource.Purchase);
            }
            catch (Exception error)
            {
                // Left unconfirmed on purpose: the store delivers the order again and the grant is retried.
                Report(telemetry => telemetry.GrantFailed(productId, OrderPath.Purchase, error));
                Notify(ui => ui.PurchaseFailed(productId, PurchaseFailureKind.GrantFailed));
                return PendingOutcome.GrantFailed;
            }

            // 5. Remember, 6. confirm. A ledger that cannot be written must not stop the
            //    confirmation: the entitlement is already granted, so an unconfirmed order could
            //    only be granted a second time.
            Remember(transactionId);
            Confirm(order);

            // 7. UI, 8. telemetry.
            Notify(ui => ui.PurchaseSucceeded(productId));
            Report(telemetry => telemetry.PurchaseGranted(productId, transactionId));
            return PendingOutcome.Granted;
        }

        /// <summary>
        /// Handles a complete list of the orders the store reports as owned. Shorthand for the
        /// overload below with no unfinished orders.
        /// </summary>
        public OwnedSyncReport ProcessOwned(IReadOnlyList<IStoreOrder> ownedOrders)
        {
            return ProcessOwned(ownedOrders, null, true);
        }

        /// <summary>
        /// Handles the orders the store reports as owned after a successful purchase fetch (launch
        /// sync or restore), then revokes what is no longer owned.
        ///
        /// Idempotency here is "is the entitlement active", not the transaction ledger: an owned
        /// product is reported on every fetch, and the ledger would wrongly block a restore after
        /// the game's own save data was lost.
        ///
        /// Taking an entitlement away is held to a stricter standard than granting one:
        ///
        /// <code>
        /// - Only a complete snapshot can revoke. A store SDK may also hand over a single order
        ///   through the same callback. For such a partial list the orders are granted and
        ///   remembered as owned, and nothing else changes.
        /// - A product whose order is still unfinished in the same fetch counts as owned if that
        ///   order passes the trust gate. The store lists it as pending, not as owned, until the
        ///   order is confirmed.
        /// - A product has to be missing from two complete snapshots in a row.
        /// - An empty snapshot revokes nothing. See OwnedSetDiff.
        /// - A product that has left the catalogue is never revoked.
        /// </code>
        /// </summary>
        /// <param name="ownedOrders">The orders the store reports as owned (confirmed).</param>
        /// <param name="unfinishedOrders">The pending orders in the same fetch, if any.</param>
        /// <param name="completeSnapshot">
        /// True when the list is the answer to a purchase fetch the caller asked for. False for
        /// anything else, which is then never used to revoke.
        /// </param>
        public OwnedSyncReport ProcessOwned(
            IReadOnlyList<IStoreOrder> ownedOrders,
            IReadOnlyList<IStoreOrder> unfinishedOrders,
            bool completeSnapshot)
        {
            var report = new OwnedSyncReport();
            var owned = new HashSet<string>(StringComparer.Ordinal);

            if (ownedOrders != null)
            {
                for (int i = 0; i < ownedOrders.Count; i++)
                {
                    IStoreOrder order = ownedOrders[i];
                    if (order != null)
                    {
                        ProcessOwnedOrder(order, owned, report);
                    }
                }
            }

            if (unfinishedOrders != null)
            {
                for (int i = 0; i < unfinishedOrders.Count; i++)
                {
                    IStoreOrder order = unfinishedOrders[i];
                    if (order != null && IsGenuineUnfinishedDurable(order))
                    {
                        owned.Add(order.ProductId);
                    }
                }
            }

            if (completeSnapshot)
            {
                RevokeNoLongerOwned(owned, report);
            }
            else
            {
                RememberAsOwned(owned);
            }

            return report;
        }

        private void ProcessOwnedOrder(IStoreOrder order, HashSet<string> owned, OwnedSyncReport report)
        {
            string productId = order.ProductId ?? string.Empty;

            // Consumables are granted on the purchase path only. If a store reports one as owned,
            // granting it here would hand it out again on every fetch.
            if (!IsDurable(productId))
            {
                report.Skipped++;
                return;
            }

            ReceiptCheck check = Judge(order, OrderPath.Restore);
            GateDecision decision = _policy.Decide(check.Verdict);
            if (decision == GateDecision.Hold)
            {
                report.Waiting++;
                return;
            }

            if (decision != GateDecision.Grant)
            {
                report.Refused++;
                Refuse(productId, check.Reason, OrderPath.Restore);
                return;
            }

            owned.Add(productId);

            try
            {
                if (_granter.IsEntitlementActive(productId))
                {
                    report.AlreadyActive++;
                    return;
                }

                _granter.Grant(productId, order.TransactionId ?? string.Empty, GrantSource.Restore);
                report.Granted++;
            }
            catch (Exception error)
            {
                report.Failed++;
                Report(telemetry => telemetry.GrantFailed(productId, OrderPath.Restore, error));
            }
        }

        // Whether an unfinished order is granted is decided on the pending path. Here it only
        // matters whether the order is real: a real unfinished order means the product is owned,
        // although the store does not list it as owned yet. A forged or unpaid one does not
        // count, so it cannot be used to keep a lapsed entitlement alive.
        private bool IsGenuineUnfinishedDurable(IStoreOrder order)
        {
            if (!IsDurable(order.ProductId))
            {
                return false;
            }

            return _policy.Decide(Judge(order, OrderPath.Purchase).Verdict) == GateDecision.Grant;
        }

        private bool IsDurable(string productId)
        {
            return _catalog.TryGet(productId, out CatalogEntry entry) && entry.Kind != ProductKind.Consumable;
        }

        private void RevokeNoLongerOwned(HashSet<string> owned, OwnedSyncReport report)
        {
            HashSet<string> baseline;
            HashSet<string> missingOnce;
            try
            {
                baseline = _ownedBaseline.Load();
                missingOnce = _ownedBaseline.LoadMissingOnce();
            }
            catch (Exception error)
            {
                // Without the baseline there is nothing to compare against, so nothing is revoked.
                Report(telemetry => telemetry.Warning("The owned-set baseline could not be read: " + error.Message));
                return;
            }

            OwnedSetComparison comparison = OwnedSetDiff.Compare(baseline, owned);
            if (comparison.EmptyFetchIgnored)
            {
                report.EmptyFetchIgnored = true;
                int previouslyOwned = baseline.Count;
                Report(telemetry => telemetry.EmptyOwnedFetchIgnored(previouslyOwned));
                return;
            }

            var stillMissing = new List<string>();
            var revoked = new List<string>();
            foreach (string productId in comparison.NoLongerOwned)
            {
                // An owned order for a product that has left the catalogue is skipped above, so its
                // absence from this fetch says nothing about ownership. It stays in the baseline.
                if (!IsDurable(productId))
                {
                    owned.Add(productId);
                    continue;
                }

                // Missing for the first time: note it and wait for the next complete snapshot. One
                // fetch is not enough to take a purchase away.
                if (!missingOnce.Contains(productId))
                {
                    owned.Add(productId);
                    stillMissing.Add(productId);
                    report.MissingOnce++;
                    continue;
                }

                try
                {
                    _granter.Revoke(productId);
                    revoked.Add(productId);
                }
                catch (Exception error)
                {
                    // Leave it as it was, so the next snapshot tries the revocation again.
                    owned.Add(productId);
                    stillMissing.Add(productId);
                    Report(telemetry => telemetry.Warning("Revoking '" + productId + "' failed: " + error.Message));
                }
            }

            if (revoked.Count > 0)
            {
                report.Revoked = revoked;
                Report(telemetry => telemetry.EntitlementsRevoked(revoked));
            }

            SaveBaseline(owned, stillMissing);
        }

        // A partial list says what is owned and nothing about what is not.
        private void RememberAsOwned(HashSet<string> owned)
        {
            if (owned.Count == 0)
            {
                return;
            }

            try
            {
                HashSet<string> baseline = _ownedBaseline.Load();
                HashSet<string> missingOnce = _ownedBaseline.LoadMissingOnce();
                baseline.UnionWith(owned);
                missingOnce.ExceptWith(owned);
                SaveBaseline(baseline, missingOnce);
            }
            catch (Exception error)
            {
                Report(telemetry => telemetry.Warning("The owned-set baseline could not be read: " + error.Message));
            }
        }

        private void SaveBaseline(IEnumerable<string> owned, IEnumerable<string> missingOnce)
        {
            try
            {
                _ownedBaseline.Save(owned, missingOnce);
            }
            catch (Exception error)
            {
                Report(telemetry => telemetry.Warning("The owned-set baseline could not be saved: " + error.Message));
            }
        }

        private ReceiptCheck Judge(IStoreOrder order, OrderPath path)
        {
            ReceiptCheck check;

            if (_options.RejectTestStoreOrders
                && string.Equals(order.StoreName, _options.TestStoreName, StringComparison.OrdinalIgnoreCase))
            {
                check = ReceiptCheck.Invalid(VerdictReason.TestStoreReceipt);
            }
            else if (path == OrderPath.Purchase && string.IsNullOrEmpty(order.TransactionId))
            {
                // A pending order with no transaction id cannot be deduplicated or confirmed, and no
                // real store produces one.
                check = ReceiptCheck.Invalid(VerdictReason.TransactionIdMissing);
            }
            else
            {
                try
                {
                    check = _gate.Check(order);
                }
                catch (Exception error)
                {
                    // The gate reads data the other side controls. A failure nobody anticipated is a
                    // refusal, never a free grant.
                    check = ReceiptCheck.Invalid(VerdictReason.GateFailed);
                    Report(telemetry => telemetry.Warning("The receipt gate threw: " + error.Message));
                }
            }

            if (check.Verdict == ReceiptVerdict.Unavailable && !_unavailableReported)
            {
                _unavailableReported = true;
                VerdictReason reason = check.Reason;
                Report(telemetry => telemetry.ValidationUnavailable(reason));
            }

            return check;
        }

        private void Refuse(string productId, VerdictReason reason, OrderPath path)
        {
            Report(telemetry => telemetry.OrderRefused(productId, reason, path));
            if (path == OrderPath.Purchase)
            {
                Notify(ui => ui.PurchaseRefused(productId, reason));
            }
        }

        private void Remember(string transactionId)
        {
            try
            {
                _ledger.Add(transactionId);
            }
            catch (Exception error)
            {
                Report(telemetry => telemetry.Warning("The transaction ledger could not be saved: " + error.Message));
            }
        }

        private void Confirm(IStoreOrder order)
        {
            try
            {
                _confirmer.Confirm(order);
            }
            catch (Exception error)
            {
                // The order stays pending and is delivered again; the ledger keeps that idempotent.
                Report(telemetry => telemetry.Warning("Confirming the order threw: " + error.Message));
            }
        }

        private void Notify(Action<IPurchaseUi> call)
        {
            try
            {
                call(_ui);
            }
            catch (Exception error)
            {
                Report(telemetry => telemetry.Warning("A purchase UI callback threw: " + error.Message));
            }
        }

        private void Report(Action<IIapTelemetry> call)
        {
            try
            {
                call(_telemetry);
            }
            catch (Exception)
            {
                // Telemetry must never change the outcome of a purchase, and there is nowhere left
                // to report its own failure.
            }
        }
    }
}
