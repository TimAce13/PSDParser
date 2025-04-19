using PsdReaderApp.Models;
using System.Text;

namespace PsdReaderApp.Core
{
    public class PsdReader
    {
        private readonly string _filePath;

        public PsdReader(string filePath)
        {
            _filePath = filePath;
        }

        public List<Layer> ReadLayers()
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);

            reader.BaseStream.Seek(26, SeekOrigin.Begin); // Skip header
            SkipBlock(reader); // Color mode
            SkipBlock(reader); // Image resources

            uint layerAndMaskLen = BinaryHelper.ReadUInt32BE(reader);
            long layerSectionEnd = reader.BaseStream.Position + layerAndMaskLen;

            uint layerInfoLen = BinaryHelper.ReadUInt32BE(reader);
            long layerInfoEnd = reader.BaseStream.Position + layerInfoLen;

            short layerCount = BinaryHelper.ReadInt16BE(reader);

            var layers = new List<Layer>();
            for (int i = 0; i < Math.Abs(layerCount); i++)
            {
                var layer = new Layer
                {
                    Top = BinaryHelper.ReadInt32BE(reader),
                    Left = BinaryHelper.ReadInt32BE(reader),
                    Bottom = BinaryHelper.ReadInt32BE(reader),
                    Right = BinaryHelper.ReadInt32BE(reader),
                    ChannelCount = BinaryHelper.ReadUInt16BE(reader)
                };

                for (int c = 0; c < layer.ChannelCount; c++)
                {
                    short id = BinaryHelper.ReadInt16BE(reader);
                    uint length = BinaryHelper.ReadUInt32BE(reader);
                    layer.Channels.Add(new ChannelInfo { ChannelID = id, DataLength = length });
                }

                string blendSig = Encoding.ASCII.GetString(reader.ReadBytes(4)); // 8BIM
                layer.BlendModeKey = Encoding.ASCII.GetString(reader.ReadBytes(4));

                layer.Opacity = reader.ReadByte();
                layer.Clipping = reader.ReadByte();
                layer.Flags = reader.ReadByte();
                reader.ReadByte(); // filler

                uint extraLen = BinaryHelper.ReadUInt32BE(reader);
                long extraEnd = reader.BaseStream.Position + extraLen;

                SkipBlock(reader); // mask data
                SkipBlock(reader); // blending ranges

                byte nameLen = reader.ReadByte();
                byte[] nameBytes = reader.ReadBytes(nameLen);
                layer.Name = Encoding.ASCII.GetString(nameBytes);

                // Skip name padding
                int namePad = ((nameLen + 1 + 3) / 4) * 4;
                reader.ReadBytes(namePad - (nameLen + 1));

                reader.BaseStream.Position = extraEnd;
                layers.Add(layer);
            }

            return layers;
        }

        private void SkipBlock(BinaryReader reader)
        {
            uint len = BinaryHelper.ReadUInt32BE(reader);
            reader.BaseStream.Seek(len, SeekOrigin.Current);
        }
    }
}
