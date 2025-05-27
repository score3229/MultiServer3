using System;
using System.Collections.Generic;
using XI5.Verification.Keys;
using XI5.Verification.Keys.Games;

namespace XI5.Verification
{
    public static class SigningKeyResolver
    {
        // Map Title IDs to PSN signing keys
        private static readonly Dictionary<string, PsnSigningKey> PsnKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            // game title ID, signing key, e.x
            // { "NPUA80093", new WarhawkSigningKey() },
        };

        /// <summary>
        /// Returns the appropriate signing key based on the issuer and title ID.
        /// </summary>
        public static ITicketSigningKey GetSigningKey(string issuer, string titleId)
        {
            // rpcn signing key
            if (issuer.Equals("RPCN", StringComparison.OrdinalIgnoreCase))
                return RpcnSigningKey.Instance;

            // psn game signing key
            if (!string.IsNullOrWhiteSpace(titleId) && PsnKeys.TryGetValue(titleId, out var psnKey))
                return psnKey;

            // default signing key
            return new DefaultSigningKey();
        }
    }
}
