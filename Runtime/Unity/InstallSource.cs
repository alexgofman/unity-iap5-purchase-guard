using System;
using System.Collections.Generic;
using UnityEngine;

namespace Iap5PurchaseGuard
{
    /// <summary>What is known about how this copy of the app was installed.</summary>
    public readonly struct InstallSourceReport
    {
        public InstallSourceReport(string installerName, bool isExpected, bool isEnforced)
        {
            InstallerName = installerName ?? string.Empty;
            IsExpected = isExpected;
            IsEnforced = isEnforced;
        }

        /// <summary>The installer package name the platform reports. Empty when there is none.</summary>
        public string InstallerName { get; }

        /// <summary>False only when the check applies and the installer is not an expected store.</summary>
        public bool IsExpected { get; }

        /// <summary>True on Android release builds, the only place where the check applies.</summary>
        public bool IsEnforced { get; }
    }

    /// <summary>
    /// Install-source detection from <c>Application.installerName</c>. See
    /// <see cref="InstallSourcePolicy"/> for what the result is and is not good for.
    /// </summary>
    public static class InstallSource
    {
        private const string PlayListingPage = "https://play.google.com/store/apps/details";

        private static readonly string[] GooglePlayOnly = { InstallSourcePolicy.GooglePlayInstaller };

        /// <summary>
        /// Checks the installer against the expected stores (Google Play when none are given).
        /// The check only applies to Android release builds. Editor play mode and development
        /// builds installed over USB have no store installer and are the developer's own copies,
        /// and the installer name carries no such meaning on other platforms.
        /// </summary>
        public static InstallSourceReport Detect(IEnumerable<string> expectedInstallers = null)
        {
            string installerName = Application.installerName ?? string.Empty;
            bool enforced = BuildFlavor.IsReleaseBuild && Application.platform == RuntimePlatform.Android;
            bool expected = !enforced || InstallSourcePolicy.IsExpected(installerName, expectedInstallers ?? GooglePlayOnly);
            return new InstallSourceReport(installerName, expected, enforced);
        }

        /// <summary>
        /// The Google Play listing of the running app, built from its application identifier. An
        /// https link rather than a market link, so it still opens somewhere useful on a device
        /// without the Play Store app; Android hands it to the Play Store app when there is one.
        /// </summary>
        public static string PlayStoreListingUrl()
        {
            return PlayListingPage + "?id=" + Uri.EscapeDataString(Application.identifier);
        }
    }
}
