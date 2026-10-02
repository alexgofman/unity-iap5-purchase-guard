using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Runs <see cref="PurchaseSession"/> on the Unity IAP 5 <c>StoreController</c>.
    ///
    /// This class only translates. SDK events become calls into the session, and the session's
    /// requests become SDK calls. Every decision about granting, confirming and revoking lives in
    /// the core assembly, where it is covered by tests that run without the engine. What is left
    /// here is the wiring and the reconnect loop.
    ///
    /// Only one pipeline may be live at a time: two would each receive the same store order and
    /// each grant it. The one static field that enforces this is cleared by <see cref="Shutdown"/>
    /// and on subsystem registration, so it also works with domain reload disabled. Use the
    /// pipeline from the main thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PurchasePipeline : MonoBehaviour, IStoreCommands
    {
        private static PurchasePipeline s_live;

        private PurchaseGuardSetup _setup;
        private BackoffSchedule _backoff;
        private PurchaseSession _session;
        private StoreController _controller;
        private IIapTelemetry _telemetry;
        private bool _connected;
        private bool _connectLoopRunning;
        private bool _lastFailureRetryable;

        /// <summary>Raised when the store has returned product data (prices, titles).</summary>
        public event Action<IReadOnlyList<Product>> ProductsUpdated;

        /// <summary>Raised after owned purchases were processed: a launch sync or a restore.</summary>
        public event Action<OwnedSyncReport> OwnedPurchasesSynced;

        /// <summary>Raised when a restore the player asked for could not be carried out.</summary>
        public event Action<string> RestoreFailed;

        /// <summary>True while the SDK reports a live store connection.</summary>
        public bool IsReady
        {
            get { return _controller != null && _connected; }
        }

        /// <summary>How this copy of the app was installed. See <see cref="InstallSourcePolicy"/>.</summary>
        public InstallSourceReport InstallReport { get; private set; }

        bool IStoreCommands.IsConnected
        {
            get { return IsReady; }
        }

        /// <summary>Creates a pipeline on its own object that survives scene loads, and starts it.</summary>
        public static PurchasePipeline Create(PurchaseGuardSetup setup)
        {
            if (setup == null)
            {
                throw new ArgumentNullException(nameof(setup));
            }

            var host = new GameObject(nameof(PurchasePipeline));
            DontDestroyOnLoad(host);
            PurchasePipeline pipeline = host.AddComponent<PurchasePipeline>();
            try
            {
                pipeline.Initialize(setup);
            }
            catch
            {
                Destroy(host);
                throw;
            }

            return pipeline;
        }

        /// <summary>Wires the pipeline to the store and starts connecting. Call it once.</summary>
        public void Initialize(PurchaseGuardSetup setup)
        {
            if (setup == null)
            {
                throw new ArgumentNullException(nameof(setup));
            }

            if (_session != null)
            {
                throw new InvalidOperationException("The pipeline is already initialised.");
            }

            if (setup.Catalog == null || setup.Granter == null)
            {
                throw new ArgumentException("A catalogue and an entitlement granter are required.", nameof(setup));
            }

            if (s_live != null && s_live != this)
            {
                throw new InvalidOperationException("Another PurchasePipeline is already running. Two pipelines would each grant the same order.");
            }

            _setup = setup;
            _backoff = setup.Reconnect ?? new BackoffSchedule();
            _telemetry = setup.Telemetry ?? new DebugLogTelemetry();

            RuntimePlatform platform = Application.platform;
            IStringStore storage = setup.Storage ?? new PlayerPrefsStore();
            string prefix = string.IsNullOrEmpty(setup.StorageKeyPrefix)
                ? PurchaseGuardSetup.DefaultStorageKeyPrefix
                : setup.StorageKeyPrefix;

            InstallSourceReport install = InstallSource.Detect(setup.ExpectedInstallers);
            InstallReport = install;
            Report(telemetry => telemetry.InstallSourceDetected(install.InstallerName, install.IsExpected));

            bool android = platform == RuntimePlatform.Android;
            bool apple = platform == RuntimePlatform.IPhonePlayer || platform == RuntimePlatform.tvOS;
            bool storeKit1 = apple && AppleSystem.IsOlderThanStoreKit2(SystemInfo.operatingSystem);
            if (storeKit1)
            {
                Warn("This system is older than iOS/tvOS 15, where Unity IAP falls back to StoreKit 1. Transactions are not verified there and restored purchases arrive as new orders. This package does not cover StoreKit 1.");
            }

            bool processOwnedOnEveryFetch = setup.OwnedSync == OwnedSync.OnConnectAndRestore
                || (setup.OwnedSync == OwnedSync.PlatformDefault && android);

            var session = new PurchaseSession(
                setup.Catalog,
                new TransactionLedger(storage, prefix + "ledger", setup.LedgerCapacity),
                new OwnedSetBaseline(storage, prefix + "owned"),
                setup.ReceiptGate ?? ReceiptGates.ForPlatform(platform, setup.PlayLicensingPublicKeyBase64, Application.identifier, storeKit1),
                new VerdictPolicy(setup.WhenValidationUnavailable),
                setup.Granter,
                this,
                setup.Ui,
                _telemetry,
                new PurchaseFlowOptions { RejectTestStoreOrders = BuildFlavor.IsReleaseBuild },
                new PurchaseSessionOptions
                {
                    // Google Play hands back unconfirmed orders when purchases are queried, so on
                    // Android the fetch after connecting is not optional, whatever is then done
                    // with the owned orders.
                    FetchPurchasesOnConnect = android || processOwnedOnEveryFetch,
                    ProcessOwnedOnEveryFetch = processOwnedOnEveryFetch,
                    BlockPurchases = setup.BlockPurchasesFromUnexpectedInstaller && !install.IsExpected,
                    BlockDetails = "installer: " + install.InstallerName
                });
            session.OwnedPurchasesSynced += report => Raise(OwnedPurchasesSynced, report);
            session.RestoreFailed += message => Raise(RestoreFailed, message);

            StoreController controller = UnityIAPServices.StoreController(StoreSelection.ForPlatform(platform));
            _session = session;
            _controller = controller;
            _controller.OnStoreConnected += HandleStoreConnected;
            _controller.OnStoreDisconnected += HandleStoreDisconnected;
            _controller.OnProductsFetched += HandleProductsFetched;
            _controller.OnProductsFetchFailed += HandleProductsFetchFailed;
            _controller.OnPurchasePending += HandlePendingOrder;
            _controller.OnPurchaseConfirmed += HandleConfirmResult;
            _controller.OnPurchaseFailed += HandlePurchaseFailed;
            _controller.OnPurchaseDeferred += HandlePurchaseDeferred;
            _controller.OnPurchasesFetched += HandlePurchasesFetched;
            _controller.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;

            s_live = this;
            EnsureConnected();
        }

        /// <summary>
        /// Stops the pipeline at once: store events are no longer handled, nothing can be bought
        /// through it, and another pipeline may be created. Destroying the object does the same,
        /// but only at the end of the frame.
        /// </summary>
        public void Shutdown()
        {
            if (s_live == this)
            {
                s_live = null;
            }

            if (_controller == null)
            {
                return;
            }

            _controller.OnStoreConnected -= HandleStoreConnected;
            _controller.OnStoreDisconnected -= HandleStoreDisconnected;
            _controller.OnProductsFetched -= HandleProductsFetched;
            _controller.OnProductsFetchFailed -= HandleProductsFetchFailed;
            _controller.OnPurchasePending -= HandlePendingOrder;
            _controller.OnPurchaseConfirmed -= HandleConfirmResult;
            _controller.OnPurchaseFailed -= HandlePurchaseFailed;
            _controller.OnPurchaseDeferred -= HandlePurchaseDeferred;
            _controller.OnPurchasesFetched -= HandlePurchasesFetched;
            _controller.OnPurchasesFetchFailed -= HandlePurchasesFetchFailed;
            _controller = null;
            _connected = false;
            _session.Reset();
        }

        /// <summary>
        /// Starts connecting when the store is not connected and no attempt is running. Buy and
        /// RestorePurchases call it, so a player action restarts the automatic retries after they
        /// have run out.
        /// </summary>
        public void EnsureConnected()
        {
            if (_controller == null || _connected || _connectLoopRunning)
            {
                return;
            }

            _connectLoopRunning = true;
            _ = RunConnectLoopAsync(destroyCancellationToken);
        }

        /// <summary>
        /// Starts a purchase. Returns false when it could not be started; the UI has then been told
        /// why. The result of a started purchase arrives through <see cref="IPurchaseUi"/>.
        /// </summary>
        public bool Buy(string productId)
        {
            RequireInitialised();
            return _session.Buy(productId);
        }

        /// <summary>
        /// Asks the store for the purchases the account owns. Call it from a Restore button. The
        /// result arrives through <see cref="OwnedPurchasesSynced"/> or <see cref="RestoreFailed"/>.
        /// If the store never answers, neither is raised, so give the button its own time limit.
        /// </summary>
        public void RestorePurchases()
        {
            RequireInitialised();
            _session.RestorePurchases();
        }

        /// <summary>The store's product data (price, title) for a catalogue id, or null.</summary>
        public Product FindProduct(string productId)
        {
            return _controller != null ? _controller.GetProductById(productId) : null;
        }

        // ---- what the session asks of the store ----

        void IStoreCommands.RequestConnection()
        {
            EnsureConnected();
        }

        void IStoreCommands.FetchProducts()
        {
            if (_controller != null)
            {
                _controller.FetchProducts(_setup.Catalog.ToProductDefinitions());
            }
        }

        void IStoreCommands.FetchPurchases()
        {
            if (_controller != null)
            {
                _controller.FetchPurchases();
            }
        }

        void IStoreCommands.RestoreTransactions()
        {
            if (_controller == null)
            {
                _session.RestoreRejected("The pipeline was shut down.");
                return;
            }

            // On success Unity IAP fetches the purchases itself; they arrive in HandlePurchasesFetched.
            _controller.RestoreTransactions((succeeded, error) =>
            {
                if (!succeeded)
                {
                    _session.RestoreRejected(error);
                }
            });
        }

        bool IStoreCommands.BeginPurchase(string productId)
        {
            Product product = _controller != null ? _controller.GetProductById(productId) : null;
            if (product == null || !product.availableToPurchase)
            {
                return false;
            }

            _controller.PurchaseProduct(product);
            return true;
        }

        void IStoreCommands.Confirm(IStoreOrder order)
        {
            if (_controller != null && order is UnityStoreOrder wrapped && wrapped.Native is PendingOrder pending)
            {
                _controller.ConfirmPurchase(pending);
            }
        }

        // ---- the reconnect loop ----

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ForgetLivePipeline()
        {
            s_live = null;
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        // One loop owns connecting and reconnecting. It ends when the store is connected, when the
        // failure cannot be retried, when the schedule is used up, or when the pipeline is shut down.
        private async Awaitable RunConnectLoopAsync(CancellationToken destroyed)
        {
            try
            {
                // This can be started from the SDK's own disconnect callback. Connecting again from
                // inside that callback would be re-entrant, so wait for the next frame first.
                await Awaitable.NextFrameAsync(destroyed);

                for (int attempt = 0; !_connected; attempt++)
                {
                    StoreController controller = _controller;
                    if (controller == null)
                    {
                        break;
                    }

                    _lastFailureRetryable = true;

                    // In Unity IAP 5 this task completes when the attempt is over, whether or not it
                    // connected. The result arrives through OnStoreConnected and OnStoreDisconnected.
                    await controller.Connect();
                    destroyed.ThrowIfCancellationRequested();

                    if (_connected || _controller == null)
                    {
                        break;
                    }

                    if (!_lastFailureRetryable)
                    {
                        Warn("The store connection failed and cannot be retried.");
                        break;
                    }

                    if (!_backoff.TryGetDelay(attempt, out double delaySeconds))
                    {
                        Warn("Automatic reconnect attempts are used up. The next purchase or restore tries again.");
                        break;
                    }

                    await WaitRealtimeAsync(delaySeconds, destroyed);
                }
            }
            catch (OperationCanceledException)
            {
                // The component was destroyed while waiting.
            }
            catch (Exception error)
            {
                Warn("The store connect loop stopped: " + error.Message);
            }
            finally
            {
                _connectLoopRunning = false;
            }
        }

        // Measured in real time, so a game paused with a time scale of zero still reconnects.
        private static async Awaitable WaitRealtimeAsync(double seconds, CancellationToken cancellation)
        {
            double resumeAt = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < resumeAt)
            {
                await Awaitable.NextFrameAsync(cancellation);
            }
        }

        // ---- SDK events, translated ----

        private void HandleStoreConnected()
        {
            _connected = true;
            _session.StoreConnected();
        }

        private void HandleStoreDisconnected(StoreConnectionFailureDescription failure)
        {
            _connected = false;
            _lastFailureRetryable = failure == null || failure.IsRetryable;
            Warn("The store disconnected: " + (failure != null ? failure.Message : "no description"));

            if (_lastFailureRetryable)
            {
                EnsureConnected();
            }
        }

        private void HandleProductsFetched(List<Product> products)
        {
            _session.ProductsFetched();
            Raise<IReadOnlyList<Product>>(ProductsUpdated, products);
        }

        private void HandleProductsFetchFailed(ProductFetchFailed failure)
        {
            int count = failure != null && failure.FailedFetchProducts != null ? failure.FailedFetchProducts.Count : 0;
            Warn("Fetching products failed for " + count + " product(s): " + (failure != null ? failure.FailureReason : "no description"));
        }

        private void HandlePendingOrder(PendingOrder pending)
        {
            UnityStoreOrder order;
            try
            {
                order = new UnityStoreOrder(pending);
            }
            catch (Exception error)
            {
                _session.PendingOrderUnreadable(error.Message);
                return;
            }

            _session.PendingOrderDelivered(order);
        }

        // Raised for every ConfirmPurchase call: a ConfirmedOrder on success, a FailedOrder when the
        // store could not acknowledge the order.
        private void HandleConfirmResult(Order order)
        {
            string transactionId = order != null && order.Info != null ? order.Info.TransactionID : null;
            if (order is FailedOrder failed)
            {
                _session.ConfirmFailed(transactionId, failed.FailureReason.ToString());
            }
            else
            {
                _session.ConfirmSucceeded(transactionId);
            }
        }

        private void HandlePurchaseFailed(FailedOrder failed)
        {
            _session.PurchaseFailed(ProductIdOf(failed), KindOf(failed.FailureReason), failed.FailureReason + ": " + failed.Details);
        }

        private void HandlePurchaseDeferred(DeferredOrder deferred)
        {
            _session.PurchaseDeferred(ProductIdOf(deferred));
        }

        private void HandlePurchasesFetched(Orders orders)
        {
            List<IStoreOrder> owned;
            List<IStoreOrder> unfinished;
            try
            {
                owned = Wrap(orders != null ? orders.ConfirmedOrders : null);
                unfinished = Wrap(orders != null ? orders.PendingOrders : null);
            }
            catch (Exception error)
            {
                _session.PurchasesFetchFailed("the fetched orders could not be read: " + error.Message);
                return;
            }

            _session.PurchasesFetched(owned, unfinished);
        }

        private void HandlePurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            _session.PurchasesFetchFailed(failure != null ? failure.FailureReason + ": " + failure.Message : "no description");
        }

        private static List<IStoreOrder> Wrap<TOrder>(IReadOnlyList<TOrder> orders) where TOrder : Order
        {
            var wrapped = new List<IStoreOrder>();
            if (orders != null)
            {
                for (int i = 0; i < orders.Count; i++)
                {
                    wrapped.Add(new UnityStoreOrder(orders[i]));
                }
            }

            return wrapped;
        }

        private static string ProductIdOf(Order order)
        {
            try
            {
                return new UnityStoreOrder(order).ProductId;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static PurchaseFailureKind KindOf(PurchaseFailureReason reason)
        {
            switch (reason)
            {
                case PurchaseFailureReason.UserCancelled:
                    return PurchaseFailureKind.Cancelled;
                case PurchaseFailureReason.DuplicateTransaction:
                    return PurchaseFailureKind.AlreadyOwned;
                case PurchaseFailureReason.PaymentDeclined:
                    return PurchaseFailureKind.PaymentDeclined;
                case PurchaseFailureReason.ProductUnavailable:
                    return PurchaseFailureKind.ProductUnavailable;
                case PurchaseFailureReason.PurchasingUnavailable:
                case PurchaseFailureReason.StoreNotConnected:
                    return PurchaseFailureKind.StoreNotReady;
                default:
                    return PurchaseFailureKind.Other;
            }
        }

        private void RequireInitialised()
        {
            if (_session == null)
            {
                throw new InvalidOperationException("Call Initialize before using the pipeline.");
            }
        }

        private void Warn(string message)
        {
            Report(telemetry => telemetry.Warning(message));
        }

        // Subscribers run inside SDK callbacks. One that throws must not unwind into the SDK.
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
