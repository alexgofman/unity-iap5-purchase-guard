namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Whether this is a release build.
    ///
    /// Two guards are relaxed in the Editor and in development builds, so the SDK test store and
    /// builds installed over USB keep working: the refusal of test-store receipts and the
    /// install-source check. The decision is made at compile time on purpose. It cannot be changed
    /// by editing a data file in a repacked build; changing it takes a patch to compiled code.
    /// </summary>
    internal static class BuildFlavor
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static readonly bool IsReleaseBuild = false;
#else
        public static readonly bool IsReleaseBuild = true;
#endif
    }
}
