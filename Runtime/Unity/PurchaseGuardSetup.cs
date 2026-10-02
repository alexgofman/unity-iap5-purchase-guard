using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>When the orders the store reports as owned are processed.</summary>
    public enum OwnedSync
    {
        /// <summary><see cref="OnConnectAndRestore"/> on Android, <see cref="RestoreOnly"/> elsewhere.</summary>
        PlatformDefault = 0,

        /// <summary>
        /// Owned purchases are fetched and processed after each store connection and whenever the
        /// player asks for a restore. This is also what runs the refund diff without a player
        /// action.
        /// </summary>
        OnConnectAndRestore = 1,

        /// <summary>
        /// Owned purchases are only processed when the player asks for a restore. On Android
        /// purchases are still fetched after each connection, because that is how Google Play
        /// hands back unconfirmed orders; the owned ones in that fetch are ignored.
        /// </summary>
        RestoreOnly = 2
    }

    /// <summary>
    /// Everything <see cref="PurchasePipeline"/> needs. <see cref="Catalog"/> and
    /// <see cref="Granter"/> are required; the rest has defaults.
    /// </summary>
    public sealed class PurchaseGuardSetup
    {
        public const string DefaultStorageKeyPrefix = "purchase_guard.";

        /// <summary>Required. The products the game sells.</summary>
        public PurchaseCatalog Catalog { get; set; }

        /// <summary>Required. Applies, checks and removes entitlements in the game.</summary>
        public IEntitlementGranter Granter { get; set; }

        /// <summary>Player-facing feedback. Nothing is shown when this is null.</summary>
        public IPurchaseUi Ui { get; set; }

        /// <summary>Event sink. Defaults to <see cref="DebugLogTelemetry"/>.</summary>
        public IIapTelemetry Telemetry { get; set; }

        /// <summary>
        /// Google Play licensing public key (Play Console, base64). Set it from a constant in
        /// compiled code; see <see cref="PlayReceiptValidator"/> for why it should not be loaded
        /// from an asset or a remote setting. Without it, validation on Android is unavailable.
        /// </summary>
        public string PlayLicensingPublicKeyBase64 { get; set; }

        /// <summary>What to do while validation is unavailable. See <see cref="UnavailablePolicy"/>.</summary>
        public UnavailablePolicy WhenValidationUnavailable { get; set; } = UnavailablePolicy.FailOpen;

        /// <summary>Replaces the platform's trust gate, for example with a server-side check.</summary>
        public IReceiptGate ReceiptGate { get; set; }

        /// <summary>Where the ledger and the owned-set baseline are kept. Defaults to PlayerPrefs.</summary>
        public IStringStore Storage { get; set; }

        public string StorageKeyPrefix { get; set; } = DefaultStorageKeyPrefix;

        public int LedgerCapacity { get; set; } = TransactionLedger.DefaultCapacity;

        /// <summary>Delays between automatic store reconnect attempts.</summary>
        public BackoffSchedule Reconnect { get; set; } = new BackoffSchedule();

        /// <summary>
        /// Installer package names that count as an official store on Android. Google Play when
        /// left null.
        /// </summary>
        public IReadOnlyList<string> ExpectedInstallers { get; set; }

        /// <summary>
        /// Refuse to start a purchase when the app was not installed by an expected store. Off by
        /// default. Restores are never blocked, so a legitimately migrated install keeps what it
        /// owns. See <see cref="InstallSourcePolicy"/> before turning this on.
        /// </summary>
        public bool BlockPurchasesFromUnexpectedInstaller { get; set; }

        public OwnedSync OwnedSync { get; set; } = OwnedSync.PlatformDefault;
    }
}
