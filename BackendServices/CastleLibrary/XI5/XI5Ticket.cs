using Microsoft.VisualBasic;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;
using System;
using System.Diagnostics;

#if NET6_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

#endif
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using XI5.Reader;
using XI5.Types;
using XI5.Types.Parsers;
using XI5.Verification;

namespace XI5
{
    // https://www.psdevwiki.com/ps3/X-I-5-Ticket
    // https://github.com/RipleyTom/rpcn/blob/master/src/server/client/ticket.rs
    // https://github.com/LittleBigRefresh/NPTicket/tree/main

    public class XI5Ticket
    {
        // constructor
        public XI5Ticket() { }

        // fields
        public TicketVersion Version { get; set; }
        public string SerialId { get; set; }
        public uint IssuerId { get; set; }

        public DateTimeOffset IssuedDate { get; set; }
        public DateTimeOffset ExpiryDate { get; set; }

        public ulong UserId { get; set; }
        public string Username { get; set; }

        public string Country { get; set; }
        public string Domain { get; set; }
        
        public string ServiceId { get; set; }
        public string TitleId { get; set; }

        public uint Status { get; set; }
        public ushort TicketLength { get; set; }
        public TicketDataSection BodySection { get; set; }

        public bool SignedByOfficialRPCN { get { return true; } }
        public string SignatureIdentifier { get; set; }
        public byte[] SignatureData { get; set; }
        public bool Valid { get; protected set; }

        // TODO: Use GeneratedRegex, this is not in netstandard yet
        internal static readonly Regex ServiceIdRegex = new Regex("(?<=-)[A-Z0-9]{9}(?=_)", RegexOptions.Compiled);

        [Pure]
        public static XI5Ticket ReadFromBytes(byte[] ticketData)
        {
            using (var ms = new MemoryStream(ticketData))
            {
                return ReadFromStream(ms);
            }
        }

        public static XI5Ticket ReadFromStream(Stream ticketStream)
        {
            byte[] ticketData;
            if (ticketStream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> buffer))
                ticketData = buffer.Array.Take((int)ms.Length).ToArray();

            else
            {
                using (var tempMs = new MemoryStream())
                {
                    ticketStream.CopyTo(tempMs);
                    ticketData = tempMs.ToArray();
                }

                // reset stream position
                ticketStream.Position = 0;
            }

            // ticket version (2 bytes), header (4 bytes), ticket length (2 bytes) = 8 bytes
            const int headerLength = sizeof(byte) + sizeof(byte) + sizeof(uint) + sizeof(ushort);
            Debug.Assert(headerLength == 8, "Header length mismatch.");

            XI5Ticket ticket = new XI5Ticket();

            using (var reader = new TicketReader(ticketStream))
            {
                ticket.Version = reader.ReadTicketVersion();
                ticket.TicketLength = reader.ReadTicketHeader();

                long actualLength = ticketStream.Length - headerLength;
                if (ticket.TicketLength != actualLength)
                    throw new FormatException($"Expected ticket length to be {ticket.TicketLength} bytes, but was {actualLength} bytes.");

                ticket.BodySection = reader.ReadTicketSectionHeader();
                if (ticket.BodySection.Type != TicketDataSectionType.Body)
                    throw new FormatException($"Expected first section to be {nameof(TicketDataSectionType.Body)}, but was {ticket.BodySection.Type} ({(int)ticket.BodySection.Type}).");

                // ticket 2.1
                if (ticket.Version.Major == 2 && ticket.Version.Minor == 1)
                    TicketParser21.ParseTicket(ticket, reader);

                // ticket 3.0
                else if (ticket.Version.Major == 3 && ticket.Version.Minor == 0)
                    TicketParser30.ParseTicket(ticket, reader);

                // unhandled ticket version
                else
                    throw new FormatException($"Unknown/unhandled ticket version {ticket.Version.Major}.{ticket.Version.Minor}.");

                var footer = reader.ReadTicketSectionHeader();
                if (footer.Type != TicketDataSectionType.Footer)
                    throw new FormatException($"Expected last section to be {nameof(TicketDataSectionType.Footer)}, but was {footer.Type} ({(int)footer.Type}).");

                ticket.SignatureIdentifier = reader.ReadTicketStringData(TicketDataType.Binary);
                ticket.SignatureData = reader.ReadTicketBinaryData();
            }

            ITicketSigningKey signingKey = SigningKeyResolver.GetSigningKey(ticket.SignatureIdentifier, ticket.TitleId);
            TicketVerifier ticketVerifier = new TicketVerifier(ticketData, ticket, signingKey);
            ticket.Valid = ticketVerifier.IsTicketValid();

            return ticket;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Version: {Version}");
            sb.AppendLine($"SerialId: {SerialId}");
            sb.AppendLine($"IssuerId: {IssuerId}");
            sb.AppendLine($"IssuedDate: {IssuedDate}");
            sb.AppendLine($"ExpiryDate: {ExpiryDate}");
            sb.AppendLine($"UserId: {UserId}");
            sb.AppendLine($"Username: {Username}");
            sb.AppendLine($"Country: {Country}");
            sb.AppendLine($"Domain: {Domain}");
            sb.AppendLine($"ServiceId: {ServiceId}");
            sb.AppendLine($"TitleId: {TitleId}");
            sb.AppendLine($"Status: {Status}");
            sb.AppendLine($"TicketLength: {TicketLength}");
            sb.AppendLine($"SignatureIdentifier: {SignatureIdentifier}");
            sb.AppendLine($"SignatureData: {(SignatureData != null ? BitConverter.ToString(SignatureData) : "null")}");
            sb.AppendLine($"Valid: {Valid}");

            return sb.ToString();
        }
    }
}