using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace PsdReaderApp.Debugging
{
    /// <summary>
    /// Utility to debug and analyze PSD files
    /// </summary>
    public class PsdDebugger
    {
        private const string PSD_SIGNATURE = "8BPS";
        private static readonly byte[] UNICODE_NAME_SIG = Encoding.ASCII.GetBytes("luni");
        
        /// <summary>
        /// Analyzes a PSD file and prints detailed information about layer names
        /// </summary>
        public static void AnalyzeLayerNames(string psdFilePath)
        {
            try
            {
                Console.WriteLine($"Analyzing PSD file: {psdFilePath}");
                
                if (!File.Exists(psdFilePath))
                {
                    Console.WriteLine("File does not exist!");
                    return;
                }
                
                using (var fs = new FileStream(psdFilePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    // Verify PSD signature
                    string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (signature != PSD_SIGNATURE)
                    {
                        Console.WriteLine("Not a valid PSD file (incorrect signature)");
                        return;
                    }
                    
                    // Read version
                    ushort version = ReadUInt16BE(reader);
                    Console.WriteLine($"PSD Version: {version}");
                    
                    // Skip reserved bytes
                    fs.Position += 6;
                    
                    // Read basic header info
                    ushort channels = ReadUInt16BE(reader);
                    uint height = ReadUInt32BE(reader);
                    uint width = ReadUInt32BE(reader);
                    ushort depth = ReadUInt16BE(reader);
                    ushort colorMode = ReadUInt16BE(reader);
                    
                    Console.WriteLine($"Dimensions: {width}x{height} pixels");
                    Console.WriteLine($"Channels: {channels}");
                    Console.WriteLine($"Depth: {depth} bits");
                    Console.WriteLine($"Color Mode: {colorMode}");
                    
                    // Skip Color Mode Data section
                    uint colorModeDataLength = ReadUInt32BE(reader);
                    fs.Position += colorModeDataLength;
                    
                    // Skip Image Resources section
                    uint imageResourcesLength = ReadUInt32BE(reader);
                    fs.Position += imageResourcesLength;
                    
                    // Layer and Mask Info section
                    long layerAndMaskInfoPos = fs.Position;
                    uint layerAndMaskInfoSize = ReadUInt32BE(reader);
                    long layerAndMaskInfoEnd = layerAndMaskInfoPos + 4 + layerAndMaskInfoSize;
                    
                    Console.WriteLine($"\nLayer and Mask Info section at position {layerAndMaskInfoPos}, size: {layerAndMaskInfoSize} bytes");
                    
                    // Layer Info section
                    long layerInfoPos = fs.Position;
                    uint layerInfoSize = ReadUInt32BE(reader);
                    long layerInfoEnd = layerInfoPos + 4 + layerInfoSize;
                    
                    Console.WriteLine($"Layer Info section at position {layerInfoPos}, size: {layerInfoSize} bytes");
                    
                    // Read layer count
                    short layerCount = ReadInt16BE(reader);
                    bool hasAlphaChannel = layerCount < 0;
                    int absLayerCount = Math.Abs(layerCount);
                    
                    Console.WriteLine($"Layer count: {absLayerCount} (raw value: {layerCount})");
                    
                    // Track all found layer names and their positions
                    var layerNames = new List<(int index, string asciiName, string unicodeName, long asciiPos, long unicodePos)>();
                    
                    // Scan all layers
                    for (int i = 0; i < absLayerCount; i++)
                    {
                        Console.WriteLine($"\n--- Layer {i} ---");
                        long layerRecordStart = fs.Position;
                        
                        // Read rectangle
                        int top = ReadInt32BE(reader);
                        int left = ReadInt32BE(reader);
                        int bottom = ReadInt32BE(reader);
                        int right = ReadInt32BE(reader);
                        
                        Console.WriteLine($"Bounds: Top={top}, Left={left}, Bottom={bottom}, Right={right}");
                        
                        // Channel info
                        ushort channelCount = ReadUInt16BE(reader);
                        Console.WriteLine($"Channel count: {channelCount}");
                        
                        // Skip channel info
                        fs.Position += channelCount * 6;
                        
                        // Blend mode
                        byte[] blendSig = reader.ReadBytes(4);
                        byte[] blendKey = reader.ReadBytes(4);
                        
                        string blendSignature = Encoding.ASCII.GetString(blendSig);
                        string blendMode = Encoding.ASCII.GetString(blendKey);
                        
                        Console.WriteLine($"Blend mode: {blendMode} (signature: {blendSignature})");
                        
                        // Other layer properties
                        byte opacity = reader.ReadByte();
                        byte clipping = reader.ReadByte();
                        byte flags = reader.ReadByte();
                        byte filler = reader.ReadByte();
                        
                        Console.WriteLine($"Opacity: {opacity}, Clipping: {clipping}, Flags: {flags:X2}");
                        
                        // Extra data length
                        uint extraDataLength = ReadUInt32BE(reader);
                        long extraDataStart = fs.Position;
                        long extraDataEnd = extraDataStart + extraDataLength;
                        
                        Console.WriteLine($"Extra data: {extraDataLength} bytes from {extraDataStart} to {extraDataEnd}");
                        
                        // Mask data
                        uint maskDataLength = ReadUInt32BE(reader);
                        Console.WriteLine($"Mask data: {maskDataLength} bytes");
                        fs.Position += maskDataLength;
                        
                        // Blending ranges
                        uint blendingRangesLength = ReadUInt32BE(reader);
                        Console.WriteLine($"Blending ranges: {blendingRangesLength} bytes");
                        fs.Position += blendingRangesLength;
                        
                        // Layer name (Pascal string)
                        long asciiNamePos = fs.Position;
                        byte nameLength = reader.ReadByte();
                        byte[] nameBytes = reader.ReadBytes(nameLength);
                        string asciiName = Encoding.ASCII.GetString(nameBytes);
                        
                        Console.WriteLine($"ASCII Name at position {asciiNamePos}: '{asciiName}' (length: {nameLength})");
                        
                        // Calculate name padding
                        int totalNameLength = nameLength + 1; // +1 for length byte
                        int paddedNameLength = ((totalNameLength + 3) / 4) * 4; // Round up to multiple of 4
                        int paddingBytes = paddedNameLength - totalNameLength;
                        
                        Console.WriteLine($"Name padding: {paddingBytes} bytes (total padded length: {paddedNameLength})");
                        
                        // Skip padding
                        fs.Position = asciiNamePos + paddedNameLength;
                        
                        // Record the name
                        string unicodeName = null;
                        long unicodeNamePos = 0;
                        
                        // Look for Unicode name (luni) block
                        Console.WriteLine("Scanning for Unicode name (luni block)...");
                        long currentPos = fs.Position;
                        
                        while (currentPos < extraDataEnd)
                        {
                            // Try to read a signature
                            fs.Position = currentPos;
                            
                            // Make sure we have at least 4 bytes to read
                            if (fs.Position + 4 <= extraDataEnd)
                            {
                                byte[] sig = reader.ReadBytes(4);
                                string sigString = Encoding.ASCII.GetString(sig);
                                
                                // Check if it's the Unicode name block
                                if (sigString == "luni")
                                {
                                    Console.WriteLine($"Found Unicode name block (luni) at position {currentPos}");
                                    
                                    // Read block size
                                    uint blockSize = ReadUInt32BE(reader);
                                    Console.WriteLine($"Unicode name block size: {blockSize} bytes");
                                    
                                    // Record position for unicode name
                                    unicodeNamePos = fs.Position;
                                    
                                    // Read Unicode name length (number of characters)
                                    uint unicodeNameLength = ReadUInt32BE(reader);
                                    Console.WriteLine($"Unicode name length: {unicodeNameLength} characters");
                                    
                                    // Read Unicode name
                                    byte[] unicodeBytes = reader.ReadBytes((int)unicodeNameLength * 2);
                                    unicodeName = Encoding.BigEndianUnicode.GetString(unicodeBytes);
                                    
                                    Console.WriteLine($"Unicode Name: '{unicodeName}'");
                                    
                                    // Skip to end of the block
                                    currentPos += 8 + blockSize; // 4 bytes sig + 4 bytes size + data
                                    break;
                                }
                                else
                                {
                                    // Try to parse this as a standard block to skip it
                                    try
                                    {
                                        // Go back to sig position
                                        fs.Position = currentPos;
                                        
                                        // Log the signature we found
                                        Console.WriteLine($"Found block with signature '{sigString}' at {currentPos}");
                                        
                                        // Read size
                                        uint blockSize = ReadUInt32BE(reader);
                                        Console.WriteLine($"Block size: {blockSize} bytes");
                                        
                                        // Skip to next block
                                        currentPos += 8 + blockSize; // 4 bytes sig + 4 bytes size + data
                                    }
                                    catch
                                    {
                                        // If we can't parse the block, just move forward a byte
                                        currentPos++;
                                    }
                                }
                            }
                            else
                            {
                                // Not enough bytes left to read a signature
                                break;
                            }
                        }
                        
                        // Add layer name info to our list
                        layerNames.Add((i, asciiName, unicodeName, asciiNamePos, unicodeNamePos));
                        
                        // Skip to the end of this layer's extra data
                        fs.Position = extraDataEnd;
                    }
                    
                    // Display summary of all layer names
                    Console.WriteLine("\n\n=== LAYER NAMES SUMMARY ===");
                    foreach (var item in layerNames)
                    {
                        Console.WriteLine($"Layer {item.index}: ASCII Name: '{item.asciiName}' at pos {item.asciiPos}");
                        
                        if (item.unicodeName != null)
                        {
                            Console.WriteLine($"           Unicode Name: '{item.unicodeName}' at pos {item.unicodePos}");
                        }
                        else
                        {
                            Console.WriteLine("           Unicode Name: Not found");
                        }
                    }
                    
                    // Verify if we've read all the layer info correctly
                    if (fs.Position != layerInfoEnd)
                    {
                        Console.WriteLine($"\nWARNING: Current position ({fs.Position}) does not match expected layer info end ({layerInfoEnd})");
                        Console.WriteLine($"Difference: {fs.Position - layerInfoEnd} bytes");
                    }
                    else
                    {
                        Console.WriteLine("\nLayer info section parsed correctly.");
                    }
                }
                
                Console.WriteLine("\nAnalysis complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during analysis: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        
        #region Binary Helper Methods
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
            return (int)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }
        #endregion
    }
}