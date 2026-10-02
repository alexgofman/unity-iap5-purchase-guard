using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard.Tests
{
    /// <summary>The fictional three-product catalogue the tests sell.</summary>
    internal static class TestCatalog
    {
        public const string LanternOil = "lantern_oil_100";        // consumable
        public const string MapExpansion = "map_expansion";        // non-consumable
        public const string ExplorerClub = "explorer_club_monthly"; // subscription

        public static PurchaseCatalog Create()
        {
            return new PurchaseCatalog(new[]
            {
                new CatalogEntry(LanternOil, ProductKind.Consumable),
                new CatalogEntry(MapExpansion, ProductKind.NonConsumable),
                new CatalogEntry(ExplorerClub, ProductKind.Subscription)
            });
        }
    }

    internal sealed class FakeOrder : IStoreOrder
    {
        public string TransactionId { get; set; } = string.Empty;

        public string ProductId { get; set; } = string.Empty;

        public string StoreProductId { get; set; } = string.Empty;

        public string StoreName { get; set; } = "GooglePlay";

        public string Receipt { get; set; } = "signed-purchase-data";

        public bool Confirmed { get; set; }
    }

    /// <summary>
    /// A store that behaves like a real one in the two ways the flow depends on: it keeps
    /// delivering an order until that order is confirmed, and a confirmation can get lost.
    /// </summary>
    internal sealed class FakeStore : IOrderConfirmer
    {
        private readonly List<FakeOrder> _unconfirmed = new List<FakeOrder>();
        private readonly List<string> _journal;

        public FakeStore(List<string> journal)
        {
            _journal = journal;
        }

        /// <summary>The confirm call is made but never reaches the store.</summary>
        public bool LoseConfirmations { get; set; }

        public bool ThrowOnConfirm { get; set; }

        public int ConfirmCalls { get; private set; }

        public IReadOnlyList<FakeOrder> Unconfirmed
        {
            get { return _unconfirmed; }
        }

        /// <summary>The player pays: the store now holds an unconfirmed order.</summary>
        public FakeOrder Purchase(string transactionId, string productId)
        {
            var order = new FakeOrder { TransactionId = transactionId, ProductId = productId, StoreProductId = productId };
            _unconfirmed.Add(order);
            return order;
        }

        public void Hold(FakeOrder order)
        {
            _unconfirmed.Add(order);
        }

        /// <summary>Delivers every unconfirmed order, as a store does after a purchase and at each launch.</summary>
        public List<PendingOutcome> Deliver(PurchaseFlow flow)
        {
            var outcomes = new List<PendingOutcome>();
            foreach (FakeOrder order in _unconfirmed.ToArray())
            {
                outcomes.Add(flow.ProcessPending(order));
            }

            return outcomes;
        }

        public void Confirm(IStoreOrder order)
        {
            ConfirmCalls++;
            _journal.Add("confirm:" + order.TransactionId);

            if (ThrowOnConfirm)
            {
                throw new InvalidOperationException("The store is offline.");
            }

            if (LoseConfirmations)
            {
                return;
            }

            var fake = (FakeOrder)order;
            fake.Confirmed = true;
            _unconfirmed.Remove(fake);
        }
    }

    internal sealed class ScriptedGate : IReceiptGate
    {
        private readonly List<string> _journal;

        public ScriptedGate(List<string> journal)
        {
            _journal = journal;
        }

        public ReceiptCheck Verdict { get; set; } = ReceiptCheck.Valid(VerdictReason.SignatureVerified);

        /// <summary>Verdicts for specific products; anything else gets <see cref="Verdict"/>.</summary>
        public Dictionary<string, ReceiptCheck> VerdictByProduct { get; } = new Dictionary<string, ReceiptCheck>();

        public Exception Failure { get; set; }

        public int Calls { get; private set; }

        public ReceiptCheck Check(IStoreOrder order)
        {
            Calls++;
            _journal.Add("gate");

            if (Failure != null)
            {
                throw Failure;
            }

            return VerdictByProduct.TryGetValue(order.ProductId, out ReceiptCheck specific) ? specific : Verdict;
        }
    }

    internal sealed class RecordingGranter : IEntitlementGranter
    {
        private readonly List<string> _journal;

        public RecordingGranter(List<string> journal)
        {
            _journal = journal;
        }

        /// <summary>Every grant, as "productId/source".</summary>
        public List<string> Grants { get; } = new List<string>();

        /// <summary>The transaction id passed with each grant.</summary>
        public List<string> GrantedTransactions { get; } = new List<string>();

        public List<string> Revocations { get; } = new List<string>();

        public HashSet<string> Active { get; } = new HashSet<string>();

        /// <summary>How many of the next grants throw.</summary>
        public int FailingGrants { get; set; }

        public HashSet<string> FailingRevocations { get; } = new HashSet<string>();

        public bool IsEntitlementActive(string productId)
        {
            return Active.Contains(productId);
        }

        public void Grant(string productId, string transactionId, GrantSource source)
        {
            if (FailingGrants > 0)
            {
                FailingGrants--;
                throw new InvalidOperationException("The save system is not ready.");
            }

            _journal.Add("grant:" + productId);
            Grants.Add(productId + "/" + source);
            GrantedTransactions.Add(transactionId);
            Active.Add(productId);
        }

        public void Revoke(string productId)
        {
            if (FailingRevocations.Contains(productId))
            {
                throw new InvalidOperationException("The save system is not ready.");
            }

            Revocations.Add(productId);
            Active.Remove(productId);
        }
    }

    internal sealed class RecordingUi : IPurchaseUi
    {
        private readonly List<string> _journal;

        public RecordingUi(List<string> journal)
        {
            _journal = journal;
        }

        public List<string> Messages { get; } = new List<string>();

        public bool Throws { get; set; }

        public void PurchaseSucceeded(string productId)
        {
            Record("succeeded:" + productId);
        }

        public void PurchaseRefused(string productId, VerdictReason reason)
        {
            Record("refused:" + productId + ":" + reason);
        }

        public void PurchasePending(string productId)
        {
            Record("pending:" + productId);
        }

        public void PurchaseFailed(string productId, PurchaseFailureKind kind)
        {
            Record("failed:" + productId + ":" + kind);
        }

        private void Record(string message)
        {
            Messages.Add(message);
            _journal.Add("ui:" + message);
            if (Throws)
            {
                throw new InvalidOperationException("The popup prefab is missing.");
            }
        }
    }

    internal sealed class RecordingTelemetry : IIapTelemetry
    {
        private readonly List<string> _journal;

        public RecordingTelemetry(List<string> journal)
        {
            _journal = journal;
        }

        public List<string> Events { get; } = new List<string>();

        public bool Throws { get; set; }

        public void PurchaseGranted(string productId, string transactionId)
        {
            Record("granted:" + productId + ":" + transactionId);
        }

        public void OrderRefused(string productId, VerdictReason reason, OrderPath path)
        {
            Record("refused:" + productId + ":" + reason + ":" + path);
        }

        public void ValidationUnavailable(VerdictReason reason)
        {
            Record("unavailable:" + reason);
        }

        public void GrantFailed(string productId, OrderPath path, Exception error)
        {
            Record("grant-failed:" + productId + ":" + path);
        }

        public void PurchaseFailed(string productId, PurchaseFailureKind kind, string details)
        {
            Record("purchase-failed:" + productId + ":" + kind);
        }

        public void EntitlementsRevoked(IReadOnlyList<string> productIds)
        {
            Record("revoked:" + string.Join(",", productIds));
        }

        public void EmptyOwnedFetchIgnored(int previouslyOwnedCount)
        {
            Record("empty-fetch-ignored:" + previouslyOwnedCount);
        }

        public void InstallSourceDetected(string installerName, bool expected)
        {
            Record("installer:" + installerName + ":" + expected);
        }

        public void Warning(string message)
        {
            Record("diagnostic");
        }

        public int Count(string prefix)
        {
            int count = 0;
            foreach (string entry in Events)
            {
                if (entry.StartsWith(prefix, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private void Record(string entry)
        {
            Events.Add(entry);
            _journal.Add("telemetry:" + entry);
            if (Throws)
            {
                throw new InvalidOperationException("The analytics SDK is not initialised.");
            }
        }
    }

    /// <summary>Records every write in the journal, so tests can see when persistence happens.</summary>
    internal sealed class JournalingStringStore : IStringStore
    {
        private readonly IStringStore _inner;
        private readonly List<string> _journal;

        public JournalingStringStore(IStringStore inner, List<string> journal)
        {
            _inner = inner;
            _journal = journal;
        }

        public string Read(string key)
        {
            return _inner.Read(key);
        }

        public void Write(string key, string value)
        {
            _journal.Add("save:" + key);
            _inner.Write(key, value);
        }
    }

    /// <summary>Storage whose next reads can be made to fail, as a flaky disk or cloud save would.</summary>
    internal sealed class FlakyStringStore : IStringStore
    {
        private readonly InMemoryStringStore _inner = new InMemoryStringStore();

        public int FailingReads { get; set; }

        public int Writes { get; private set; }

        public string Read(string key)
        {
            if (FailingReads > 0)
            {
                FailingReads--;
                throw new InvalidOperationException("The storage is not reachable.");
            }

            return _inner.Read(key);
        }

        public void Write(string key, string value)
        {
            Writes++;
            _inner.Write(key, value);
        }
    }

    /// <summary>Storage whose writes can be switched off and on again.</summary>
    internal sealed class SwitchableStringStore : IStringStore
    {
        private readonly IStringStore _inner;

        public SwitchableStringStore(IStringStore inner)
        {
            _inner = inner;
        }

        public bool FailWrites { get; set; }

        public string Read(string key)
        {
            return _inner.Read(key);
        }

        public void Write(string key, string value)
        {
            if (FailWrites)
            {
                throw new InvalidOperationException("The storage is full.");
            }

            _inner.Write(key, value);
        }
    }

    /// <summary>A store whose writes fail, to model storage that is full or unavailable.</summary>
    internal sealed class ReadOnlyStringStore : IStringStore
    {
        public string Read(string key)
        {
            return string.Empty;
        }

        public void Write(string key, string value)
        {
            throw new InvalidOperationException("The storage is full.");
        }
    }

    /// <summary>The store as <see cref="PurchaseSession"/> sees it: a list of what was asked of it.</summary>
    internal sealed class FakeStoreFront : IStoreCommands
    {
        public List<string> Calls { get; } = new List<string>();

        public bool IsConnected { get; set; } = true;

        /// <summary>What BeginPurchase answers.</summary>
        public bool CanSell { get; set; } = true;

        public bool ThrowOnConfirm { get; set; }

        public void RequestConnection()
        {
            Calls.Add("connect");
        }

        public void FetchProducts()
        {
            Calls.Add("fetch-products");
        }

        public void FetchPurchases()
        {
            Calls.Add("fetch-purchases");
        }

        public void RestoreTransactions()
        {
            Calls.Add("restore-transactions");
        }

        public bool BeginPurchase(string productId)
        {
            Calls.Add("buy:" + productId);
            return CanSell;
        }

        public void Confirm(IStoreOrder order)
        {
            Calls.Add("confirm:" + order.TransactionId);
            if (ThrowOnConfirm)
            {
                throw new InvalidOperationException("The store is offline.");
            }
        }

        public int Count(string call)
        {
            int count = 0;
            foreach (string entry in Calls)
            {
                if (entry == call)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Builds a <see cref="PurchaseSession"/> on fakes.</summary>
    internal sealed class SessionHarness
    {
        private readonly List<string> _journal = new List<string>();

        public SessionHarness(
            bool fetchOnConnect = true,
            bool processOwnedOnEveryFetch = true,
            bool blockPurchases = false,
            IStringStore storage = null)
        {
            Storage = storage ?? new InMemoryStringStore();
            Gate = new ScriptedGate(_journal);
            Granter = new RecordingGranter(_journal);
            Ui = new RecordingUi(_journal);
            Telemetry = new RecordingTelemetry(_journal);
            Session = new PurchaseSession(
                TestCatalog.Create(),
                new TransactionLedger(Storage, Harness.LedgerKey),
                new OwnedSetBaseline(Storage, Harness.OwnedKey),
                Gate,
                new VerdictPolicy(UnavailablePolicy.FailOpen),
                Granter,
                Store,
                Ui,
                Telemetry,
                new PurchaseFlowOptions(),
                new PurchaseSessionOptions
                {
                    FetchPurchasesOnConnect = fetchOnConnect,
                    ProcessOwnedOnEveryFetch = processOwnedOnEveryFetch,
                    BlockPurchases = blockPurchases,
                    BlockDetails = "installer: com.example.filemanager"
                });
            Session.OwnedPurchasesSynced += report => Synced.Add(report);
            Session.RestoreFailed += message => RestoreFailures.Add(message);
        }

        public IStringStore Storage { get; }

        public FakeStoreFront Store { get; } = new FakeStoreFront();

        public ScriptedGate Gate { get; }

        public RecordingGranter Granter { get; }

        public RecordingUi Ui { get; }

        public RecordingTelemetry Telemetry { get; }

        public PurchaseSession Session { get; }

        public List<OwnedSyncReport> Synced { get; } = new List<OwnedSyncReport>();

        public List<string> RestoreFailures { get; } = new List<string>();

        /// <summary>The store connects, returns the products and is asked for the purchases.</summary>
        public void ConnectAndFetchProducts()
        {
            Store.IsConnected = true;
            Session.StoreConnected();
            Session.ProductsFetched();
        }

        public static FakeOrder Pending(string transactionId, string productId)
        {
            return new FakeOrder { TransactionId = transactionId, ProductId = productId, StoreProductId = productId };
        }
    }

    /// <summary>
    /// Builds a <see cref="PurchaseFlow"/> on fakes. <see cref="Relaunch"/> rebuilds the flow and
    /// its ledger over the same storage, which is what an app restart does.
    /// </summary>
    internal sealed class Harness
    {
        public const string LedgerKey = "test.ledger";
        public const string OwnedKey = "test.owned";

        private readonly UnavailablePolicy _policy;
        private readonly bool _rejectTestStoreOrders;

        public Harness(
            UnavailablePolicy policy = UnavailablePolicy.FailOpen,
            bool rejectTestStoreOrders = true,
            IStringStore storage = null)
        {
            _policy = policy;
            _rejectTestStoreOrders = rejectTestStoreOrders;
            Storage = new JournalingStringStore(storage ?? new InMemoryStringStore(), Journal);
            Store = new FakeStore(Journal);
            Gate = new ScriptedGate(Journal);
            Granter = new RecordingGranter(Journal);
            Ui = new RecordingUi(Journal);
            Telemetry = new RecordingTelemetry(Journal);
            Relaunch();
        }

        /// <summary>Every side effect in the order it happened.</summary>
        public List<string> Journal { get; } = new List<string>();

        public IStringStore Storage { get; }

        public FakeStore Store { get; }

        public ScriptedGate Gate { get; }

        public RecordingGranter Granter { get; }

        public RecordingUi Ui { get; }

        public RecordingTelemetry Telemetry { get; }

        public PurchaseFlow Flow { get; private set; }

        public void Relaunch()
        {
            Flow = new PurchaseFlow(
                TestCatalog.Create(),
                new TransactionLedger(Storage, LedgerKey),
                new OwnedSetBaseline(Storage, OwnedKey),
                Gate,
                new VerdictPolicy(_policy),
                Granter,
                Store,
                Ui,
                Telemetry,
                new PurchaseFlowOptions { RejectTestStoreOrders = _rejectTestStoreOrders });
        }

        public static FakeOrder Owned(string productId, string transactionId = "owned-tx")
        {
            return new FakeOrder { TransactionId = transactionId, ProductId = productId, StoreProductId = productId };
        }
    }
}
