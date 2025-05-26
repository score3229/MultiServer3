using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XI5.Verification
{
    /// <summary>
    /// Defines the parameters for a key used for verification.
    /// </summary>
    public interface ITicketSigningKey
    {
        string HashAlgorithm { get; }
        string CurveTable { get; }
        TicketSignatureMessageType MessageType { get; }
        string PublicKeyX { get; }
        string PublicKeyY { get; }
    }
}
