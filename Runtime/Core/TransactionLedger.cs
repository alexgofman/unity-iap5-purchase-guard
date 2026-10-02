using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Persisted set of transaction ids whose entitlement has already been granted on this device.
    ///
    /// A store delivers an order again until the app confirms it: on the next launch, after a
    /// confirmation that did not reach the store, after a crash between grant and confirm. If the
    /// "already granted" set lives only in memory, each of those redeliveries grants again. A
    /// store's transaction id for a purchase stays the same across sessions and redeliveries (a
    /// purchase token on Google Play, a StoreKit 2 transaction id on the App Store), so
    /// remembering the ids on disk makes redelivery idempotent.
    ///
    /// The ledger is bounded and forgets the oldest entry first. Only unconfirmed orders are
    /// redelivered, and an order is normally unconfirmed for a short time, so an entry that has
    /// been pushed out by <see cref="Capacity"/> newer ones is no longer needed.
    ///
    /// Storage failures never make the ledger forget. An id that was added is known for the rest
    /// of the session even if it could not be written, and a failed read is retried instead of
    /// being taken for an empty ledger, so it can never be followed by a write that would replace
    /// the stored entries.
    /// </summary>
    /// <remarks>Not thread-safe. Use it from the thread that receives the store callbacks.</remarks>
    public sealed class TransactionLedger
    {
        public const int DefaultCapacity = 256;

        private const char Separator = '\n';

        private readonly IStringStore _store;
        private readonly string _storageKey;
        private readonly Queue<string> _oldestFirst = new Queue<string>();
        private readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _addedWhileUnreadable = new List<string>();
        private bool _loaded;
        private bool _unsaved;

        public TransactionLedger(IStringStore store, string storageKey, int capacity = DefaultCapacity)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (string.IsNullOrEmpty(storageKey))
            {
                throw new ArgumentException("A storage key is required.", nameof(storageKey));
            }

            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
            }

            _store = store;
            _storageKey = storageKey;
            Capacity = capacity;
        }

        /// <summary>Maximum number of transaction ids kept.</summary>
        public int Capacity { get; }

        public int Count
        {
            get
            {
                Load();
                return _known.Count;
            }
        }

        public bool Contains(string transactionId)
        {
            string id = Canonical(transactionId);
            if (id.Length == 0)
            {
                return false;
            }

            if (_addedWhileUnreadable.Contains(id))
            {
                return true;
            }

            Load();
            return _known.Contains(id);
        }

        /// <summary>
        /// Records a transaction id and persists the ledger. Returns false for an empty id and for
        /// one that is already recorded. A storage failure propagates to the caller; the id is
        /// still known for the rest of this session.
        /// </summary>
        public bool Add(string transactionId)
        {
            string id = Canonical(transactionId);
            if (id.Length == 0)
            {
                return false;
            }

            try
            {
                Load();
            }
            catch (Exception)
            {
                if (!_addedWhileUnreadable.Contains(id))
                {
                    _addedWhileUnreadable.Add(id);
                }

                throw;
            }

            bool added = Remember(id);
            if (added)
            {
                _unsaved = true;
            }

            if (!_unsaved)
            {
                return false;
            }

            // If this write throws, the ids stay in memory and the next Add writes them again.
            DropOldestOverCapacity();
            _store.Write(_storageKey, string.Join(Separator.ToString(), _oldestFirst));
            _unsaved = false;
            return added;
        }

        private void DropOldestOverCapacity()
        {
            while (_oldestFirst.Count > Capacity)
            {
                _known.Remove(_oldestFirst.Dequeue());
            }
        }

        private void Load()
        {
            if (_loaded)
            {
                return;
            }

            // If this read throws, the ledger stays unloaded: the next call tries again, and
            // nothing is written in between.
            string stored = _store.Read(_storageKey);

            if (!string.IsNullOrEmpty(stored))
            {
                foreach (string entry in stored.Split(Separator))
                {
                    Remember(Canonical(entry));
                }
            }

            // Ids added while the storage could not be read join the ledger now and are written
            // by the next Add.
            foreach (string id in _addedWhileUnreadable)
            {
                if (Remember(id))
                {
                    _unsaved = true;
                }
            }

            _addedWhileUnreadable.Clear();

            // The capacity may have been lowered since the ledger was written.
            DropOldestOverCapacity();
            _loaded = true;
        }

        private bool Remember(string id)
        {
            if (id.Length == 0 || !_known.Add(id))
            {
                return false;
            }

            _oldestFirst.Enqueue(id);
            return true;
        }

        // Entries are stored one per line, so a line break inside an id would split it into two
        // entries on the next load. Real store ids never contain one; this keeps a malformed id
        // from breaking the lookup.
        private static string Canonical(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId))
            {
                return string.Empty;
            }

            return transactionId.Trim().Replace('\n', '_').Replace('\r', '_');
        }
    }
}
