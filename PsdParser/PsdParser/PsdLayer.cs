using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace PsdReaderApp.Core
{
    /// <summary>
    /// Class that handles renaming layers in PSD files, including all instances of layer names
    /// with support for different length names
    /// </summary>
    public class PsdLayer
    {
        private const string PSD_SIGNATURE = "8BPS";
        private const ushort PSD_VERSION = 1;
        private const string UNICODE_NAME_KEY = "luni";
        private const string DESCRIPTOR_KEY = "tdta";
        
        /// <summary>
        /// Renames a layer in a PSD file, handling all instances of layer names
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
                // First check if the file exists
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException($"Input file {inputPath} not found");
                }
                
                // First scan the file to locate layers and their names
                var layerNames = ScanLayerNames(inputPath);
                
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
                
                // Check if we need file reconstruction
                bool needsReconstruction = NeedsReconstruction(layerInfo, newName);
                
                if (needsReconstruction)
                {
                    Console.WriteLine("Layer name length change requires full file reconstruction");
                    return ReconstructFileWithNewLayerName(inputPath, outputPath, layerIndex, newName, layerNames);
                }
                else
                {
                    // Simple in-place update if sizes match
                    File.Copy(inputPath, outputPath, true);
                    return UpdateLayerNameInPlace(outputPath, layerInfo, newName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }
        
        /// <summary>
        /// Determines if the file needs reconstruction based on name length changes
        /// </summary>
        private static bool NeedsReconstruction(LayerNameInfo layerInfo, string newName)
        {
            // Check if ASCII name padding would change
            byte oldNameLength = (byte)layerInfo.AsciiName.Length;
            byte newNameLength = (byte)Math.Min(255, newName.Length);
            
            int oldTotalLength = oldNameLength + 1; // +1 for length byte
            int oldPaddedLength = ((oldTotalLength + 3) / 4) * 4; // Round up to multiple of 4
            
            int newTotalLength = newNameLength + 1; // +1 for length byte
            int newPaddedLength = ((newTotalLength + 3) / 4) * 4; // Round up to multiple of 4
            
            return oldPaddedLength != newPaddedLength;
        }
        
        /// <summary>
        /// Updates layer name in-place when padding sizes match
        /// </summary>
        private static bool UpdateLayerNameInPlace(string filePath, LayerNameInfo layerInfo, string newName)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
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
                
                // Update Descriptor name if it exists
                if (layerInfo.HasDescriptorName && layerInfo.DescriptorNamePositions.Count > 0)
                {
                    foreach (var position in layerInfo.DescriptorNamePositions)
                    {
                        UpdateDescriptorName(fs, writer, position, layerInfo.DescriptorName, newName);
                    }
                }
                
                // Update any additional layer name references
                if (layerInfo.AdditionalNameInfoBlocks.Count > 0)
                {
                    foreach (var block in layerInfo.AdditionalNameInfoBlocks)
                    {
                        UpdateAdditionalNameBlock(fs, writer, block.Key, block.Position, block.Size, newName);
                    }
                }
            }
            
            // Verify changes
            var updatedLayers = ScanLayerNames(filePath);
            if (layerInfo.LayerIndex < updatedLayers.Count)
            {
                var updatedLayer = updatedLayers[layerInfo.LayerIndex];
                Console.WriteLine($"Verification: Layer {layerInfo.LayerIndex} ASCII name is now '{updatedLayer.AsciiName}'");
                
                if (updatedLayer.HasUnicodeName)
                {
                    Console.WriteLine($"Verification: Layer {layerInfo.LayerIndex} Unicode name is now '{updatedLayer.UnicodeName}'");
                }
                
                // Check if the names match what we wanted
                bool allNamesMatch = true;
                
                if (updatedLayer.AsciiName != newName)
                {
                    Console.WriteLine($"WARNING: ASCII name doesn't match expected value '{newName}'");
                    allNamesMatch = false;
                }
                
                if (updatedLayer.HasUnicodeName && updatedLayer.UnicodeName != newName)
                {
                    Console.WriteLine($"WARNING: Unicode name doesn't match expected value '{newName}'");
                    allNamesMatch = false;
                }
                
                return allNamesMatch;
            }
            
            return true;
        }
        
        /// <summary>
        /// Reconstructs the entire PSD file to accommodate name length changes
        /// </summary>
        private static bool ReconstructFileWithNewLayerName(string inputPath, string outputPath, int targetLayerIndex, string newName, List<LayerNameInfo> layers)
        {
            // Create new file for output
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            
            try
            {
                using (var inputFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
                using (var outputFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                using (var reader = new BinaryReader(inputFs))
                using (var writer = new BinaryWriter(outputFs))
                {
                    // Copy file signature and header (26 bytes)
                    byte[] header = reader.ReadBytes(26);
                    writer.Write(header);
                    
                    // Copy color mode data section
                    uint colorModeDataLength = ReadUInt32BE(reader);
                    WriteUInt32BE(writer, colorModeDataLength);
                    CopyBlock(reader, writer, colorModeDataLength);
                    
                    // Copy image resources section
                    uint imageResourcesLength = ReadUInt32BE(reader);
                    WriteUInt32BE(writer, imageResourcesLength);
                    CopyBlock(reader, writer, imageResourcesLength);
                    
                    // Process layer and mask info section
                    ProcessLayerAndMaskInfoSection(reader, writer, targetLayerIndex, newName, layers);
                    
                    // Copy remaining data (image data)
                    CopyRemaining(reader, writer);
                }
                
                // Verify the changes
                bool success = VerifyChanges(outputPath, targetLayerIndex, newName);
                if (success)
                {
                    Console.WriteLine($"Layer name change with reconstruction completed successfully!");
                }
                else
                {
                    Console.WriteLine($"Layer name change incomplete after reconstruction");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during reconstruction: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }
        
        /// <summary>
        /// Process the layer and mask info section, updating layer names as needed
        /// </summary>
        private static void ProcessLayerAndMaskInfoSection(BinaryReader reader, BinaryWriter writer, int targetLayerIndex, string newName, List<LayerNameInfo> layers)
        {
            // Layer and mask info section - we need to reconstruct this
            long sectionStart = reader.BaseStream.Position;
            uint sectionLength = ReadUInt32BE(reader);
            long sectionEnd = sectionStart + 4 + sectionLength;
            
            // We'll buffer the section and modify it before writing
            using (var sectionMs = new MemoryStream())
            using (var sectionWriter = new BinaryWriter(sectionMs))
            {
                // Process layer info section
                ProcessLayerInfoSection(reader, sectionWriter, targetLayerIndex, newName, layers);
                
                // Copy any remaining layer and mask info data
                long currentPos = reader.BaseStream.Position;
                long remainingBytes = sectionEnd - currentPos;
                if (remainingBytes > 0)
                {
                    CopyBlock(reader, sectionWriter, (uint)remainingBytes);
                }
                
                // Get final section data
                byte[] sectionData = sectionMs.ToArray();
                
                // Write section length and data to the output file
                WriteUInt32BE(writer, (uint)sectionData.Length);
                writer.Write(sectionData);
            }
        }
        
        /// <summary>
        /// Process the layer info section, rebuilding it with new layer names
        /// </summary>
        private static void ProcessLayerInfoSection(BinaryReader reader, BinaryWriter writer, int targetLayerIndex, string newName, List<LayerNameInfo> layers)
        {
            // Capture layer info section start
            long sectionStart = reader.BaseStream.Position;
            uint sectionLength = ReadUInt32BE(reader);
            long sectionEnd = sectionStart + 4 + sectionLength;
            
            // Create memory stream for modified layer info
            using (var layerInfoMs = new MemoryStream())
            using (var layerInfoWriter = new BinaryWriter(layerInfoMs))
            {
                // Read layer count
                short layerCount = ReadInt16BE(reader);
                WriteInt16BE(layerInfoWriter, layerCount);
                
                int absLayerCount = Math.Abs(layerCount);
                
                // Process each layer record
                for (int i = 0; i < absLayerCount; i++)
                {
                    bool isTargetLayer = (i == targetLayerIndex);
                    
                    // Layer bounds
                    int top = ReadInt32BE(reader);
                    int left = ReadInt32BE(reader);
                    int bottom = ReadInt32BE(reader);
                    int right = ReadInt32BE(reader);
                    
                    WriteInt32BE(layerInfoWriter, top);
                    WriteInt32BE(layerInfoWriter, left);
                    WriteInt32BE(layerInfoWriter, bottom);
                    WriteInt32BE(layerInfoWriter, right);
                    
                    // Channels info
                    ushort channelCount = ReadUInt16BE(reader);
                    WriteUInt16BE(layerInfoWriter, channelCount);
                    
                    // Channel data
                    for (int c = 0; c < channelCount; c++)
                    {
                        short channelId = ReadInt16BE(reader);
                        uint channelLength = ReadUInt32BE(reader);
                        
                        WriteInt16BE(layerInfoWriter, channelId);
                        WriteUInt32BE(layerInfoWriter, channelLength);
                    }
                    
                    // Blend mode signature and key
                    byte[] blendSig = reader.ReadBytes(4);
                    byte[] blendKey = reader.ReadBytes(4);
                    
                    layerInfoWriter.Write(blendSig);
                    layerInfoWriter.Write(blendKey);
                    
                    // Layer properties
                    byte opacity = reader.ReadByte();
                    byte clipping = reader.ReadByte();
                    byte flags = reader.ReadByte();
                    byte filler = reader.ReadByte();
                    
                    layerInfoWriter.Write(opacity);
                    layerInfoWriter.Write(clipping);
                    layerInfoWriter.Write(flags);
                    layerInfoWriter.Write(filler);
                    
                    // Extra data sections
                    uint extraDataLength = ReadUInt32BE(reader);
                    long extraDataStart = reader.BaseStream.Position;
                    long extraDataEnd = extraDataStart + extraDataLength;
                    
                    // Create buffer for extra data to modify
                    using (var extraDataMs = new MemoryStream())
                    using (var extraDataWriter = new BinaryWriter(extraDataMs))
                    {
                        // Copy mask data
                        uint maskDataSize = ReadUInt32BE(reader);
                        WriteUInt32BE(extraDataWriter, maskDataSize);
                        CopyBlock(reader, extraDataWriter, maskDataSize);
                        
                        // Copy layer blending ranges
                        uint blendingRangesSize = ReadUInt32BE(reader);
                        WriteUInt32BE(extraDataWriter, blendingRangesSize);
                        CopyBlock(reader, extraDataWriter, blendingRangesSize);
                        
                        // Layer name - this is where the change happens
                        byte nameLength = reader.ReadByte();
                        byte[] nameBytes = reader.ReadBytes(nameLength);
                        string currentName = Encoding.ASCII.GetString(nameBytes);
                        
                        // Calculate original padding
                        int origTotalLength = nameLength + 1; // +1 for length byte
                        int origPaddedLength = ((origTotalLength + 3) / 4) * 4; // Round up to multiple of 4
                        
                        // Skip original padding bytes
                        reader.BaseStream.Position += (origPaddedLength - origTotalLength);
                        
                        // Write new name (if this is the target layer)
                        string nameToWrite = isTargetLayer ? newName : currentName;
                        byte[] newNameBytes = Encoding.ASCII.GetBytes(nameToWrite);
                        byte newNameLength = (byte)Math.Min(255, newNameBytes.Length);
                        
                        extraDataWriter.Write(newNameLength);
                        extraDataWriter.Write(newNameBytes, 0, newNameLength);
                        
                        // Calculate and write new padding
                        int newTotalLength = newNameLength + 1; // +1 for length byte
                        int newPaddedLength = ((newTotalLength + 3) / 4) * 4; // Round up to multiple of 4
                        int newPadding = newPaddedLength - newTotalLength;
                        
                        for (int p = 0; p < newPadding; p++)
                        {
                            extraDataWriter.Write((byte)0);
                        }
                        
                        // Now copy and process additional layer info blocks
                        long currentPos = reader.BaseStream.Position;
                        
                        while (currentPos < extraDataEnd)
                        {
                            // Check for signature
                            if (currentPos + 8 <= extraDataEnd)
                            {
                                reader.BaseStream.Position = currentPos;
                                
                                byte[] sigBytes = reader.ReadBytes(4);
                                string signature = Encoding.ASCII.GetString(sigBytes);
                                
                                byte[] keyBytes = reader.ReadBytes(4);
                                string key = Encoding.ASCII.GetString(keyBytes);
                                
                                uint blockSize = ReadUInt32BE(reader);
                                long blockStart = reader.BaseStream.Position;
                                long blockEnd = blockStart + blockSize;
                                
                                // Write signature and key
                                extraDataWriter.Write(sigBytes);
                                extraDataWriter.Write(keyBytes);
                                
                                // Handle special blocks
                                if (isTargetLayer && signature == "8BIM" && key == UNICODE_NAME_KEY)
                                {
                                    // Unicode name block - replace with new name
                                    byte[] unicodeNameData = reader.ReadBytes((int)blockSize);
                                    
                                    // Create new Unicode block
                                    using (var unicodeMs = new MemoryStream())
                                    using (var unicodeWriter = new BinaryWriter(unicodeMs))
                                    {
                                        // Write Unicode string length (character count)
                                        WriteUInt32BE(unicodeWriter, (uint)newName.Length);
                                        
                                        // Write Unicode string
                                        byte[] unicodeNameBytes = Encoding.BigEndianUnicode.GetBytes(newName);
                                        unicodeWriter.Write(unicodeNameBytes);
                                        
                                        // Get new block data
                                        byte[] newUnicodeData = unicodeMs.ToArray();
                                        
                                        // Write block size and data
                                        WriteUInt32BE(extraDataWriter, (uint)newUnicodeData.Length);
                                        extraDataWriter.Write(newUnicodeData);
                                    }
                                }
                                else if (isTargetLayer && (key == "tdta" || key == "shmd"))
                                {
                                    // These blocks might contain descriptor name references
                                    // Create a modified version with updated name references
                                    byte[] blockData = reader.ReadBytes((int)blockSize);
                                    byte[] newBlockData = ProcessDescriptorBlock(blockData, currentName, newName);
                                    
                                    // Write block size and modified data
                                    WriteUInt32BE(extraDataWriter, (uint)newBlockData.Length);
                                    extraDataWriter.Write(newBlockData);
                                }
                                else
                                {
                                    // Copy block as-is
                                    WriteUInt32BE(extraDataWriter, blockSize);
                                    CopyBlock(reader, extraDataWriter, blockSize);
                                }
                                
                                // Move to next block
                                currentPos = blockEnd;
                            }
                            else
                            {
                                // Not enough bytes for a full block
                                // Copy remaining bytes as-is
                                long bytesRemaining = extraDataEnd - currentPos;
                                reader.BaseStream.Position = currentPos;
                                
                                byte[] remainingBytes = reader.ReadBytes((int)bytesRemaining);
                                extraDataWriter.Write(remainingBytes);
                                
                                currentPos = extraDataEnd;
                            }
                        }
                        
                        // Write the extra data
                        byte[] extraData = extraDataMs.ToArray();
                        WriteUInt32BE(layerInfoWriter, (uint)extraData.Length);
                        layerInfoWriter.Write(extraData);
                    }
                }
                
                // Copy layer pixel data which follows the layer records
                long pixelDataStart = reader.BaseStream.Position;
                long pixelDataLength = sectionEnd - pixelDataStart;
                
                if (pixelDataLength > 0)
                {
                    CopyBlock(reader, layerInfoWriter, (uint)pixelDataLength);
                }
                
                // Write the complete layer info section
                byte[] layerInfoData = layerInfoMs.ToArray();
                WriteUInt32BE(writer, (uint)layerInfoData.Length);
                writer.Write(layerInfoData);
            }
        }
        
        /// <summary>
        /// Process a descriptor block to update layer name references
        /// </summary>
        private static byte[] ProcessDescriptorBlock(byte[] blockData, string oldName, string newName)
        {
            // This is a placeholder for descriptor processing
            // In a real implementation, you'd need to parse the descriptor format
            // and update name references
            
            // For simple demonstration, just look for UTF string markers followed by name
            using (var ms = new MemoryStream(blockData))
            using (var reader = new BinaryReader(ms))
            using (var outMs = new MemoryStream())
            using (var writer = new BinaryWriter(outMs))
            {
                long length = blockData.Length;
                long pos = 0;
                
                while (pos < length - 8)
                {
                    ms.Position = pos;
                    
                    // Look for UTF string type marker (0x03) followed by "nam" in hex
                    if (reader.ReadByte() == 0x03 && 
                        reader.ReadByte() == 0x6E && // 'n'
                        reader.ReadByte() == 0x61 && // 'a'
                        reader.ReadByte() == 0x6D)   // 'm'
                    {
                        // This might be a name reference
                        // Read string length
                        uint stringLength = ReadUInt32BE(reader);
                        
                        // Read string data
                        byte[] stringBytes = reader.ReadBytes((int)stringLength * 2); // 2 bytes per UTF-16 char
                        string stringValue = Encoding.BigEndianUnicode.GetString(stringBytes);
                        
                        // If this matches the old name, we'll replace it
                        if (stringValue == oldName)
                        {
                            // Write the marker and type bytes
                            ms.Position = pos;
                            byte[] marker = reader.ReadBytes(4);
                            writer.Write(marker);
                            
                            // Write new string length
                            WriteUInt32BE(writer, (uint)newName.Length);
                            
                            // Write new string
                            byte[] newStringBytes = Encoding.BigEndianUnicode.GetBytes(newName);
                            writer.Write(newStringBytes);
                            
                            // Skip ahead past the replaced data
                            pos += 4 + 4 + stringLength * 2;
                        }
                        else
                        {
                            // Not a matching name, copy as is
                            ms.Position = pos;
                            writer.Write(reader.ReadByte());
                            pos++;
                        }
                    }
                    else
                    {
                        // Not a UTF string marker, copy as is
                        ms.Position = pos;
                        writer.Write(reader.ReadByte());
                        pos++;
                    }
                }
                
                // Copy any remaining bytes
                if (pos < length)
                {
                    ms.Position = pos;
                    byte[] remaining = reader.ReadBytes((int)(length - pos));
                    writer.Write(remaining);
                }
                
                return outMs.ToArray();
            }
        }
        
        /// <summary>
        /// Verify that the layer name changes were applied correctly
        /// </summary>
        private static bool VerifyChanges(string filePath, int layerIndex, string expectedName)
        {
            var layers = ScanLayerNames(filePath);
            
            if (layerIndex >= layers.Count)
            {
                Console.WriteLine($"Error: Could not verify changes - layer index {layerIndex} not found");
                return false;
            }
            
            var layerInfo = layers[layerIndex];
            
            Console.WriteLine($"Verification: Layer {layerIndex} ASCII name is now '{layerInfo.AsciiName}'");
            if (layerInfo.HasUnicodeName)
            {
                Console.WriteLine($"Verification: Layer {layerIndex} Unicode name is now '{layerInfo.UnicodeName}'");
            }
            
            bool allNamesMatch = true;
            
            if (layerInfo.AsciiName != expectedName)
            {
                Console.WriteLine($"WARNING: ASCII name doesn't match expected value '{expectedName}'");
                allNamesMatch = false;
            }
            
            if (layerInfo.HasUnicodeName && layerInfo.UnicodeName != expectedName)
            {
                Console.WriteLine($"WARNING: Unicode name doesn't match expected value '{expectedName}'");
                allNamesMatch = false;
            }
            
            return allNamesMatch;
        }
        
        /// <summary>
        /// Scans a PSD file and extracts information about each layer's name in all formats
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
                    
                    // Now look for additional blocks like Unicode name and descriptor blocks
                    ScanAdditionalLayerInfo(reader, extraDataEnd, nameInfo);
                    
                    layerNames.Add(nameInfo);
                    
                    // Skip to the end of this layer's extra data
                    fs.Position = extraDataEnd;
                }
            }
            
            return layerNames;
        }
        
        /// <summary>
        /// Scan for additional layer information blocks that might contain layer names
        /// </summary>
        private static void ScanAdditionalLayerInfo(BinaryReader reader, long extraDataEnd, LayerNameInfo nameInfo)
        {
            long currentPos = reader.BaseStream.Position;
            
            while (currentPos < extraDataEnd)
            {
                // Try to read a signature
                reader.BaseStream.Position = currentPos;
                
                // Make sure we have at least 8 bytes to read (signature + key)
                if (reader.BaseStream.Position + 8 <= extraDataEnd)
                {
                    string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    string key = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    
                    // Process if it's a valid additional info block (8BIM or 8B64)
                    if (signature == "8BIM" || signature == "8B64")
                    {
                        // Get block size
                        uint blockSize = ReadUInt32BE(reader);
                        long blockStart = reader.BaseStream.Position;
                        long blockEnd = blockStart + blockSize;
                        
                        // Handle known block types
                        if (key == UNICODE_NAME_KEY) // Unicode layer name
                        {
                            nameInfo.UnicodeNamePosition = reader.BaseStream.Position;
                            uint unicodeNameLength = ReadUInt32BE(reader);
                            
                            // Read Unicode name
                            byte[] unicodeBytes = reader.ReadBytes((int)unicodeNameLength * 2);
                            nameInfo.UnicodeName = Encoding.BigEndianUnicode.GetString(unicodeBytes);
                            nameInfo.HasUnicodeName = true;
                            
                            // Move to end of block
                            currentPos = blockEnd;
                        }
                        else if (key == DESCRIPTOR_KEY || key == "shmd") // Layer descriptor or metadata with names
                        {
                            // Scan for UTF string with name (often found in adjustment layers, smart objects)
                            ScanDescriptorForNames(reader, blockStart, blockEnd, nameInfo);
                            
                            // Track all blocks that might contain names for potential updates
                            nameInfo.AdditionalNameInfoBlocks.Add(new AdditionalNameBlock
                            {
                                Key = key,
                                Position = blockStart,
                                Size = blockSize
                            });
                            
                            // Move to end of block
                            currentPos = blockEnd;
                        }
                        else
                        {
                            // Track all blocks for debugging
                            nameInfo.BlockKeys.Add(key);
                            
                            // Skip to end of this block
                            currentPos = blockEnd;
                        }
                    }
                    else
                    {
// Not a valid signature, move forward by 1 byte
                        currentPos++;
                    }
                }
                else
                {
                    // Not enough bytes remaining for a full block
                    break;
                }
            }
        }
        
        /// <summary>
        /// Scan descriptor blocks for potential name references
        /// </summary>
        private static void ScanDescriptorForNames(BinaryReader reader, long blockStart, long blockEnd, LayerNameInfo nameInfo)
        {
            long currentPos = reader.BaseStream.Position;
            byte[] buffer = new byte[(int)(blockEnd - blockStart)];
            
            // Read the entire block into memory for faster scanning
            reader.BaseStream.Position = blockStart;
            reader.Read(buffer, 0, buffer.Length);
            
            // Restore position
            reader.BaseStream.Position = currentPos;
            
            // Look for name field in descriptor
            for (int i = 0; i < buffer.Length - 8; i++)
            {
                if (buffer[i] == 0x03 && buffer[i+1] == 0x63 && buffer[i+2] == 0x6E && buffer[i+3] == 0x61 && buffer[i+4] == 0x6D) // Pattern: UTF string type + "nam" in big-endian
                {
                    // Potential name field found
                    long namePos = blockStart + i + 6; // Skip type byte and "nam"
                    
                    // Read string length at this position
                    reader.BaseStream.Position = namePos;
                    uint nameLength = ReadUInt32BE(reader);
                    
                    if (nameLength > 0 && nameLength < 256 && reader.BaseStream.Position + nameLength * 2 <= blockEnd)
                    {
                        // Read name
                        byte[] nameBytes = reader.ReadBytes((int)nameLength * 2);
                        string descriptorName = Encoding.BigEndianUnicode.GetString(nameBytes);
                        
                        // Check if this descriptor name matches ASCII or Unicode name
                        if (descriptorName == nameInfo.AsciiName || 
                            (nameInfo.HasUnicodeName && descriptorName == nameInfo.UnicodeName))
                        {
                            nameInfo.HasDescriptorName = true;
                            nameInfo.DescriptorName = descriptorName;
                            nameInfo.DescriptorNamePositions.Add(namePos);
                        }
                    }
                }
            }
            
            // Restore position
            reader.BaseStream.Position = currentPos;
        }
        
        /// <summary>
        /// Updates the ASCII layer name in the PSD file when sizes match
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
            
            // We should only be here if sizes match
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
        
        /// <summary>
        /// Updates a descriptor name in the PSD file
        /// </summary>
        private static void UpdateDescriptorName(FileStream fs, BinaryWriter writer, long position, string oldName, string newName)
        {
            // Position at the descriptor name length field
            fs.Position = position;
            
            // Read the old name length
            uint oldCharCount = ReadUInt32BE(fs);
            
            // Calculate old data size
            uint oldDataSize = oldCharCount * 2; // UTF-16 = 2 bytes per char
            
            // Prepare new name data
            byte[] newNameBytes = Encoding.BigEndianUnicode.GetBytes(newName);
            uint newCharCount = (uint)(newName.Length);
            
            // Only update if same length to avoid breaking file structure
            if (oldCharCount == newCharCount)
            {
                // Go back to position
                fs.Position = position;
                
                // Write new character count
                WriteUInt32BE(writer, newCharCount);
                
                // Write new UTF-16 string
                writer.Write(newNameBytes);
                
                Console.WriteLine($"Updated descriptor name from '{oldName}' to '{newName}'");
            }
            else
            {
                Console.WriteLine($"WARNING: Cannot update descriptor name at position {position} - length mismatch");
            }
        }
        
        /// <summary>
        /// Update additional blocks that might contain layer names
        /// This requires deeper inspection and specialized handling
        /// </summary>
        private static void UpdateAdditionalNameBlock(FileStream fs, BinaryWriter writer, string blockKey, long position, uint size, string newName)
        {
            // For advanced blocks, we need specialized code based on block type
            Console.WriteLine($"Scanning additional name block '{blockKey}' at position {position}");
            
            // Read block into memory for manipulation
            fs.Position = position;
            byte[] blockData = new byte[size];
            fs.Read(blockData, 0, (int)size);
            
            bool modified = false;
            
            // Different handling based on block type
            if (blockKey == "tdta" || blockKey == "shmd")
            {
                // These blocks have complex descriptor structures
                // We'd need to parse them fully and replace names
                Console.WriteLine($"Found complex descriptor block '{blockKey}' - specialized handling required");
            }
            
            // If we modified the block, write it back
            if (modified)
            {
                fs.Position = position;
                writer.Write(blockData);
                Console.WriteLine($"Updated names in block '{blockKey}'");
            }
        }
        
        /// <summary>
        /// Copy a block of data from reader to writer
        /// </summary>
        private static void CopyBlock(BinaryReader reader, BinaryWriter writer, uint length)
        {
            const int BUFFER_SIZE = 8192;
            byte[] buffer = new byte[BUFFER_SIZE];
            
            uint remaining = length;
            
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(BUFFER_SIZE, remaining);
                int read = reader.Read(buffer, 0, toRead);
                
                if (read > 0)
                {
                    writer.Write(buffer, 0, read);
                    remaining -= (uint)read;
                }
                else
                {
                    // No more data
                    break;
                }
            }
        }
        
        /// <summary>
        /// Copy all remaining data from reader to writer
        /// </summary>
        private static void CopyRemaining(BinaryReader reader, BinaryWriter writer)
        {
            const int BUFFER_SIZE = 8192;
            byte[] buffer = new byte[BUFFER_SIZE];
            
            int read;
            while ((read = reader.Read(buffer, 0, BUFFER_SIZE)) > 0)
            {
                writer.Write(buffer, 0, read);
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
        
        private static uint ReadUInt32BE(FileStream fs)
        {
            byte[] bytes = new byte[4];
            fs.Read(bytes, 0, 4);
            return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }
        
        private static int ReadInt32BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return (int)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }
        
        private static void WriteUInt16BE(BinaryWriter writer, ushort value)
        {
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }
        
        private static void WriteInt16BE(BinaryWriter writer, short value)
        {
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }
        
        private static void WriteUInt32BE(BinaryWriter writer, uint value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }
        
        private static void WriteInt32BE(BinaryWriter writer, int value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }
        #endregion
    }
}