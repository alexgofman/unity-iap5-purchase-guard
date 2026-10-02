using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Minimal persistent key/value storage. It keeps the core logic free of UnityEngine, so the
    /// ledger and the owned-set baseline can be tested without the engine.
    /// </summary>
    public interface IStringStore
    {
        /// <summary>Returns the stored value, or null or an empty string when nothing is stored.</summary>
        string Read(string key);

        /// <summary>Stores the value. Implementations should make it durable before returning.</summary>
        void Write(string key, string value);
    }

    /// <summary>Non-persistent store for tests and for tools that run outside the engine.</summary>
    public sealed class InMemoryStringStore : IStringStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Read(string key)
        {
            return _values.TryGetValue(key, out string value) ? value : string.Empty;
        }

        public void Write(string key, string value)
        {
            _values[key] = value ?? string.Empty;
        }
    }
}
