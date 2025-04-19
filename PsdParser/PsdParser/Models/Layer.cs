namespace PsdReaderApp.Models
{
    public class Layer
    {
        public string Name { get; set; }
        public int Top { get; set; }
        public int Left { get; set; }
        public int Bottom { get; set; }
        public int Right { get; set; }

        public ushort ChannelCount { get; set; }
        public List<ChannelInfo> Channels { get; set; } = new();

        public string BlendModeKey { get; set; }
        public byte Opacity { get; set; }
        public byte Clipping { get; set; }
        public byte Flags { get; set; }

        public override string ToString()
        {
            return $"Layer: {Name} | Rect: ({Top},{Left},{Bottom},{Right}) | Blend: {BlendModeKey} | Opacity: {Opacity}";
        }
    }
}