using NUnit.Framework;

namespace Iap5PurchaseGuard.Tests
{
    [TestFixture]
    public class InstallSourcePolicyTests
    {
        private static readonly string[] GooglePlay = { InstallSourcePolicy.GooglePlayInstaller };

        [Test]
        public void GooglePlayInstaller_IsExpected()
        {
            Assert.That(InstallSourcePolicy.IsExpected("com.android.vending", GooglePlay), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("com.example.filemanager")]
        [TestCase("COM.ANDROID.VENDING")]
        [TestCase("com.android.vending.clone")]
        public void AnythingElse_IsNotExpected(string installerName)
        {
            Assert.That(InstallSourcePolicy.IsExpected(installerName, GooglePlay), Is.False);
        }

        [Test]
        public void AdditionalStores_CanBeAllowed()
        {
            string[] stores = { InstallSourcePolicy.GooglePlayInstaller, "com.example.otherstore" };

            Assert.That(InstallSourcePolicy.IsExpected("com.example.otherstore", stores), Is.True);
        }

        [Test]
        public void NoExpectedStores_MeansNothingIsExpected()
        {
            Assert.That(InstallSourcePolicy.IsExpected("com.android.vending", null), Is.False);
            Assert.That(InstallSourcePolicy.IsExpected("com.android.vending", new string[0]), Is.False);
        }
    }
}
