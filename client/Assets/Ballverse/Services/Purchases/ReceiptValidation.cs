namespace Ballverse.Services
{
    /// <summary>
    /// On-device purchase verification before rights are granted (spec §5.5, no server in 1.0, Р8).
    /// iOS: StoreKit 2 verifies the signed transaction (JWS) inside Unity IAP — unverified transactions never reach the app —
    /// so <see cref="AcceptAllReceiptValidator"/> is used there. Android: <see cref="GooglePlayReceiptValidator"/> checks the
    /// Play signature with the app's public license key.
    /// </summary>
    public interface IReceiptValidator
    {
        bool Validate(StorePurchase purchase, out string errorCode);
    }

    /// <summary>iOS (StoreKit 2 verifies), editor, fake store.</summary>
    public sealed class AcceptAllReceiptValidator : IReceiptValidator
    {
        public static readonly AcceptAllReceiptValidator Instance = new AcceptAllReceiptValidator();

        public bool Validate(StorePurchase purchase, out string errorCode)
        {
            errorCode = null;
            return true;
        }
    }

    /// <summary>Scriptable validator for tests.</summary>
    public sealed class FakeReceiptValidator : IReceiptValidator
    {
        public bool Accept { get; set; } = true;

        public int Calls { get; private set; }

        public bool Validate(StorePurchase purchase, out string errorCode)
        {
            Calls++;
            errorCode = Accept ? null : "validation_failed";
            return Accept;
        }
    }
}
