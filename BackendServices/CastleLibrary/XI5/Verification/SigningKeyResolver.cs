using CustomLogger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XI5.Verification.Keys;
using XI5.Verification.Keys.Games;

namespace XI5.Verification
{
    public static class SigningKeyResolver
    {
        // Map Title IDs to PSN signing keys
        private static readonly Dictionary<string, PsnSigningKey> PsnKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            { "NPUA80093", new WarhawkSigningKey() }, // US public beta
            { "NPUA80077", new WarhawkSigningKey() }, // US digital
            { "NPEA00017", new WarhawkSigningKey() }, // EU digital
            { "BCUS98117", new WarhawkSigningKey() }, // US disc
            { "BCES00008", new WarhawkSigningKey() }, // EU disc
            { "BCAS20015", new WarhawkSigningKey() }, // Asia disc
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
            LoggerAccessor.LogInfo($"No signing key found for TitleID '{titleId}'. Using default key.");
            return new DefaultSigningKey();
        }
    }
}
