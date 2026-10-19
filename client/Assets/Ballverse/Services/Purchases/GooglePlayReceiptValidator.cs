using System;

namespace Ballverse.Services
{
    /// <summary>
    /// Android on-device check of the Google Play purchase signature (spec §5.5) with Unity IAP's
    /// <c>CrossPlatformValidator</c> and the app's base64 RSA public key from Play Console (Monetize → Licensing). The key is
    /// public, not a secret; it is set in <see cref="ServicesConfig"/>. Without a key (or outside an Android device build)
    /// purchases are accepted and a warning is logged once — the owner must set the key before the store release.
    /// </summary>
    public sealed class GooglePlayReceiptValidator : IReceiptValidator
    {
        const string Tag = "Receipts";

        readonly IServiceLog _log;
#if BALLVERSE_UNITY_IAP && UNITY_ANDROID && !UNITY_EDITOR
        readonly UnityEngine.Purchasing.Security.CrossPlatformValidator _validator;
#endif
        bool _warned;

        public GooglePlayReceiptValidator(string publicKeyBase64, string applicationId, IServiceLog log)
        {
            _log = log ?? NullServiceLog.Instance;
            if (string.IsNullOrEmpty(publicKeyBase64)) return;
            byte[] key;
            try
            {
                key = Convert.FromBase64String(publicKeyBase64.Trim());
            }
            catch (FormatException)
            {
                _log.Error(Tag, "Google Play public key is not valid base64; purchases are not verified.");
                return;
            }
#if BALLVERSE_UNITY_IAP && UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _validator = new UnityEngine.Purchasing.Security.CrossPlatformValidator(key, applicationId);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Google Play public key rejected; purchases are not verified.", e);
            }
#else
            _ = key;
            _ = applicationId;
#endif
        }

        /// <summary>True when signatures are actually checked.</summary>
        public bool IsActive
        {
            get
            {
#if BALLVERSE_UNITY_IAP && UNITY_ANDROID && !UNITY_EDITOR
                return _validator != null;
#else
                return false;
#endif
            }
        }

        public bool Validate(StorePurchase purchase, out string errorCode)
        {
            errorCode = null;
#if BALLVERSE_UNITY_IAP && UNITY_ANDROID && !UNITY_EDITOR
            if (_validator != null && !string.IsNullOrEmpty(purchase.Receipt))
            {
                try
                {
                    _validator.Validate(purchase.Receipt);
                    return true;
                }
                catch (UnityEngine.Purchasing.Security.IAPSecurityException e)
                {
                    _log.Warning(Tag, "Rejected purchase of " + purchase.ProductId + ": " + e.GetType().Name);
                    errorCode = "validation_failed";
                    return false;
                }
            }
#endif
            if (!_warned)
            {
                _warned = true;
                _log.Warning(Tag, IsActive
                    ? "Purchase without a receipt accepted unverified."
                    : "Google Play signature check inactive (no public key or not an Android device build); purchases accepted unverified.");
            }
            return true;
        }
    }
}
