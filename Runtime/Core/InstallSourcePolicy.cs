using System;
using System.Collections.Generic;

namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Compares the installer Android reports for this copy of the app with the stores the game is
    /// actually published on.
    ///
    /// This is a signal, not a proof. The installer name can be set by whoever installs the
    /// package, so a repack can be made to report any value, and a legitimate copy that was put
    /// back by a device-migration tool can report a different one. Use it to label a cohort in
    /// analytics and to point players to the official listing. Do not take away what a player
    /// already owns because of it.
    /// </summary>
    public static class InstallSourcePolicy
    {
        /// <summary>The installer package name Android reports for apps installed by Google Play.</summary>
        public const string GooglePlayInstaller = "com.android.vending";

        /// <summary>
        /// True when <paramref name="installerName"/> is one of the expected installers. A missing
        /// installer name (a sideloaded package) is never expected.
        /// </summary>
        public static bool IsExpected(string installerName, IEnumerable<string> expectedInstallers)
        {
            if (string.IsNullOrEmpty(installerName) || expectedInstallers == null)
            {
                return false;
            }

            foreach (string expected in expectedInstallers)
            {
                if (string.Equals(installerName, expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
