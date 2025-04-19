using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace PsdReaderApp.Core
{
    /// <summary>
    /// Class that handles renaming layers in PSD files, including Unicode layer names
    /// </summary>
    public class UnicodePsdLayerRenamer
    {
        private const string PSD_SIGNATURE = "8BPS";
        private const ushort PSD_VERSION = 1;
        
        /// <summary>
        /// Renames a layer in a PSD file, handling both ASCII and Unicode layer names
        /// </summary>
        /// <param name="inputPath">Path to the input PSD file</param>
        /// <param name="outputPath">Path to save the modified PSD file</param>
        /// <param name="layerIndex">Zero-based index of the layer to rename</param>
        /// <param name="newName">New name to assign to the layer</param>
        /// <returns>True if the operation was successful</returns>
        public static bool RenameLayer(string inputPath, string outputPath, int layerIndex, string newName)
        {
            Console.WriteLine($"Attempting to rename layer {layerIndex} to '{newName}'");
            
            try
            {
                // Always create a new copy of the file to avoid modifying the original
                File.Copy(inputPath, outputPath, true);
                
                // First scan the file to locate layers and their names
                var layerNames = ScanLayerNames(outputPath);
                
                // Check if we found the requested layer
                if (layerIndex < 0 || layerIndex >= layerNames.Count)
                {
                    throw new Exception($"Layer index {layerIndex} is out of range (0-{layerNames.Count - 1})");
                }
                
                // Get the layer name info
                var layerInfo = layerNames[layerIndex];
                
                Console.WriteLine($"Found layer {layerIndex} with name '{layerInfo.AsciiName}'");
                if (layerInfo.HasUnicodeName)
                {
                    Console.WriteLine($"This layer also has a Unicode name: '{layerInfo.UnicodeName}'");
                }
                
                // Now update the layer names
                using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite))
                using (var writer = new BinaryWriter(fs))
                {
                    // Update ASCII name
                    if (layerInfo.AsciiNamePosition > 0)
                    {
                        UpdateAsciiName(fs, writer, layerInfo.AsciiNamePosition, layerInfo.AsciiName, newName);
                    }
                    
                    // Update Unicode name if it exists
                    if (layerInfo.HasUnicodeName && layerInfo.UnicodeNamePosition > 0)
                    {
                        UpdateUnicodeName(fs, writer, layerInfo.UnicodeNamePosition, layerInfo.UnicodeName, newName);
                    }
                }
                
                // Verify the changes
                var updatedLayers = ScanLayerNames(outputPath);
                if (layerIndex < updatedLayers.Count)
                {
                    var updatedLayer = updatedLayers[layerIndex];
                    Console.WriteLine($"Verification: Layer {layerIndex} ASCII name is now '{updatedLayer.AsciiName}'");
                    if (updatedLayer.HasUnicodeName)
                    {
                        Console.WriteLine($"Verification: Layer {layerIndex} Unicode name is now '{updatedLayer.UnicodeName}'");
                    }
                    
                    // Check if the names match what we wanted
                    bool asciiMatches = updatedLayer.AsciiName == newName;
                    bool unicodeMatches = !updatedLayer.HasUnicodeName || updatedLayer.UnicodeName == newName;
                    
                    if (!asciiMatches)
                    {
                        Console.WriteLine($"WARNING: ASCII name doesn't match expected value '{newName}'");
                    }
                    
                    if (!unicodeMatches)
                    {
                        Console.WriteLine($"WARNING: Unicode name doesn't match expected value '{newName}'");
                    }
                    
                    return asciiMatches && unicodeMatches;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }
        
        /// <summary>
        /// Scans a PSD file and extracts information about each layer's name
        /// </summary>
        private static List<LayerNameInfo> ScanLayerNames(string psdFilePath)
        {
            var layerNames = new List<LayerNameInfo>();
            
            using (var fs = new FileStream(psdFilePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                // Verify PSD signature
                string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (signature != PSD_SIGNATURE)
                {
                    throw new Exception("Not a valid PSD file");
                }
                
                // Skip the header
                fs.Position = 26;
                
                // Skip Color Mode Data section
                uint colorModeDataLength = ReadUInt32BE(reader);
                fs.Position += colorModeDataLength;
                
                // Skip Image Resources section
                uint imageResourcesLength = ReadUInt32BE(reader);
                fs.Position += imageResourcesLength;
                
                // Layer and Mask Info section
                long layerAndMaskInfoPos = fs.Position;
                uint layerAndMaskInfoSize = ReadUInt32BE(reader);
                
                // Layer Info section
                long layerInfoPos = fs.Position;
                uint layerInfoSize = ReadUInt32BE(reader);
                
                // Read layer count
                short layerCount = ReadInt16BE(reader);
                int absLayerCount = Math.Abs(layerCount);
                
                Console.WriteLine($"File has {absLayerCount} layers");
                
                // Process each layer
                for (int i = 0; i < absLayerCount; i++)
                {
                    var nameInfo = new LayerNameInfo { LayerIndex = i };
                    
                    // Skip rectangle
                    fs.Position += 16;
                    
                    // Skip channel info
                    ushort channelCount = ReadUInt16BE(reader);
                    fs.Position += channelCount * 6;
                    
                    // Skip blend mode
                    fs.Position += 8;
                    
                    // Skip opacity, clipping, flags, filler
                    fs.Position += 4;
                    
                    // Extra data length
                    uint extraDataLength = ReadUInt32BE(reader);
                    long extraDataStart = fs.Position;
                    long extraDataEnd = extraDataStart + extraDataLength;
                    
                    // Skip mask data
                    uint maskDataLength = ReadUInt32BE(reader);
                    fs.Position += maskDataLength;
                    
                    // Skip blending ranges
                    uint blendingRangesLength = ReadUInt32BE(reader);
                    fs.Position += blendingRangesLength;
                    
                    // Read ASCII layer name
                    nameInfo.AsciiNamePosition = fs.Position;
                    byte nameLength = reader.ReadByte();
                    byte[] nameBytes = reader.ReadBytes(nameLength);
                    nameInfo.AsciiName = Encoding.ASCII.GetString(nameBytes);
                    
                    // Calculate name padding
                    int totalNameLength = nameLength + 1; // +1 for length byte
                    int paddedNameLength = ((totalNameLength + 3) / 4) * 4; // Round up to multiple of 4
                    
                    // Skip padding
                    fs.Position = nameInfo.AsciiNamePosition + paddedNameLength;
                    
                    // Now look for Unicode name
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
                                // Read block size
                                uint blockSize = ReadUInt32BE(reader);
                                
                                // Record position for unicode name
                                nameInfo.UnicodeNamePosition = fs.Position;
                                
                                // Read Unicode name length (number of characters)
                                uint unicodeNameLength = ReadUInt32BE(reader);
                                
                                // Read Unicode name
                                byte[] unicodeBytes = reader.ReadBytes((int)unicodeNameLength * 2);
                                nameInfo.UnicodeName = Encoding.BigEndianUnicode.GetString(unicodeBytes);
                                nameInfo.HasUnicodeName = true;
                                
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
                                    
                                    // Read size
                                    uint blockSize = ReadUInt32BE(reader);
                                    
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
                    
                    layerNames.Add(nameInfo);
                    
                    // Skip to the end of this layer's extra data
                    fs.Position = extraDataEnd;
                }
            }
            
            return layerNames;
        }
        
        /// <summary>
        /// Updates the ASCII layer name in the PSD file
        /// </summary>
        private static void UpdateAsciiName(FileStream fs, BinaryWriter writer, long position, string oldName, string newName)
        {
            fs.Position = position;
            
            // Read old name length
            byte oldNameLength = (byte)fs.ReadByte();
            
            // Calculate padding for old name
            int oldTotalLength = oldNameLength + 1; // +1 for length byte
            int oldPaddedLength = ((oldTotalLength + 3) / 4) * 4; // Round up to multiple of 4
            
            // Prepare new name
            byte[] newNameBytes = Encoding.ASCII.GetBytes(newName);
            byte newNameLength = (byte)Math.Min(255, newNameBytes.Length);
            
            // Calculate padding for new name
            int newTotalLength = newNameLength + 1; // +1 for length byte
            int newPaddedLength = ((newTotalLength + 3) / 4) * 4; // Round up to multiple of 4
            
            // Check if sizes are the same (for now, only handle same-size names)
            if (oldPaddedLength != newPaddedLength)
            {
                Console.WriteLine("WARNING: ASCII names with different padded lengths require full file reconstruction");
                Console.WriteLine("Only updating if same padded length to avoid breaking the file structure");
            }
            
            if (oldPaddedLength == newPaddedLength)
            {
                // Go back to name position
                fs.Position = position;
                
                // Write new name
                writer.Write(newNameLength);
                writer.Write(newNameBytes, 0, newNameLength);
                
                // Write padding
                int paddingBytes = oldPaddedLength - (newNameLength + 1);
                for (int i = 0; i < paddingBytes; i++)
                {
                    writer.Write((byte)0);
                }
                
                Console.WriteLine($"Updated ASCII name from '{oldName}' to '{newName}'");
            }
        }
        
        /// <summary>
        /// Updates the Unicode layer name in the PSD file
        /// </summary>
        private static void UpdateUnicodeName(FileStream fs, BinaryWriter writer, long position, string oldName, string newName)
        {
            // Position at the Unicode name length field
            fs.Position = position;
            
            // Read the old name length
            uint oldCharCount = ReadUInt32BE(fs);
            
            // Calculate old data size
            uint oldDataSize = oldCharCount * 2; // UTF-16 = 2 bytes per char
            
            // Prepare new name data
            byte[] newNameBytes = Encoding.BigEndianUnicode.GetBytes(newName);
            uint newCharCount = (uint)(newName.Length);
            
            // Go back to position
            fs.Position = position;
            
            // Write new character count
            WriteUInt32BE(writer, newCharCount);
            
            // Write new UTF-16 string
            writer.Write(newNameBytes);
            
            Console.WriteLine($"Updated Unicode name from '{oldName}' to '{newName}'");
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
        
        private static uint ReadUInt32BE(FileStream fs)
        {
            byte[] bytes = new byte[4];
            fs.Read(bytes, 0, 4);
            return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }
        
        private static void WriteUInt32BE(BinaryWriter writer, uint value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }
        #endregion
        
        /// <summary>
        /// Class to store information about a layer's name
        /// </summary>
        private class LayerNameInfo
        {
            public int LayerIndex { get; set; }
            public string AsciiName { get; set; }
            public long AsciiNamePosition { get; set; }
            public bool HasUnicodeName { get; set; }
            public string UnicodeName { get; set; }
            public long UnicodeNamePosition { get; set; }
        }
    }
}