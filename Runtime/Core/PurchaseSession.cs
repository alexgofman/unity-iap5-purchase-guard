using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>What the session asks of the store. Implemented by the SDK adapter.</summary>
    public interface IStoreCommands
    {
        /// <summary>True while the store connection is live.</summary>
        bool IsConnected { get; }

        /// <summary>Starts connecting if no attempt is running.</summary>
        void RequestConnection();

        /// <summary>Asks the store for the product data of the catalogue.</summary>
        void FetchProducts();

        /// <summary>
        /// Asks the store for the account's purchases. The answer comes back through
        /// <see cref="PurchaseSession.PurchasesFetched"/> or <see cref="PurchaseSession.PurchasesFetchFailed"/>.
        /// </summary>
        void FetchPurchases();

        /// <summary>
        /// Asks the store to restore purchases. A refusal comes back through
        /// <see cref="PurchaseSession.RestoreRejected"/>; after a success the purchases arrive
        /// through <see cref="PurchaseSession.PurchasesFetched"/>.
        /// </summary>
        void RestoreTransactions();

        /// <summary>
        /// Opens the store's purchase dialog. Returns false when the store does not know the
        /// product or cannot sell it right now.
        /// </summary>
        bool BeginPurchase(string productId);

        /// <summary>
        /// Tells the store an order is fulfilled. The result comes back through
        /// <see cref="PurchaseSession.ConfirmSucceeded"/> or <see cref="PurchaseSession.ConfirmFailed"/>.
        /// </summary>
        void Confirm(IStoreOrder order);
    }

    /// <summary>Settings for <see cref="PurchaseSession"/>.</summary>
    public sealed class PurchaseSessionOptions
    {
        /// <summary>
        /// Fetch purchases after every store connection, once the products are known. On Google
        /// Play this is how unconfirmed orders come back, so it should be on there.
        /// </summary>
        public bool FetchPurchasesOnConnect { get; set; }

        /// <summary>
        /// Grant and revoke from every purchase fetch. When off, owned orders are only processed
        /// after the player asked for a restore.
        /// </summary>
        public bool ProcessOwnedOnEveryFetch { get; set; }

        /// <summary>Refuse to start purchases, for example on a copy from an unexpected installer.</summary>
        public bool BlockPurchases { get; set; }

        /// <summary>Why purchases are blocked. Passed to telemetry.</summary>
        public string BlockDetails { get; set; }
    }

    /// <summary>
    /// The conversation with the store around <see cref="PurchaseFlow"/>: what was asked for, what
    /// is still in flight, and which answer belongs to which request. It has no SDK types, so the
    /// adapter that feeds it stays a translator and this logic runs under plain tests.
    ///
    /// What it keeps track of:
    ///
    /// <code>
    /// - Which fetch answers are complete snapshots. Only the answer to a fetch this session asked
    ///   for may be used to revoke; anything else the store passes along is granted from only.
    /// - Whether a restore is in progress, so that in restore-only mode nothing is granted or
    ///   revoked unless the player asked.
    /// - Which purchase the player just started, so that it always ends in a UI call, also when
    ///   the store answers with an order that had been granted before.
    /// - Which confirmations the store has not acknowledged, so that a failed one is sent again.
    ///   A store hands an unconfirmed order to the app once per session, and until a consumable
    ///   is confirmed it cannot be bought again.
    /// </code>
    /// </summary>
    /// <remarks>Not thread-safe. Call it from the thread that receives the store callbacks.</remarks>
    public sealed class PurchaseSession : IOrderConfirmer
    {
        private readonly Dictionary<string, IStoreOrder> _awaitingConfirmation = new Dictionary<string, IStoreOrder>(StringComparer.Ordinal);
        private readonly PurchaseCatalog _catalog;
        private readonly PurchaseFlow _flow;
        private readonly IStoreCommands _store;
        private readonly IPurchaseUi _ui;
        private readonly IIapTelemetry _telemetry;
        private readonly PurchaseSessionOptions _options;
        private bool _fetchDue;
        private bool _snapshotRequested;
        private bool _restoreRequested;
        private string _buyInFlight;

        public PurchaseSession(
            PurchaseCatalog catalog,
            TransactionLedger ledger,
            OwnedSetBaseline ownedBaseline,
            IReceiptGate gate,
            VerdictPolicy policy,
            IEntitlementGranter granter,
            IStoreCommands store,
            IPurchaseUi ui = null,
            IIapTelemetry telemetry = null,
            PurchaseFlowOptions flowOptions = null,
            PurchaseSessionOptions options = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _ui = ui ?? NullPurchaseUi.Instance;
            _telemetry = telemetry ?? new NullIapTelemetry();
            _options = options ?? new PurchaseSessionOptions();
            _flow = new PurchaseFlow(catalog, ledger, ownedBaseline, gate, policy, granter, this, _ui, _telemetry, flowOptions);
        }

        /// <summary>Raised after owned purchases were processed: a launch sync or a restore.</summary>
        public event Action<OwnedSyncReport> OwnedPurchasesSynced;

        /// <summary>Raised when a restore the player asked for could not be carried out.</summary>
        public event Action<string> RestoreFailed;

        // ---- requests from the game ----

        /// <summary>
        /// Starts a purchase. Returns false when it could not be started; the UI has then been told
        /// why. The result of a started purchase arrives through <see cref="IPurchaseUi"/>.
        /// </summary>
        public bool Buy(string productId)
        {
            if (_options.BlockPurchases)
            {
                FailBeforeStore(productId, PurchaseFailureKind.UnofficialInstall, _options.BlockDetails);
                return false;
            }

            // The flow would leave an order for an unknown product unconfirmed, so it must not be
            // possible to pay for one in the first place.
            if (!_catalog.Contains(productId))
            {
                FailBeforeStore(productId, PurchaseFailureKind.ProductUnavailable, "not in the catalogue");
                return false;
            }

            if (!_store.IsConnected)
            {
                _store.RequestConnection();
                FailBeforeStore(productId, PurchaseFailureKind.StoreNotReady, "the store is not connected");
                return false;
            }

            _buyInFlight = productId;
            if (!_store.BeginPurchase(productId))
            {
                _buyInFlight = null;
                FailBeforeStore(productId, PurchaseFailureKind.ProductUnavailable, "the store cannot sell this product now");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Asks the store for the purchases the account owns. The result arrives through
        /// <see cref="OwnedPurchasesSynced"/> or <see cref="RestoreFailed"/>. If the store never
        /// answers, neither is raised.
        /// </summary>
        public void RestorePurchases()
        {
            if (!_store.IsConnected)
            {
                _store.RequestConnection();
                Raise(RestoreFailed, "The store is not connected.");
                return;
            }

            _restoreRequested = true;
            _snapshotRequested = true;
            _store.RestoreTransactions();
        }

        /// <summary>Forgets everything that is in flight. For shutting the adapter down.</summary>
        public void Reset()
        {
            _fetchDue = false;
            _snapshotRequested = false;
            _restoreRequested = false;
            _buyInFlight = null;
            _awaitingConfirmation.Clear();
        }

        // ---- events from the store ----

        public void StoreConnected()
        {
            _fetchDue = _options.FetchPurchasesOnConnect;
            _store.FetchProducts();
            ConfirmAgain();
        }

        /// <summary>
        /// Purchases are requested once the products are known, so their orders resolve to
        /// catalogue ids.
        /// </summary>
        public void ProductsFetched()
        {
            if (_fetchDue && _store.IsConnected)
            {
                _fetchDue = false;
                FetchSnapshot();
            }
        }

        public void PendingOrderDelivered(IStoreOrder order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            string productId = order.ProductId ?? string.Empty;
            bool answersBuy = TakeBuyInFlight(productId);
            try
            {
                PendingOutcome outcome = _flow.ProcessPending(order);

                // The flow confirms an order it has already granted without telling the player. A
                // store can hand such an order over as the answer to a purchase the player just
                // started, and the paywall is then still waiting for a result.
                if (answersBuy && outcome == PendingOutcome.AlreadyGranted)
                {
                    Notify(ui => ui.PurchaseFailed(productId, PurchaseFailureKind.AlreadyOwned));
                }
            }
            catch (Exception error)
            {
                // The flow wraps the grant, the confirm, the UI and the telemetry itself, so this is
                // storage failing before anything was granted. The order stays unconfirmed. Only a
                // paywall that is waiting needs to hear about it.
                Warn("Handling a pending order threw, so it was left unconfirmed: " + error.Message);
                if (answersBuy)
                {
                    Notify(ui => ui.PurchaseFailed(productId, PurchaseFailureKind.Other));
                }
            }
        }

        /// <summary>The adapter could not read a pending order at all. It stays unconfirmed.</summary>
        public void PendingOrderUnreadable(string details)
        {
            Warn("A pending order could not be read, so it was left unconfirmed: " + details);
            if (_buyInFlight != null)
            {
                string productId = _buyInFlight;
                _buyInFlight = null;
                Notify(ui => ui.PurchaseFailed(productId, PurchaseFailureKind.Other));
            }
        }

        public void PurchaseFailed(string productId, PurchaseFailureKind kind, string details)
        {
            _buyInFlight = null;
            Report(telemetry => telemetry.PurchaseFailed(productId, kind, details));
            Notify(ui => ui.PurchaseFailed(productId, kind));

            // The store says the product is already owned. That is either an order whose
            // confirmation failed earlier, which is sent again, or one this session has not seen,
            // which a fetch brings in. Where owned orders are only processed on restore, the player
            // has to ask for a restore instead.
            if (kind == PurchaseFailureKind.AlreadyOwned && _store.IsConnected)
            {
                ConfirmAgain();
                if (_options.FetchPurchasesOnConnect)
                {
                    FetchSnapshot();
                }
            }
        }

        /// <summary>
        /// A deferred purchase (a slow or offline payment method, or one waiting for approval) stays
        /// with the store until the payment completes, then arrives as a normal pending order.
        /// </summary>
        public void PurchaseDeferred(string productId)
        {
            _buyInFlight = null;
            Notify(ui => ui.PurchasePending(productId));
        }

        /// <param name="owned">The orders the store reports as owned (confirmed).</param>
        /// <param name="unfinished">The pending orders in the same answer.</param>
        public void PurchasesFetched(IReadOnlyList<IStoreOrder> owned, IReadOnlyList<IStoreOrder> unfinished)
        {
            // Only the answer to a fetch this session asked for is treated as the complete list of
            // what is owned. A store SDK can also pass a single order through the same callback,
            // and a list like that must never be used to revoke.
            bool restoring = _restoreRequested;
            bool complete = _snapshotRequested;
            _restoreRequested = false;
            _snapshotRequested = false;

            // In restore-only mode no owned order is granted or revoked unless the player asked.
            if (!restoring && !_options.ProcessOwnedOnEveryFetch)
            {
                return;
            }

            OwnedSyncReport report;
            try
            {
                report = _flow.ProcessOwned(owned, unfinished, complete);
            }
            catch (Exception error)
            {
                Warn("Processing owned purchases threw: " + error.Message);
                if (restoring)
                {
                    Raise(RestoreFailed, "Owned purchases could not be processed.");
                }

                return;
            }

            Raise(OwnedPurchasesSynced, report);
        }

        /// <summary>A failed fetch never reaches the owned-set diff, so it can never revoke anything.</summary>
        public void PurchasesFetchFailed(string message)
        {
            Warn("Fetching purchases failed: " + message);
            _snapshotRequested = false;
            FailRestore(message);
        }

        public void RestoreRejected(string message)
        {
            FailRestore(string.IsNullOrEmpty(message) ? "The store could not restore purchases." : message);
        }

        public void ConfirmSucceeded(string transactionId)
        {
            if (!string.IsNullOrEmpty(transactionId))
            {
                _awaitingConfirmation.Remove(transactionId);
            }
        }

        public void ConfirmFailed(string transactionId, string reason)
        {
            Warn("Confirming an order failed (" + reason + "). It is sent again after the next reconnect, and the store delivers the order again at the next launch; the ledger stops a second grant.");
        }

        void IOrderConfirmer.Confirm(IStoreOrder order)
        {
            // Kept until the store reports the confirmation, so a failed one can be sent again.
            string transactionId = order.TransactionId ?? string.Empty;
            if (transactionId.Length > 0)
            {
                _awaitingConfirmation[transactionId] = order;
            }

            _store.Confirm(order);
        }

        private void ConfirmAgain()
        {
            if (_awaitingConfirmation.Count == 0 || !_store.IsConnected)
            {
                return;
            }

            foreach (IStoreOrder order in new List<IStoreOrder>(_awaitingConfirmation.Values))
            {
                try
                {
                    _store.Confirm(order);
                }
                catch (Exception error)
                {
                    Warn("Confirming an order again threw: " + error.Message);
                }
            }
        }

        private void FetchSnapshot()
        {
            _snapshotRequested = true;
            _store.FetchPurchases();
        }

        private bool TakeBuyInFlight(string productId)
        {
            if (_buyInFlight == null || !string.Equals(_buyInFlight, productId, StringComparison.Ordinal))
            {
                return false;
            }

            _buyInFlight = null;
            return true;
        }

        private void FailRestore(string message)
        {
            if (!_restoreRequested)
            {
                return;
            }

            _restoreRequested = false;
            _snapshotRequested = false;
            Raise(RestoreFailed, message);
        }

        private void FailBeforeStore(string productId, PurchaseFailureKind kind, string details)
        {
            Report(telemetry => telemetry.PurchaseFailed(productId, kind, details));
            Notify(ui => ui.PurchaseFailed(productId, kind));
        }

        private void Warn(string message)
        {
            Report(telemetry => telemetry.Warning(message));
        }

        // Subscribers run inside store callbacks. One that throws must not unwind into the SDK.
        private void Raise<T>(Action<T> handler, T argument)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(argument);
            }
            catch (Exception error)
            {
                Warn("An event subscriber threw: " + error.Message);
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
                Warn("A purchase UI callback threw: " + error.Message);
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
                // Telemetry must never change the outcome of a purchase.
            }
        }
    }
}
