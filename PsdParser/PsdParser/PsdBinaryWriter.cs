using System;
using System.IO;
using System.Text;

namespace PsdReaderApp
{
    public class PsdBinaryWriter : BinaryWriter
    {
        public PsdBinaryWriter(Stream output) : base(output) { }

        public void WriteAscii(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            Write(bytes);
        }

        public void WritePascalString(string value, int padTo)
        {
            byte[] stringBytes = Encoding.ASCII.GetBytes(value);
            byte length = (byte)Math.Min(255, stringBytes.Length);
            Write(length);
            Write(stringBytes, 0, length);

            int totalLength = 1 + length;
            int padding = (padTo - (totalLength % padTo)) % padTo;
            for (int i = 0; i < padding; i++)
                Write((byte)0);
        }

        public void WriteRect((int Top, int Left, int Bottom, int Right) rect)
        {
            Write(rect.Top);
            Write(rect.Left);
            Write(rect.Bottom);
            Write(rect.Right);
        }
    }
}