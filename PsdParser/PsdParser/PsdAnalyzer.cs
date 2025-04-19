using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace PsdReaderApp.Tools
{
    public class PsdAnalyzer
    {
        private const string PSD_SIGNATURE = "8BPS";
        
        public static void AnalyzePsdFile(string filePath)
        {
            Console.WriteLine($"Analyzing PSD file: {filePath}");
            
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                // Verify PSD signature
                string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (signature != PSD_SIGNATURE)
                {
                    Console.WriteLine("❌ Not a valid PSD file");
                    return;
                }
                
                // Read version
                ushort version = ReadUInt16BE(reader);
                Console.WriteLine($"PSD Version: {version}");
                
                // Skip 6 reserved bytes
                reader.ReadBytes(6);
                
                // Read channel count
                ushort channelCount = ReadUInt16BE(reader);
                uint height = ReadUInt32BE(reader);
                uint width = ReadUInt32BE(reader);
                ushort bitDepth = ReadUInt16BE(reader);
                ushort colorMode = ReadUInt16BE(reader);
                
                Console.WriteLine($"Dimensions: {width}x{height}, Channels: {channelCount}, Bit Depth: {bitDepth}, Color Mode: {colorMode}");
                
                // Color Mode Data section
                uint colorModeDataLength = ReadUInt32BE(reader);
                Console.WriteLine($"Color Mode Data Length: {colorModeDataLength}");
                fs.Position += colorModeDataLength;
                
                // Image Resources section
                uint imageResourcesLength = ReadUInt32BE(reader);
                Console.WriteLine($"Image Resources Length: {imageResourcesLength}");
                fs.Position += imageResourcesLength;
                
                // Layer and Mask Information section
                uint layerAndMaskInfoLength = ReadUInt32BE(reader);
                Console.WriteLine($"Layer and Mask Info Length: {layerAndMaskInfoLength}");
                
                // Layer Info section
                uint layerInfoLength = ReadUInt32BE(reader);
                Console.WriteLine($"Layer Info Length: {layerInfoLength}");
                
                // Layer count
                short layerCount = ReadInt16BE(reader);
                bool hasAlphaChannel = layerCount < 0;
                int absLayerCount = Math.Abs(layerCount);
                
                Console.WriteLine($"Layer Count: {absLayerCount} (Alpha Channel: {hasAlphaChannel})");
                
                // Process each layer
                for (int i = 0; i < absLayerCount; i++)
                {
                    Console.WriteLine($"\n--- Layer {i} ---");
                    
                    // Read rectangle
                    int top = ReadInt32BE(reader);
                    int left = ReadInt32BE(reader);
                    int bottom = ReadInt32BE(reader);
                    int right = ReadInt32BE(reader);
                    
                    Console.WriteLine($"Rectangle: Top={top}, Left={left}, Bottom={bottom}, Right={right}");
                    
                    // Read channel info
                    ushort layerChannelCount = ReadUInt16BE(reader);
                    Console.WriteLine($"Channel Count: {layerChannelCount}");
                    
                    // Read channel data
                    for (int c = 0; c < layerChannelCount; c++)
                    {
                        short channelID = ReadInt16BE(reader);
                        uint channelLength = ReadUInt32BE(reader);
                        Console.WriteLine($"  Channel ID: {channelID}, Length: {channelLength}");
                    }
                    
                    // Read blend mode info
                    string blendSignature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    string blendMode = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    Console.WriteLine($"Blend Mode: {blendSignature}:{blendMode}");
                    
                    // Read opacity, clipping, flags
                    byte opacity = reader.ReadByte();
                    byte clipping = reader.ReadByte();
                    byte flags = reader.ReadByte();
                    byte filler = reader.ReadByte();
                    
                    Console.WriteLine($"Opacity: {opacity}, Clipping: {clipping}, Flags: {flags:X2}");
                    
                    // Read extra data length
                    long extraDataLengthPos = fs.Position;
                    uint extraDataLength = ReadUInt32BE(reader);
                    Console.WriteLine($"Extra Data Length: {extraDataLength} (at position {extraDataLengthPos})");
                    
                    long extraDataStart = fs.Position;
                    long extraDataEnd = extraDataStart + extraDataLength;
                    
                    // Read mask data
                    uint maskDataLength = ReadUInt32BE(reader);
                    Console.WriteLine($"Mask Data Length: {maskDataLength}");
                    fs.Position += maskDataLength;
                    
                    // Read blending ranges
                    uint blendingRangesLength = ReadUInt32BE(reader);
                    Console.WriteLine($"Blending Ranges Length: {blendingRangesLength}");
                    fs.Position += blendingRangesLength;
                    
                    // Read layer name
                    long namePos = fs.Position;
                    byte nameLength = reader.ReadByte();
                    byte[] nameBytes = reader.ReadBytes(nameLength);
                    string layerName = Encoding.ASCII.GetString(nameBytes);
                    
                    Console.WriteLine($"Name Position: {namePos}, Length: {nameLength}");
                    Console.WriteLine($"Layer Name: '{layerName}'");
                    
                    // Calculate name padding
                    int totalNameLength = nameLength + 1; // +1 for length byte
                    int paddedNameLength = ((totalNameLength + 3) / 4) * 4; // Round up to multiple of 4
                    int namePadding = paddedNameLength - totalNameLength;
                    
                    Console.WriteLine($"Name Padding: {namePadding} bytes");
                    
                    // Skip to after the padded name
                    fs.Position = namePos + paddedNameLength;
                    
                    // Check for additional data
                    long afterNamePos = fs.Position;
                    long remainingExtraData = extraDataEnd - afterNamePos;
                    
                    if (remainingExtraData > 0)
                    {
                        Console.WriteLine($"Additional layer data: {remainingExtraData} bytes");
                        
                        // Try to read any additional data blocks (like adjustment layers, etc.)
                        AnalyzeAdditionalLayerData(reader, extraDataEnd);
                    }
                    
                    // Move to the end of this layer's extra data
                    fs.Position = extraDataEnd;
                }
            }
        }
        
        public static void CompareLayerNames(string file1Path, string file2Path)
        {
            Console.WriteLine($"Comparing layer names between:\n1. {file1Path}\n2. {file2Path}\n");
            
            var layers1 = ExtractLayerNames(file1Path);
            var layers2 = ExtractLayerNames(file2Path);
            
            int maxCount = Math.Max(layers1.Count, layers2.Count);
            
            Console.WriteLine($"{"Index",-6} | {"File 1",-30} | {"File 2",-30}");
            Console.WriteLine(new string('-', 70));
            
            for (int i = 0; i < maxCount; i++)
            {
                string name1 = i < layers1.Count ? layers1[i] : "N/A";
                string name2 = i < layers2.Count ? layers2[i] : "N/A";
                
                bool different = name1 != name2;
                string prefix = different ? "* " : "  ";
                
                Console.WriteLine($"{prefix}{i,-5} | {name1,-30} | {name2,-30}");
            }
        }
        
        private static List<string> ExtractLayerNames(string filePath)
        {
            var layerNames = new List<string>();
            
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                // Skip signature, version, and reserved bytes
                fs.Position = 26;
                
                // Skip Color Mode Data section
                uint colorModeDataLength = ReadUInt32BE(reader);
                fs.Position += colorModeDataLength;
                
                // Skip Image Resources section
                uint imageResourcesLength = ReadUInt32BE(reader);
                fs.Position += imageResourcesLength;
                
                // Layer and Mask Information section
                uint layerAndMaskInfoLength = ReadUInt32BE(reader);
                
                // Layer Info section
                uint layerInfoLength = ReadUInt32BE(reader);
                
                // Layer count
                short layerCount = ReadInt16BE(reader);
                int absLayerCount = Math.Abs(layerCount);
                
                // Process each layer
                for (int i = 0; i < absLayerCount; i++)
                {
                    // Skip rectangle
                    fs.Position += 16;
                    
                    // Read channel count and skip channels
                    ushort layerChannelCount = ReadUInt16BE(reader);
                    fs.Position += layerChannelCount * 6;
                    
                    // Skip blend mode
                    fs.Position += 8;
                    
                    // Skip opacity, clipping, flags, filler
                    fs.Position += 4;
                    
                    // Read extra data length
                    uint extraDataLength = ReadUInt32BE(reader);
                    long extraDataEnd = fs.Position + extraDataLength;
                    
                    // Skip mask data
                    uint maskDataLength = ReadUInt32BE(reader);
                    fs.Position += maskDataLength;
                    
                    // Skip blending ranges
                    uint blendingRangesLength = ReadUInt32BE(reader);
                    fs.Position += blendingRangesLength;
                    
                    // Read layer name
                    byte nameLength = reader.ReadByte();
                    byte[] nameBytes = reader.ReadBytes(nameLength);
                    string layerName = Encoding.ASCII.GetString(nameBytes);
                    
                    layerNames.Add(layerName);
                    
                    // Skip to end of this layer's extra data
                    fs.Position = extraDataEnd;
                }
            }
            
            return layerNames;
        }
        
        private static void AnalyzeAdditionalLayerData(BinaryReader reader, long endPosition)
        {
            while (reader.BaseStream.Position < endPosition)
            {
                long blockStart = reader.BaseStream.Position;
                
                // Try to detect if we have an additional layer info block
                if (reader.BaseStream.Position + 4 <= endPosition)
                {
                    string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    
                    if (signature == "8BIM" || signature == "8B64")
                    {
                        // This appears to be an additional layer info block
                        if (reader.BaseStream.Position + 4 <= endPosition)
                        {
                            string key = Encoding.ASCII.GetString(reader.ReadBytes(4));
                            Console.WriteLine($"  Additional block: {signature} - {key}");
                            
                            // Read size if we have enough bytes
                            if (reader.BaseStream.Position + 4 <= endPosition)
                            {
                                uint dataSize = ReadUInt32BE(reader);
                                Console.WriteLine($"  Data size: {dataSize} bytes");
                                
                                // Skip this block's data
                                reader.BaseStream.Position += dataSize;
                            }
                            else
                            {
                                // Not enough data for the size, skip to the end
                                reader.BaseStream.Position = endPosition;
                            }
                        }
                        else
                        {
                            // Not enough data for the key, skip to the end
                            reader.BaseStream.Position = endPosition;
                        }
                    }
                    else
                    {
                        // Not a recognized block, move back and skip 4 bytes
                        reader.BaseStream.Position = blockStart + 4;
                    }
                }
                else
                {
                    // Not enough data for a signature, skip to the end
                    reader.BaseStream.Position = endPosition;
                }
            }
        }
        
        #region Helper Methods
        
        private static ushort ReadUInt16BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            return (ushort)((bytes[0] << 8) | bytes[1]);
        }
        
        private static short ReadInt16BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            return (short)((bytes[0] << 8) | bytes[1]);
        }
        
        private static uint ReadUInt32BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }
        
        private static int ReadInt32BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        }
        
        #endregion
    }
}