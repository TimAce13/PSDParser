namespace PsdParser.Models
{
    public class PsdHeader
    {
        public string Signature { get; set; }
        public ushort Version { get; set; }
        public ushort ChannelCount { get; set; }
        public uint Height { get; set; }
        public uint Width { get; set; }
        public ushort BitDepth { get; set; }
        public ushort ColorMode { get; set; }

        public override string ToString()
        {
            return $"Signature: {Signature}, Version: {Version}, Size: {Width}x{Height}, Channels: {ChannelCount}, BitDepth: {BitDepth}, ColorMode: {ColorMode}";
        }
    }
}