using PsdReaderApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PsdReaderApp.Core
{
    public class PsdWriter
    {
        private readonly string _sourcePath;
        private readonly string _targetPath;
        private readonly List<Layer> _modifiedLayers;

        // Constants for PSD format
        private const string SIGNATURE = "8BPS";
        private const ushort VERSION = 1;

        public PsdWriter(string sourcePath, string targetPath, List<Layer> modifiedLayers)
        {
            _sourcePath = sourcePath;
            _targetPath = targetPath;
            _modifiedLayers = modifiedLayers;
        }

        public void Write()
        {
            // First read the header to get basic file info
            PsdHeader header;
            using (var fs = new FileStream(_sourcePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                header = ReadHeader(reader);
            }

            // Do the full copy with modifications
            using (var input = new FileStream(_sourcePath, FileMode.Open, FileAccess.Read))
            using (var output = new FileStream(_targetPath, FileMode.Create, FileAccess.Write))
            using (var reader = new BinaryReader(input))
            using (var writer = new BinaryWriter(output))
            {
                // 1. Copy the header
                CopyHeader(reader, writer);

                // 2. Copy color mode data
                CopyColorModeData(reader, writer);

                // 3. Copy image resources
                CopyImageResources(reader, writer);

                // 4. Process layer and mask info section
                ProcessLayerAndMaskInfo(reader, writer, header);

                // 5. Copy image data
                CopyImageData(reader, writer);
            }
        }

        private PsdHeader ReadHeader(BinaryReader reader)
        {
            var header = new PsdHeader
            {
                Signature = Encoding.ASCII.GetString(reader.ReadBytes(4)),
                Version = BinaryHelper.ReadUInt16BE(reader),
                // Skip reserved bytes
                Reserved = reader.ReadBytes(6),
                ChannelCount = BinaryHelper.ReadUInt16BE(reader),
                Height = BinaryHelper.ReadUInt32BE(reader),
                Width = BinaryHelper.ReadUInt32BE(reader),
                BitDepth = BinaryHelper.ReadUInt16BE(reader),
                ColorMode = BinaryHelper.ReadUInt16BE(reader)
            };

            return header;
        }

        private void CopyHeader(BinaryReader reader, BinaryWriter writer)
        {
            // Read header bytes
            byte[] headerBytes = reader.ReadBytes(26);
            
            // Verify signature
            string signature = Encoding.ASCII.GetString(headerBytes, 0, 4);
            if (signature != SIGNATURE)
            {
                throw new Exception($"Invalid PSD signature: {signature}");
            }
            
            // Write header bytes exactly as they were
            writer.Write(headerBytes);
        }

        private void CopyColorModeData(BinaryReader reader, BinaryWriter writer)
        {
            uint colorModeLength = BinaryHelper.ReadUInt32BE(reader);
            BinaryHelper.WriteUInt32BE(writer, colorModeLength);
            
            if (colorModeLength > 0)
            {
                byte[] colorModeData = reader.ReadBytes((int)colorModeLength);
                writer.Write(colorModeData);
            }
        }

        private void CopyImageResources(BinaryReader reader, BinaryWriter writer)
        {
            uint imageResourcesLength = BinaryHelper.ReadUInt32BE(reader);
            BinaryHelper.WriteUInt32BE(writer, imageResourcesLength);
            
            if (imageResourcesLength > 0)
            {
                byte[] imageResourcesData = reader.ReadBytes((int)imageResourcesLength);
                writer.Write(imageResourcesData);
            }
        }

        private void ProcessLayerAndMaskInfo(BinaryReader reader, BinaryWriter writer, PsdHeader header)
        {
            // Layer and mask info section
            long layerAndMaskStart = reader.BaseStream.Position;
            uint layerAndMaskLength = BinaryHelper.ReadUInt32BE(reader);
            long layerAndMaskEnd = layerAndMaskStart + 4 + layerAndMaskLength;

            // We'll build our own layer and mask info section
            using (var layerAndMaskBuffer = new MemoryStream())
            using (var layerAndMaskWriter = new BinaryWriter(layerAndMaskBuffer))
            {
                // Process layer info section
                ProcessLayerInfo(reader, layerAndMaskWriter);

                // Copy global layer mask info
                long currentPos = reader.BaseStream.Position;
                long remainingBytes = layerAndMaskEnd - currentPos;
                
                if (remainingBytes > 0)
                {
                    byte[] globalLayerMaskInfo = reader.ReadBytes((int)remainingBytes);
                    layerAndMaskWriter.Write(globalLayerMaskInfo);
                }

                // Write the completed layer and mask info section
                byte[] layerAndMaskData = layerAndMaskBuffer.ToArray();
                BinaryHelper.WriteUInt32BE(writer, (uint)layerAndMaskData.Length);
                writer.Write(layerAndMaskData);
            }
        }

        private void ProcessLayerInfo(BinaryReader reader, BinaryWriter writer)
        {
            long layerInfoStart = reader.BaseStream.Position;
            uint layerInfoLength = BinaryHelper.ReadUInt32BE(reader);
            long layerInfoEnd = layerInfoStart + 4 + layerInfoLength;

            // Store the current position to calculate actual layer info length
            long layerInfoDataStart = writer.BaseStream.Position;

            // Read layer count
            short layerCount = BinaryHelper.ReadInt16BE(reader);
            bool hasAlpha = layerCount < 0;
            short absLayerCount = (short)Math.Abs(layerCount);
            
            // Write the same layer count (preserve sign)
            BinaryHelper.WriteInt16BE(writer, layerCount);

            // Process each layer
            for (int i = 0; i < absLayerCount; i++)
            {
                // Layer record
                int top = BinaryHelper.ReadInt32BE(reader);
                int left = BinaryHelper.ReadInt32BE(reader);
                int bottom = BinaryHelper.ReadInt32BE(reader);
                int right = BinaryHelper.ReadInt32BE(reader);
                
                // Write modified layer record
                BinaryHelper.WriteInt32BE(writer, _modifiedLayers[i].Top);
                BinaryHelper.WriteInt32BE(writer, _modifiedLayers[i].Left);
                BinaryHelper.WriteInt32BE(writer, _modifiedLayers[i].Bottom);
                BinaryHelper.WriteInt32BE(writer, _modifiedLayers[i].Right);

                // Channels info
                ushort channelCount = BinaryHelper.ReadUInt16BE(reader);
                BinaryHelper.WriteUInt16BE(writer, channelCount);

                // Channel information
                for (int c = 0; c < channelCount; c++)
                {
                    short channelID = BinaryHelper.ReadInt16BE(reader);
                    uint channelLength = BinaryHelper.ReadUInt32BE(reader);
                    
                    BinaryHelper.WriteInt16BE(writer, channelID);
                    BinaryHelper.WriteUInt32BE(writer, channelLength);
                }

                // Layer blend mode
                byte[] blendSig = reader.ReadBytes(4);
                byte[] blendMode = reader.ReadBytes(4);
                writer.Write(blendSig);
                writer.Write(blendMode);

                // Layer opacity, clipping, flags
                byte opacity = reader.ReadByte();
                byte clipping = reader.ReadByte();
                byte flags = reader.ReadByte();
                byte filler = reader.ReadByte();
                
                writer.Write(opacity);
                writer.Write(clipping);
                writer.Write(flags);
                writer.Write(filler);

                // Layer extra data length
                uint extraDataLength = BinaryHelper.ReadUInt32BE(reader);
                long extraDataEnd = reader.BaseStream.Position + extraDataLength;
                
                // We'll write our own extra data section
                using (var extraDataBuffer = new MemoryStream())
                using (var extraDataWriter = new BinaryWriter(extraDataBuffer))
                {
                    // Layer mask data
                    uint maskDataSize = BinaryHelper.ReadUInt32BE(reader);
                    BinaryHelper.WriteUInt32BE(extraDataWriter, maskDataSize);
                    if (maskDataSize > 0)
                    {
                        extraDataWriter.Write(reader.ReadBytes((int)maskDataSize));
                    }

                    // Layer blending ranges
                    uint blendingRangesSize = BinaryHelper.ReadUInt32BE(reader);
                    BinaryHelper.WriteUInt32BE(extraDataWriter, blendingRangesSize);
                    if (blendingRangesSize > 0)
                    {
                        extraDataWriter.Write(reader.ReadBytes((int)blendingRangesSize));
                    }

                    // Layer name (Pascal string with 4-byte padding)
                    byte nameLength = reader.ReadByte();
                    byte[] nameBytes = reader.ReadBytes(nameLength);
                    
                    // Skip padding to reach 4-byte boundary
                    int paddedNameLength = ((nameLength + 1 + 3) / 4) * 4;
                    reader.BaseStream.Position += (paddedNameLength - (nameLength + 1));
                    
                    // Write the modified name
                    string modifiedName = _modifiedLayers[i].Name;
                    byte[] modifiedNameBytes = Encoding.ASCII.GetBytes(modifiedName);
                    byte modifiedNameLength = (byte)Math.Min(255, modifiedNameBytes.Length);
                    
                    extraDataWriter.Write(modifiedNameLength);
                    extraDataWriter.Write(modifiedNameBytes, 0, modifiedNameLength);
                    
                    // Add padding to reach 4-byte boundary
                    int modifiedPaddedNameLength = ((modifiedNameLength + 1 + 3) / 4) * 4;
                    int modifiedPadding = modifiedPaddedNameLength - (modifiedNameLength + 1);
                    for (int p = 0; p < modifiedPadding; p++)
                    {
                        extraDataWriter.Write((byte)0);
                    }

                    // Copy any remaining additional layer data (adjustment layers, type info, etc.)
                    long currentPos = reader.BaseStream.Position;
                    long remainingBytes = extraDataEnd - currentPos;
                    
                    if (remainingBytes > 0)
                    {
                        byte[] additionalData = reader.ReadBytes((int)remainingBytes);
                        extraDataWriter.Write(additionalData);
                    }
                    else
                    {
                        // Ensure we're at the right position
                        reader.BaseStream.Position = extraDataEnd;
                    }

                    // Write the completed extra data
                    byte[] extraData = extraDataBuffer.ToArray();
                    BinaryHelper.WriteUInt32BE(writer, (uint)extraData.Length);
                    writer.Write(extraData);
                }
            }

            // Copy layer channel image data
            long layerChannelDataStart = reader.BaseStream.Position;
            long layerChannelDataLength = layerInfoEnd - layerChannelDataStart;
            if (layerChannelDataLength > 0)
            {
                byte[] channelImageData = reader.ReadBytes((int)layerChannelDataLength);
                writer.Write(channelImageData);
            }

            // Calculate the actual layer info length
            long layerInfoDataEnd = writer.BaseStream.Position;
            long actualLayerInfoLength = layerInfoDataEnd - layerInfoDataStart;
            
            // Write the layer info length at the beginning
            long currentPos_1 = writer.BaseStream.Position;
            writer.BaseStream.Position = layerInfoDataStart - 4;
            BinaryHelper.WriteUInt32BE(writer, (uint)actualLayerInfoLength);
            writer.BaseStream.Position = currentPos_1;
        }

        private void CopyImageData(BinaryReader reader, BinaryWriter writer)
        {
            // Copy the rest of the file (image data)
            long bytesRemaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (bytesRemaining > 0)
            {
                byte[] imageData = reader.ReadBytes((int)bytesRemaining);
                writer.Write(imageData);
            }
        }
    }

    // Add the missing PsdHeader class if it doesn't exist
    public class PsdHeader
    {
        public string Signature { get; set; }
        public ushort Version { get; set; }
        public byte[] Reserved { get; set; }
        public ushort ChannelCount { get; set; }
        public uint Height { get; set; }
        public uint Width { get; set; }
        public ushort BitDepth { get; set; }
        public ushort ColorMode { get; set; }
    }
}