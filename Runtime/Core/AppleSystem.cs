namespace Iap5PurchaseGuard
{
    /// <summary>
    /// Unity IAP 5 uses StoreKit 2 on iOS and tvOS 15 and later and falls back to StoreKit 1 on
    /// older systems. This package is written for StoreKit 2. On StoreKit 1 nothing verifies a
    /// transaction on the device, and restored purchases arrive as new orders with new
    /// transaction ids, which the ledger cannot recognise. The pipeline uses this helper to say so
    /// at start-up instead of failing quietly.
    /// </summary>
    public static class AppleSystem
    {
        public const int FirstStoreKit2MajorVersion = 15;

        /// <summary>
        /// True when an operating system description such as "iOS 14.8.1" or "tvOS 13.4" names a
        /// major version below 15. False when no version number can be read from it.
        /// </summary>
        public static bool IsOlderThanStoreKit2(string operatingSystem)
        {
            if (string.IsNullOrEmpty(operatingSystem))
            {
                return false;
            }

            int start = 0;
            while (start < operatingSystem.Length && !IsDigit(operatingSystem[start]))
            {
                start++;
            }

            int end = start;
            while (end < operatingSystem.Length && IsDigit(operatingSystem[end]) && end - start < 4)
            {
                end++;
            }

            if (end == start)
            {
                return false;
            }

            int major = int.Parse(operatingSystem.Substring(start, end - start));
            return major < FirstStoreKit2MajorVersion;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
    }
}
