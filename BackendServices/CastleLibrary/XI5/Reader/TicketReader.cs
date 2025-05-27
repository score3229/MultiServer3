using BitConverterExtension;
using System;
using System.IO;
using System.Text;
using XI5.Types;

namespace XI5.Reader
{
    public class TicketReader : BinaryReader
    {
        // setup endian converter
        private static readonly BigEndianBitConverter bigEndian = new();

        public TicketReader(Stream input) : base(input) { }

        #region Big Endian Conversion

        public override short ReadInt16()
        {
            byte[] bytes = ReadBytes(2);
            return bigEndian.ToInt16(bytes, 0);
        }

        public override int ReadInt32()
        {
            byte[] bytes = ReadBytes(4);
            return bigEndian.ToInt32(bytes, 0);
        }

        public override long ReadInt64()
        {
            byte[] bytes = ReadBytes(8);
            return bigEndian.ToInt64(bytes, 0);
        }

        public override ushort ReadUInt16()
        {
            byte[] bytes = ReadBytes(2);
            return bigEndian.ToUInt16(bytes, 0);
        }

        public override uint ReadUInt32()
        {
            byte[] bytes = ReadBytes(4);
            return bigEndian.ToUInt32(bytes, 0);
        }

        public override ulong ReadUInt64()
        {
            byte[] bytes = ReadBytes(8);
            return bigEndian.ToUInt64(bytes, 0);
        }

        #endregion

        internal TicketVersion ReadTicketVersion() => new TicketVersion((byte)(ReadByte() >> 4), ReadByte());

        internal ushort ReadTicketHeader()
        {
            ReadBytes(4);           // header
            return ReadUInt16();    // ticket length
        }

        internal TicketDataSection ReadTicketSectionHeader()
        {
            long position = this.BaseStream.Position;

            byte sectionHeader = this.ReadByte();
            if (sectionHeader != 0x30)
                throw new FormatException($"[XI5Ticket] - Expected 0x30 for section header, was {sectionHeader}. Offset is {this.BaseStream.Position}");

            TicketDataSectionType type = (TicketDataSectionType)this.ReadByte();
            ushort length = this.ReadUInt16();

            return new TicketDataSection(type, length, position);
        }

        private TicketData ReadTicketData(TicketDataType expectedType)
        {
            TicketData data = new TicketData((TicketDataType)ReadUInt16(), ReadUInt16());
            if (data.Type != expectedType && expectedType != TicketDataType.Empty)
                throw new FormatException($"[XI5Ticket] - Expected data type to be {expectedType}, was really {data.Type} ({(int)data.Type})");

            return data;
        }

        internal byte[] ReadTicketBinaryData(TicketDataType type = TicketDataType.Binary)
            => ReadBytes(ReadTicketData(type).Length);
        internal string ReadTicketStringData(TicketDataType type = TicketDataType.String)
            => Encoding.Default.GetString(ReadTicketBinaryData(type)).TrimEnd('\0');

        internal uint ReadTicketUInt32Data()
        {
            ReadTicketData(TicketDataType.UInt32);
            return ReadUInt32();
        }

        internal ulong ReadTicketUInt64Data()
        {
            ReadTicketData(TicketDataType.UInt64);
            return ReadUInt64();
        }

        internal DateTimeOffset ReadTicketTimestampData()
        {
            ReadTicketData(TicketDataType.Timestamp);
            return DateTimeOffset.FromUnixTimeMilliseconds((long)ReadUInt64());
        }

        internal void SkipTicketEmptyData(int sections = 1)
        {
            for (int i = 0; i < sections; i++) ReadTicketData(TicketDataType.Empty);
        }
    }
}
