using UnityEngine;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// <see cref="IStringStore"/> on PlayerPrefs. Every write is saved immediately: a ledger entry
    /// has to survive the app being killed right after a grant.
    /// </summary>
    /// <remarks>
    /// PlayerPrefs is separate from the game's save data, so the ledger adds nothing to a save
    /// schema. It does not survive a reinstall. An order that was granted but not yet confirmed
    /// when the app was removed is therefore granted again afterwards, which is right when the
    /// save was lost too. If the game restores progress from a cloud save, keep the ledger in
    /// that save by supplying your own <see cref="IStringStore"/>.
    /// </remarks>
    public sealed class PlayerPrefsStore : IStringStore
    {
        public string Read(string key)
        {
            return PlayerPrefs.GetString(key, string.Empty);
        }

        public void Write(string key, string value)
        {
            PlayerPrefs.SetString(key, value ?? string.Empty);
            PlayerPrefs.Save();
        }
    }
}
