using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class AppleSystemTests
    {
        [TestCase("iOS 14.8.1", true)]
        [TestCase("iPhone OS 13.7", true)]
        [TestCase("iPadOS 14.0", true)]
        [TestCase("tvOS 13.4", true)]
        [TestCase("iOS 15.0", false)]
        [TestCase("iPadOS 17.4.1", false)]
        [TestCase("tvOS 18.0", false)]
        [TestCase("iOS", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OlderThanStoreKit2_IsReadFromTheMajorVersion(string operatingSystem, bool expected)
        {
            Assert.That(AppleSystem.IsOlderThanStoreKit2(operatingSystem), Is.EqualTo(expected));
        }
    }
}
