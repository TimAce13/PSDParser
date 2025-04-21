using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PsdReaderApp.Core
{
    /// <summary>
    /// Class for handling text layer operations in PSD files
    /// </summary>
    public class PsdTextLayer
    {
        private const string PSD_SIGNATURE = "8BPS";
        private const string TEXT_LAYER_KEY = "TySh"; // Correct key for text layers
        private const string TEXT_ENGINE_DATA = "TxLr"; // Keep this as an alternative
        private const string TEXT_CONTENT_KEY = "Txt "; // Text content descriptor key

        // Дополнительные сигнатуры для идентификации текстовых слоев
        private static readonly string[] TEXT_LAYER_IDENTIFIERS = new string[] 
        {
            "TySh", // Primary text layer key
            "TxLr", // Text engine data
            "txtd", // Text descriptor
            "tdta"  // Text data
        };
        
        /// <summary>
        /// Represents a text layer found in a PSD file
        /// </summary>
        public class TextLayerInfo
        {
            public int LayerIndex { get; set; }
            public string LayerName { get; set; }
            public string Text { get; set; }
            public List<TextLocation> TextLocations { get; set; } = new List<TextLocation>();
            
            public override string ToString()
            {
                return $"Layer {LayerIndex}: '{LayerName}' with text: '{Text}'";
            }
        }
        
        /// <summary>
        /// Represents a location of text content in the PSD file
        /// </summary>
        public class TextLocation
        {
            public long Position { get; set; }
            public uint Size { get; set; }
            public string Format { get; set; } // "ASCII", "UTF16", or "UTF16-HEX"
            public string Key { get; set; } // The identifying block key
            
            public override string ToString()
            {
                return $"Text at position {Position}, size: {Size}, format: {Format}";
            }
        }
        
        /// <summary>
/// Выполнить полное сканирование файла для поиска всех экземпляров текста
/// </summary>
public static List<TextLocation> FullFileScanForText(string psdFilePath, string textToFind)
{
    List<TextLocation> textLocations = new List<TextLocation>();
    
    try
    {
        // Создаем несколько вариантов шаблона для поиска
        byte[] utf16Pattern = Encoding.BigEndianUnicode.GetBytes(textToFind);
        byte[] asciiPattern = Encoding.ASCII.GetBytes(textToFind);
        
        using (var fs = new FileStream(psdFilePath, FileMode.Open, FileAccess.Read))
        {
            byte[] fileData = new byte[fs.Length];
            fs.Read(fileData, 0, (int)fs.Length);
            
            Console.WriteLine($"Scanning entire file ({fileData.Length} bytes) for text: '{textToFind}'");
            
            // Ищем все экземпляры UTF-16 формата
            FindAllPatternOccurrences(fileData, utf16Pattern, "UTF16", textLocations);
            
            // Ищем все экземпляры ASCII формата
            FindAllPatternOccurrences(fileData, asciiPattern, "ASCII", textLocations);
            
            // Ищем текст в формате PDF/PostScript с экранированием
            FindPostScriptTextFormat(fileData, textToFind, textLocations);
            
            // Ищем специфические форматы PostScript
            FindPostScriptSpecificFormats(fileData, textToFind, textLocations);
            
            // Ищем все экземпляры с разной шириной символов
            for (int byteSpacing = 1; byteSpacing <= 4; byteSpacing++)
            {
                FindSpacedTextFormat(fileData, textToFind, byteSpacing, textLocations);
            }
            
            Console.WriteLine($"Found {textLocations.Count} occurrences of text '{textToFind}' in file");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during file scan: {ex.Message}");
    }
    
    return textLocations;
}

/// <summary>
/// Найти все вхождения шаблона в данных
/// </summary>
private static void FindAllPatternOccurrences(byte[] data, byte[] pattern, string format, 
                                            List<TextLocation> textLocations)
{
    if (pattern.Length == 0 || data.Length < pattern.Length)
        return;
        
    for (int i = 0; i <= data.Length - pattern.Length; i++)
    {
        bool match = true;
        for (int j = 0; j < pattern.Length; j++)
        {
            if (data[i + j] != pattern[j])
            {
                match = false;
                break;
            }
        }
        
        if (match)
        {
            long position = i;
            
            // Проверяем, что мы еще не добавили это местоположение
            if (!textLocations.Exists(loc => 
                loc.Position == position && loc.Format == format))
            {
                textLocations.Add(new TextLocation
                {
                    Position = position,
                    Size = (uint)pattern.Length,
                    Format = format,
                    Key = "SCAN"
                });
                
                Console.WriteLine($"Found {format} text at position 0x{position:X8}");
            }
        }
    }
}

/// <summary>
/// Найти текст в формате PDF/PostScript, где текст окружен скобками и с префиксом FE FF
/// </summary>
private static void FindPostScriptTextFormat(byte[] data, string text, List<TextLocation> textLocations)
{
    // Шаблон: ( FE FF 00 XX 00 YY 00 ZZ )
    string pattern = "/Text (";
    byte[] patternBytes = Encoding.ASCII.GetBytes(pattern);
    
    for (int i = 0; i <= data.Length - patternBytes.Length - 10; i++)
    {
        bool patternMatch = true;
        for (int j = 0; j < patternBytes.Length; j++)
        {
            if (data[i + j] != patternBytes[j])
            {
                patternMatch = false;
                break;
            }
        }
        
        if (patternMatch)
        {
            // Найден шаблон "/Text (", теперь проверяем наличие маркера UTF-16
            int pos = i + patternBytes.Length;
            if (pos + 2 < data.Length && data[pos] == 0xFE && data[pos + 1] == 0xFF)
            {
                pos += 2; // Пропускаем маркер UTF-16
                
                // Читаем текст в UTF-16 формате
                int startPos = pos;
                int textLength = 0;
                
                while (pos + 1 < data.Length && !(data[pos] == 0x29 && (data[pos+1] == 0x0A || data[pos+1] == 0x20)))
                {
                    pos += 2;
                    textLength += 2;
                }
                
                if (textLength > 0)
                {
                    byte[] textBytes = new byte[textLength];
                    Array.Copy(data, startPos, textBytes, 0, textLength);
                    
                    try
                    {
                        string foundText = Encoding.BigEndianUnicode.GetString(textBytes);
                        
                        Console.WriteLine($"Found PS text format at position 0x{startPos:X8}: '{foundText}'");
                        
                        if (foundText.Contains(text))
                        {
                            textLocations.Add(new TextLocation
                            {
                                Position = startPos,
                                Size = (uint)textLength,
                                Format = "PS-UTF16",
                                Key = "PSTEXT"
                            });
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибки декодирования
                    }
                }
            }
        }
    }
}

/// <summary>
/// Найти текст с различным интервалом между байтами
/// </summary>
private static void FindSpacedTextFormat(byte[] data, string text, int spacing, List<TextLocation> textLocations)
{
    if (string.IsNullOrEmpty(text) || data.Length < text.Length * (spacing + 1))
        return;
    
    string formatName = $"SPACED-{spacing}";
    
    for (int i = 0; i <= data.Length - text.Length * (spacing + 1); i++)
    {
        bool match = true;
        for (int j = 0; j < text.Length; j++)
        {
            int pos = i + j * (spacing + 1);
            
            if (pos >= data.Length || data[pos] != (byte)text[j])
            {
                match = false;
                break;
            }
            
            // Проверяем, что все промежуточные байты нулевые
            bool allZeros = true;
            for (int k = 1; k <= spacing; k++)
            {
                if (pos + k >= data.Length || data[pos + k] != 0)
                {
                    allZeros = false;
                    break;
                }
            }
            
            if (!allZeros)
            {
                match = false;
                break;
            }
        }
        
        if (match)
        {
            long position = i;
            
            // Проверяем, что мы еще не добавили это местоположение
            if (!textLocations.Exists(loc => 
                loc.Position == position && loc.Format == formatName))
            {
                textLocations.Add(new TextLocation
                {
                    Position = position,
                    Size = (uint)(text.Length * (spacing + 1)),
                    Format = formatName,
                    Key = "SPACED"
                });
                
                Console.WriteLine($"Found {formatName} text at position 0x{position:X8}");
            }
        }
    }
}
        
            /// <summary>
    /// Scan for text data in layer additional information
    /// </summary>
    private static TextLayerInfo ScanForTextData(BinaryReader reader, long extraDataEnd, int layerIndex, string layerName)
    {
        TextLayerInfo textInfo = null;
        long currentPos = reader.BaseStream.Position;
        
        try
        {
            // Сначала проверяем, является ли этот слой текстовым на основе флагов слоя
            // PSD text layers often have specific flags

            while (currentPos < extraDataEnd - 12) // Need at least 12 bytes for a signature, key, and size
            {
                reader.BaseStream.Position = currentPos;
                
                // Read signature
                string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                
                // Check if this is a valid signature ("8BIM" or "8B64")
                if (signature == "8BIM" || signature == "8B64")
                {
                    // Read key
                    string key = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    
                    // Get block size
                    uint blockSize = ReadUInt32BE(reader);
                    long blockStart = reader.BaseStream.Position;
                    long blockEnd = blockStart + blockSize;
                    
                    // If block size is too large or invalid, move forward and try again
                    if (blockSize > 1000000 || blockEnd > extraDataEnd)
                    {
                        currentPos += 4; // Move forward and try again
                        continue;
                    }
                    
                    // Check for text layer identifiers
                    if (Array.IndexOf(TEXT_LAYER_IDENTIFIERS, key) >= 0)
                    {
                        Console.WriteLine($"Found potential text layer marker: {key} at position {blockStart}, size: {blockSize}");
                        
                        // Initialize text layer info on first match
                        if (textInfo == null)
                        {
                            textInfo = new TextLayerInfo
                            {
                                LayerIndex = layerIndex,
                                LayerName = layerName
                            };
                        }
                        
                        // Scan through the data for text content
                        if (key == TEXT_LAYER_KEY) // Main text layer descriptor
                        {
                            ScanTextLayerDescriptor(reader, blockStart, blockEnd, textInfo);
                        }
                        else
                        {
                            // For other text block types, scan generically
                            ScanTextEngineData(reader, blockStart, blockEnd, textInfo);
                        }
                    }
                    
                    // Move to the end of this block
                    currentPos = blockEnd;
                    
                    // Ensure end is aligned to even boundary
                    if (currentPos % 2 != 0)
                        currentPos++;
                }
                else
                {
                    // Not a valid signature, move forward by 1 byte
                    currentPos++;
                }
            }
            
            // Если мы нашли текстовый слой, но не нашли текст, 
            // попробуем выполнить глубокое сканирование всего блока данных слоя
            if (textInfo != null && string.IsNullOrEmpty(textInfo.Text))
            {
                reader.BaseStream.Position = reader.BaseStream.Position; // Reset to the beginning of extra data
                byte[] allLayerData = reader.ReadBytes((int)(extraDataEnd - reader.BaseStream.Position));
                
                // Try to find Unicode strings in the layer data
                FindPossibleTextInData(allLayerData, textInfo);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning text data: {ex.Message}");
        }
        
        return textInfo;
    }

    /// <summary>
    /// Scan the text layer descriptor (TySh) for text content
    /// </summary>
    private static void ScanTextLayerDescriptor(BinaryReader reader, long blockStart, long blockEnd, TextLayerInfo textInfo)
    {
        // Store original position
        long originalPosition = reader.BaseStream.Position;
        
        try
        {
            // The TySh block contains a text descriptor with a specific structure
            // Move forward to find the text descriptor
            
            // First, there's a version number (short = 2 bytes)
            reader.BaseStream.Position = blockStart;
            short version = ReadInt16BE(reader);
            
            Console.WriteLine($"Text layer descriptor version: {version}");
            
            // There are transform values and other parameters before the text data
            // Skip to text data area, looking for patterns that might indicate text
            
            long currentPos = blockStart;
            while (currentPos < blockEnd - 12)
            {
                reader.BaseStream.Position = currentPos;
                
                try
                {
                    // Look for text indicator - might be TEXT, Txt, txtC, etc.
                    byte[] buffer = reader.ReadBytes(4);
                    string key = Encoding.ASCII.GetString(buffer);
                    
                    // Check for text content marker
                    if (key == "TEXT" || key == "Txt " || key == "txtC" || key == "tdta")
                    {
                        Console.WriteLine($"Found text indicator: {key} at position {currentPos}");
                        
                        // Try to read the text length
                        uint textLength = 0;
                        try
                        {
                            // Some text blocks have length as a 32-bit integer
                            textLength = ReadUInt32BE(reader);
                        }
                        catch
                        {
                            // If that fails, try alternate methods
                            // Move back to the key position
                            reader.BaseStream.Position = currentPos + 4;
                        }
                        
                        // If we found a valid text length
                        if (textLength > 0 && textLength < 10000 && reader.BaseStream.Position + textLength * 2 <= blockEnd)
                        {
                            long textPos = reader.BaseStream.Position;
                            
                            // Try to read as UTF-16
                            byte[] textBytes = reader.ReadBytes((int)textLength * 2);
                            string text = Encoding.BigEndianUnicode.GetString(textBytes);
                            
                            if (!string.IsNullOrWhiteSpace(text) && text.Length > 0)
                            {
                                Console.WriteLine($"Found text content: '{text}'");
                                textInfo.Text = text;
                                textInfo.TextLocations.Add(new TextLocation
                                {
                                    Position = textPos,
                                    Size = textLength * 2,
                                    Format = "UTF16",
                                    Key = key
                                });
                                
                                // Once we've found the text, break out
                                break;
                            }
                        }
                    }
                    
                    // Move forward byte by byte
                    currentPos++;
                }
                catch
                {
                    // On error, just move forward
                    currentPos++;
                }
            }
            
            // If we've found text, scan for copies of it
            if (!string.IsNullOrEmpty(textInfo.Text))
            {
                // Do a deep scan to find all instances
                PerformDeepTextScan(reader, blockStart, blockEnd, textInfo);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning text layer descriptor: {ex.Message}");
        }
        finally
        {
            // Restore original position
            reader.BaseStream.Position = originalPosition;
        }
    }

    /// <summary>
    /// Try to find any text-like data in the layer
    /// </summary>
    private static void FindPossibleTextInData(byte[] data, TextLayerInfo textInfo)
    {
        // Look for consecutive non-zero bytes that might be text
        List<int> textStarts = new List<int>();
        
        for (int i = 0; i < data.Length - 2; i++)
        {
            // Look for patterns that might indicate the start of text
            // For UTF-16, we'd expect a pattern like: char, 0, char, 0, ...
            if (i < data.Length - 20 && // Make sure we have enough data
                data[i] > 32 && data[i] < 127 && // Printable ASCII
                data[i+1] == 0 && // UTF-16 zero byte
                data[i+2] > 32 && data[i+2] < 127 && // Another printable ASCII
                data[i+3] == 0) // Another UTF-16 zero byte
            {
                textStarts.Add(i);
                i += 3; // Skip ahead to avoid finding the same text multiple times
            }
        }
        
        // Try to read text from potential starting points
        foreach (int start in textStarts)
        {
            try
            {
                // Find the end of the text (where we hit zeros or unprintable chars)
                int end = start;
                while (end < data.Length - 1 && 
                       ((data[end] >= 32 && data[end] < 127) || data[end] > 127) && // Allow extended ASCII
                       end - start < 1000) // Reasonable text length limit
                {
                    end += 2; // Skip to next UTF-16 char
                }
                
                int length = end - start;
                if (length > 4) // Minimum length for real text
                {
                    byte[] textBytes = new byte[length];
                    Array.Copy(data, start, textBytes, 0, length);
                    
                    // Try to interpret as UTF-16
                    string text = Encoding.BigEndianUnicode.GetString(textBytes);
                    
                    // If it looks like real text
                    if (!string.IsNullOrWhiteSpace(text) && 
                        text.Length > 2 && 
                        !textInfo.TextLocations.Exists(loc => loc.Position == start))
                    {
                        Console.WriteLine($"Found possible text content: '{text}'");
                        
                        if (string.IsNullOrEmpty(textInfo.Text))
                        {
                            textInfo.Text = text;
                        }
                        
                        textInfo.TextLocations.Add(new TextLocation
                        {
                            Position = start,
                            Size = (uint)length,
                            Format = "UTF16",
                            Key = "AUTO"
                        });
                    }
                }
            }
            catch
            {
                // Ignore errors in text parsing attempts
            }
        }
    }
    
    /// <summary>
            /// Расширенный поиск текста в формате PostScript с различными вариантами экранирования
            /// </summary>
            private static void FindExtendedPostScriptTextFormats(byte[] data, string text, List<TextLocation> textLocations)
            {
                // Преобразуем текст в UTF-16BE для поиска
                byte[] utf16Text = Encoding.BigEndianUnicode.GetBytes(text);
                
                // Различные шаблоны, которые могут предшествовать тексту в формате PDF/PostScript
                string[] possiblePrefixes = new string[] 
                {
                    "/Text (", 
                    "0 (", 
                    "/0 (", 
                    "/1 (", 
                    "/2 (", 
                    "/3 ("
                };
                
                // Для каждого возможного префикса
                foreach (string prefix in possiblePrefixes)
                {
                    byte[] prefixBytes = Encoding.ASCII.GetBytes(prefix);
                    
                    // Ищем все вхождения этого префикса
                    for (int i = 0; i <= data.Length - prefixBytes.Length - 8; i++)
                    {
                        bool prefixMatch = true;
                        for (int j = 0; j < prefixBytes.Length; j++)
                        {
                            if (data[i + j] != prefixBytes[j])
                            {
                                prefixMatch = false;
                                break;
                            }
                        }
                        
                        if (prefixMatch)
                        {
                            // Нашли потенциальный префикс, проверяем наличие маркера UTF-16 (FE FF)
                            int pos = i + prefixBytes.Length;
                            if (pos + 2 < data.Length && data[pos] == 0xFE && data[pos + 1] == 0xFF)
                            {
                                pos += 2; // Пропускаем маркер UTF-16
                                
                                // Проверяем, совпадает ли текст после маркера с искомым
                                bool textMatches = true;
                                for (int j = 0; j < utf16Text.Length; j++)
                                {
                                    if (pos + j >= data.Length || data[pos + j] != utf16Text[j])
                                    {
                                        textMatches = false;
                                        break;
                                    }
                                }
                                
                                if (textMatches)
                                {
                                    // Нашли текст в формате PDF/PostScript
                                    long position = pos;
                                    
                                    Console.WriteLine($"Found PDF/PS format text at position 0x{position:X8} with prefix '{prefix}'");
                                    
                                    // Добавляем это местоположение, если его еще нет
                                    if (!textLocations.Exists(loc => 
                                        loc.Position == position && loc.Format == "PDF-UTF16"))
                                    {
                                        textLocations.Add(new TextLocation
                                        {
                                            Position = position,
                                            Size = (uint)utf16Text.Length,
                                            Format = "PDF-UTF16",
                                            Key = "PS:" + prefix
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Также ищем дополнительные варианты с различными префиксами в двоичном формате
                // Например: 3C 3C 20 2F 30 20 28 FE FF ...
                byte[][] binaryPatterns = new byte[][] 
                {
                    new byte[] { 0x3C, 0x3C, 0x20, 0x2F, 0x30, 0x20, 0x28 },
                    new byte[] { 0x3C, 0x3C, 0x20, 0x2F, 0x31, 0x20, 0x28 },
                    new byte[] { 0x3C, 0x3C, 0x20, 0x2F, 0x32, 0x20, 0x28 },
                    new byte[] { 0x2F, 0x54, 0x65, 0x78, 0x74, 0x20, 0x28 },
                    new byte[] { 0x54, 0x65, 0x78, 0x74, 0x20, 0x28, 0xFE, 0xFF },
                    new byte[] { 0x30, 0x20, 0x28, 0xFE, 0xFF }, 
                };
                
                foreach (byte[] pattern in binaryPatterns)
                {
                    for (int i = 0; i <= data.Length - pattern.Length - 10; i++)
                    {
                        bool patternMatch = true;
                        for (int j = 0; j < pattern.Length; j++)
                        {
                            if (data[i + j] != pattern[j])
                            {
                                patternMatch = false;
                                break;
                            }
                        }
                        
                        if (patternMatch)
                        {
                            // Проверяем наличие маркера UTF-16
                            int pos = i + pattern.Length;
                            if (pos + 2 < data.Length && data[pos] == 0xFE && data[pos + 1] == 0xFF)
                            {
                                pos += 2; // Пропускаем маркер
                                
                                // Проверяем текст
                                bool textMatches = true;
                                for (int j = 0; j < utf16Text.Length; j++)
                                {
                                    if (pos + j >= data.Length || data[pos + j] != utf16Text[j])
                                    {
                                        textMatches = false;
                                        break;
                                    }
                                }
                                
                                if (textMatches)
                                {
                                    // Нашли текст в двоичном формате
                                    long position = pos;
                                    string patternHex = BitConverter.ToString(pattern).Replace("-", " ");
                                    
                                    Console.WriteLine($"Found binary pattern text at position 0x{position:X8} with pattern {patternHex}");
                                    
                                    if (!textLocations.Exists(loc => 
                                        loc.Position == position && loc.Format == "BIN-UTF16"))
                                    {
                                        textLocations.Add(new TextLocation
                                        {
                                            Position = position,
                                            Size = (uint)utf16Text.Length,
                                            Format = "BIN-UTF16",
                                            Key = "BIN:" + patternHex
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
    private static void FindPostScriptWithSpecialFormat(byte[] data, string text, List<TextLocation> textLocations)
{
    byte[] utf16Text = Encoding.BigEndianUnicode.GetBytes(text);
    
    // Ищем специфическую последовательность: "/Text (\xFE\xFF" + текст + дополнительные байты + ")"
    for (int i = 0; i < data.Length - 20; i++)
    {
        // Проверяем, начинается ли с "/Text ("
        if (i + 7 < data.Length && 
            data[i] == 0x2F && data[i+1] == 0x54 && data[i+2] == 0x65 && data[i+3] == 0x78 && 
            data[i+4] == 0x74 && data[i+5] == 0x20 && data[i+6] == 0x28)
        {
            // Проверяем наличие маркера UTF-16
            int pos = i + 7;
            if (pos + 2 < data.Length && data[pos] == 0xFE && data[pos+1] == 0xFF)
            {
                pos += 2; // Пропускаем маркер
                
                // Проверяем совпадение с UTF-16 текстом
                bool textMatch = true;
                for (int j = 0; j < utf16Text.Length; j++)
                {
                    if (pos + j >= data.Length || data[pos + j] != utf16Text[j])
                    {
                        textMatch = false;
                        break;
                    }
                }
                
                if (textMatch)
                {
                    // Найдено совпадение, ищем закрывающую скобку ")"
                    int endPos = pos + utf16Text.Length;
                    int closeParenPos = -1;
                    
                    // Ищем закрывающую скобку в пределах разумного расстояния
                    for (int j = 0; j < 20 && endPos + j < data.Length; j++)
                    {
                        if (data[endPos + j] == 0x29) // ')'
                        {
                            closeParenPos = endPos + j;
                            break;
                        }
                    }
                    
                    if (closeParenPos > 0)
                    {
                        Console.WriteLine($"Found special PostScript format at position 0x{pos:X8} to 0x{closeParenPos:X8}");
                        
                        // Добавляем это местоположение
                        textLocations.Add(new TextLocation
                        {
                            Position = pos,
                            Size = (uint)(closeParenPos - pos),
                            Format = "PS-SPECIAL",
                            Key = "PS-TEXTBLOCK"
                        });
                    }
                }
            }
        }
        
        // Также проверяем шаблон "/0 (" или "0 ("
        if (i + 4 < data.Length && 
            ((data[i] == 0x2F && data[i+1] == 0x30 && data[i+2] == 0x20 && data[i+3] == 0x28) ||
             (data[i] == 0x30 && data[i+1] == 0x20 && data[i+2] == 0x28)))
        {
            int parenthesisPos = data[i] == 0x2F ? i + 3 : i + 2;
            
            // Проверяем наличие маркера UTF-16
            int pos = parenthesisPos + 1;
            if (pos + 2 < data.Length && data[pos] == 0xFE && data[pos+1] == 0xFF)
            {
                pos += 2; // Пропускаем маркер
                
                // Проверяем совпадение с UTF-16 текстом
                bool textMatch = true;
                for (int j = 0; j < utf16Text.Length; j++)
                {
                    if (pos + j >= data.Length || data[pos + j] != utf16Text[j])
                    {
                        textMatch = false;
                        break;
                    }
                }
                
                if (textMatch)
                {
                    // Найдено совпадение, ищем закрывающую скобку ")"
                    int endPos = pos + utf16Text.Length;
                    int closeParenPos = -1;
                    
                    // Ищем закрывающую скобку в пределах разумного расстояния
                    for (int j = 0; j < 20 && endPos + j < data.Length; j++)
                    {
                        if (data[endPos + j] == 0x29) // ')'
                        {
                            closeParenPos = endPos + j;
                            break;
                        }
                    }
                    
                    if (closeParenPos > 0)
                    {
                        Console.WriteLine($"Found special PostScript format at position 0x{pos:X8} to 0x{closeParenPos:X8}");
                        
                        // Добавляем это местоположение
                        textLocations.Add(new TextLocation
                        {
                            Position = pos,
                            Size = (uint)(closeParenPos - pos),
                            Format = "PS-SPECIAL",
                            Key = "PS-NUMBLOCK"
                        });
                    }
                }
            }
        }
    }
}
private static void UpdateSpecialPostScriptFormat(FileStream fs, BinaryWriter writer, TextLocation location, 
    string oldText, string newText)
{
    try
    {
        // Для этого специального формата нужно быть осторожным с длиной
        // Если длина разная, то нужно учесть возможные дополнительные байты
        
        fs.Position = location.Position;
        
        // Чтение оригинального содержимого
        byte[] originalData = new byte[location.Size];
        fs.Position = location.Position;
        fs.Read(originalData, 0, (int)location.Size);
        
        // Новый текст в UTF-16BE
        byte[] newTextBytes = Encoding.BigEndianUnicode.GetBytes(newText);
        
        // Определяем, есть ли дополнительные байты после текста
        int additionalBytes = (int)location.Size - Encoding.BigEndianUnicode.GetBytes(oldText).Length;
        
        if (additionalBytes >= 0)
        {
            // Записываем новый текст
            writer.Write(newTextBytes);
            
            // Копируем дополнительные байты (если они есть)
            if (additionalBytes > 0)
            {
                byte[] extraBytes = new byte[additionalBytes];
                Buffer.BlockCopy(originalData, originalData.Length - additionalBytes, extraBytes, 0, additionalBytes);
                writer.Write(extraBytes);
            }
            
            Console.WriteLine($"Updated special PostScript format at position 0x{location.Position:X8}");
        }
        else
        {
            Console.WriteLine($"WARNING: Cannot update special PostScript format at position 0x{location.Position:X8} - size issue");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error updating special PostScript format at position 0x{location.Position:X8}: {ex.Message}");
    }
}
            /// <summary>
            /// Обновляет текст в формате PDF/PostScript или Бинарном UTF16 формате
            /// </summary>
            private static void UpdateExtendedFormatText(FileStream fs, BinaryWriter writer, TextLocation location, 
                string oldText, string newText)
            {
                try
                {
                    // Проверка на соответствие длин строк
                    if (oldText.Length != newText.Length)
                    {
                        Console.WriteLine($"WARNING: Cannot update text at position {location.Position} - length mismatch (old: {oldText.Length}, new: {newText.Length})");
                        return;
                    }
        
                    // Позиционируемся на точное место в файле
                    fs.Position = location.Position;
        
                    // Записываем новый текст в UTF-16BE формате
                    byte[] newBytes = Encoding.BigEndianUnicode.GetBytes(newText);
                    writer.Write(newBytes);
        
                    Console.WriteLine($"Updated {location.Format} text at position 0x{location.Position:X8}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating extended format text at position {location.Position}: {ex.Message}");
                }
            }
        /// <summary>
        /// Finds all text layers in a PSD file
        /// </summary>
        /// <param name="psdFilePath">Path to PSD file</param>
        /// <returns>List of text layer information</returns>
        public static List<TextLayerInfo> FindTextLayers(string psdFilePath)
        {
            var textLayers = new List<TextLayerInfo>();
            
            try
            {
                using (var fs = new FileStream(psdFilePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    // Verify PSD signature
                    string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (signature != PSD_SIGNATURE)
                    {
                        throw new Exception("Not a valid PSD file");
                    }
                    
                    // Skip header and mode data
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
                    uint layerInfoSize = ReadUInt32BE(reader);
                    
                    // Read layer count
                    short layerCount = ReadInt16BE(reader);
                    int absLayerCount = Math.Abs(layerCount);
                    
                    Console.WriteLine($"Found {absLayerCount} layers in file");
                    
                    // Process each layer
                    for (int i = 0; i < absLayerCount; i++)
                    {
                        long layerStart = fs.Position;
                        
                        // Read layer bounds
                        int top = ReadInt32BE(reader);
                        int left = ReadInt32BE(reader);
                        int bottom = ReadInt32BE(reader);
                        int right = ReadInt32BE(reader);
                        
                        // Read channel info
                        ushort channelCount = ReadUInt16BE(reader);
                        fs.Position += channelCount * 6; // Skip channel info (each 6 bytes)
                        
                        // Blend mode signature and key
                        string blendSignature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                        string blendKey = Encoding.ASCII.GetString(reader.ReadBytes(4));
                        
                        // Layer properties
                        byte opacity = reader.ReadByte();
                        byte clipping = reader.ReadByte();
                        byte flags = reader.ReadByte();
                        byte filler = reader.ReadByte();
                        
                        // Extra data section
                        uint extraDataLength = ReadUInt32BE(reader);
                        long extraDataStart = fs.Position;
                        long extraDataEnd = extraDataStart + extraDataLength;
                        
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
                        
                        // Calculate name padding
                        int totalNameLength = nameLength + 1; // +1 for length byte
                        int paddedNameLength = ((totalNameLength + 3) / 4) * 4; // Round up to multiple of 4
                        
                        // Skip to after name padding
                        fs.Position = extraDataStart + 8 + maskDataLength + blendingRangesLength + paddedNameLength;
                        
                        // Check for additional layer information blocks to find text
                        var textLayerInfo = ScanForTextData(reader, extraDataEnd, i, layerName);
                        
                        if (textLayerInfo != null)
                        {
                            textLayers.Add(textLayerInfo);
                            Console.WriteLine($"Found text layer: {textLayerInfo}");
                        }
                        
                        // Jump to end of this layer's extra data
                        fs.Position = extraDataEnd;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning for text layers: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            
            return textLayers;
        }
        
        //// <summary>
        /// Scan text engine data for text content
        /// </summary>
        private static void ScanTextEngineData(BinaryReader reader, long blockStart, long blockEnd, TextLayerInfo textInfo)
        {
            long currentPos = blockStart;
            
            // First try to find the descriptor text
            while (currentPos < blockEnd - 12)
            {
                reader.BaseStream.Position = currentPos;
                
                try
                {
                    // Look for "Txt " key which indicates text content
                    byte[] buffer = reader.ReadBytes(4);
                    string key = Encoding.ASCII.GetString(buffer);
                    
                    if (key == TEXT_CONTENT_KEY)
                    {
                        // Read text descriptor
                        string textType = Encoding.ASCII.GetString(reader.ReadBytes(4));
                        
                        if (textType == "TEXT")
                        {
                            // This is a text descriptor
                            uint textLength = ReadUInt32BE(reader);
                            long textPos = reader.BaseStream.Position;
                            
                            // Read the text
                            if (textLength > 0 && textPos + textLength * 2 <= blockEnd)
                            {
                                byte[] textBytes = reader.ReadBytes((int)textLength * 2); // UTF-16 (2 bytes per char)
                                string text = Encoding.BigEndianUnicode.GetString(textBytes);
                                
                                // Add to text info
                                textInfo.Text = text;
                                textInfo.TextLocations.Add(new TextLocation
                                {
                                    Position = textPos,
                                    Size = textLength * 2,
                                    Format = "UTF16",
                                    Key = TEXT_CONTENT_KEY
                                });
                                
                                Console.WriteLine($"Found text: '{text}'");
                            }
                        }
                    }
                    
                    // Move forward
                    currentPos++;
                }
                catch
                {
                    // On error, just move forward
                    currentPos++;
                }
            }
            
            // Now perform a deep scan for all copies of the text
            if (textInfo.Text != null && !string.IsNullOrEmpty(textInfo.Text))
            {
                PerformDeepTextScan(reader, blockStart, blockEnd, textInfo);
            }
        }
        
        /// <summary>
        /// Scan for additional copies of the text in the block
        /// </summary>
        private static void ScanForAdditionalTextCopies(BinaryReader reader, long blockStart, long blockEnd, TextLayerInfo textInfo)
        {
            string textToFind = textInfo.Text;
            
            // UTF-16 copy (2 bytes per char with null bytes between)
            byte[] utf16Pattern = Encoding.BigEndianUnicode.GetBytes(textToFind);
            long currentPos = blockStart;
            
            while (currentPos < blockEnd - utf16Pattern.Length)
            {
                bool found = true;
                reader.BaseStream.Position = currentPos;
                
                // Check if the UTF-16 pattern matches at this position
                for (int i = 0; i < utf16Pattern.Length; i++)
                {
                    if (reader.BaseStream.Position >= blockEnd)
                    {
                        found = false;
                        break;
                    }
                    
                    byte b = reader.ReadByte();
                    if (b != utf16Pattern[i])
                    {
                        found = false;
                        break;
                    }
                }
                
                if (found && !textInfo.TextLocations.Exists(loc => loc.Position == currentPos))
                {
                    Console.WriteLine($"Found UTF-16 text copy at position {currentPos}");
                    textInfo.TextLocations.Add(new TextLocation
                    {
                        Position = currentPos,
                        Size = (uint)utf16Pattern.Length,
                        Format = "UTF16",
                        Key = "COPY"
                    });
                }
                
                currentPos++;
            }
            
            // UTF-16-HEX copy (each char as 4 hex bytes)
            byte[] asciiBytes = Encoding.ASCII.GetBytes(textToFind);
            currentPos = blockStart;
            
            while (currentPos < blockEnd - asciiBytes.Length)
            {
                bool found = true;
                reader.BaseStream.Position = currentPos;
                
                // Check if ASCII pattern matches at this position
                for (int i = 0; i < asciiBytes.Length; i++)
                {
                    if (reader.BaseStream.Position >= blockEnd)
                    {
                        found = false;
                        break;
                    }
                    
                    byte b = reader.ReadByte();
                    if (b != asciiBytes[i])
                    {
                        found = false;
                        break;
                    }
                }
                
                if (found && !textInfo.TextLocations.Exists(loc => loc.Position == currentPos))
                {
                    Console.WriteLine($"Found ASCII text copy at position {currentPos}");
                    textInfo.TextLocations.Add(new TextLocation
                    {
                        Position = currentPos,
                        Size = (uint)asciiBytes.Length,
                        Format = "ASCII",
                        Key = "COPY"
                    });
                }
                
                currentPos++;
            }
            
            // Attempt to find hex version of text (more complex pattern)
            currentPos = blockStart;
            
            while (currentPos < blockEnd - textToFind.Length * 4)
            {
                bool found = true;
                reader.BaseStream.Position = currentPos;
                
                // Check for hex pattern (each character followed by 00 00 00)
                for (int i = 0; i < textToFind.Length; i++)
                {
                    if (reader.BaseStream.Position >= blockEnd - 4)
                    {
                        found = false;
                        break;
                    }
                    
                    byte charByte = reader.ReadByte();
                    byte zeroByte1 = reader.ReadByte();
                    byte zeroByte2 = reader.ReadByte();
                    byte zeroByte3 = reader.ReadByte();
                    
                    char c = textToFind[i];
                    if (charByte != (byte)c || zeroByte1 != 0 || zeroByte2 != 0 || zeroByte3 != 0)
                    {
                        found = false;
                        break;
                    }
                }
                
                if (found && !textInfo.TextLocations.Exists(loc => loc.Position == currentPos))
                {
                    Console.WriteLine($"Found UTF16-HEX text copy at position {currentPos}");
                    textInfo.TextLocations.Add(new TextLocation
                    {
                        Position = currentPos,
                        Size = (uint)(textToFind.Length * 4),
                        Format = "UTF16-HEX",
                        Key = "COPY"
                    });
                }
                
                currentPos++;
            }
        }
        /// <summary>
        /// More thorough scan to find all instances of text in a text layer
        /// </summary>
        private static void PerformDeepTextScan(BinaryReader reader, long blockStart, long blockEnd, TextLayerInfo textInfo)
        {
            // Skip if no text was found in the basic scan
            if (string.IsNullOrEmpty(textInfo.Text))
                return;
                
            // Store the original position
            long originalPosition = reader.BaseStream.Position;
            
            try
            {
                // Read the entire block into a buffer for faster scanning
                reader.BaseStream.Position = blockStart;
                byte[] blockData = reader.ReadBytes((int)(blockEnd - blockStart));
                
                // Convert text to various formats for scanning
                string searchText = textInfo.Text;
                
                // 1. UTF-16BE format (normal Unicode text)
                byte[] utf16Bytes = Encoding.BigEndianUnicode.GetBytes(searchText);
                
                // 2. ASCII format (if text only contains ASCII chars)
                byte[] asciiBytes = Encoding.ASCII.GetBytes(searchText);
                
                // 3. UTF-16 with zero padding (4 bytes per char: char byte, 0, 0, 0)
                byte[] paddedHexBytes = new byte[searchText.Length * 4];
                for (int i = 0; i < searchText.Length; i++)
                {
                    paddedHexBytes[i * 4] = (byte)searchText[i];
                    // Other bytes are already 0
                }
                
                // 4. ASCII with zero padding (2 bytes per char: char byte, 0)
                byte[] paddedAsciiBytes = new byte[searchText.Length * 2];
                for (int i = 0; i < searchText.Length; i++)
                {
                    paddedAsciiBytes[i * 2] = (byte)searchText[i];
                    // Other bytes are already 0
                }
                
                // Find all occurrences of the text in different formats
                FindAllTextOccurrences(blockData, utf16Bytes, "UTF16", textInfo, blockStart);
                FindAllTextOccurrences(blockData, asciiBytes, "ASCII", textInfo, blockStart);
                FindAllTextOccurrences(blockData, paddedHexBytes, "UTF16-HEX", textInfo, blockStart);
                FindAllTextOccurrences(blockData, paddedAsciiBytes, "ASCII-HEX", textInfo, blockStart);
                
                // Also look for the text with each character as separate 4-byte entities
                FindHexTextWithSpacing(blockData, searchText, textInfo, blockStart);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in deep text scan: {ex.Message}");
            }
            finally
            {
                // Restore original position
                reader.BaseStream.Position = originalPosition;
            }
        }

        /// <summary>
        /// Find all occurrences of a byte pattern in a data buffer
        /// </summary>
        private static void FindAllTextOccurrences(byte[] data, byte[] pattern, string format, TextLayerInfo textInfo, long blockStart)
        {
            if (pattern.Length == 0 || data.Length < pattern.Length)
                return;
                
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                
                if (match)
                {
                    long position = blockStart + i;
                    
                    // Check if we already have this location (avoid duplicates)
                    if (!textInfo.TextLocations.Exists(loc => 
                        loc.Position == position && loc.Format == format))
                    {
                        textInfo.TextLocations.Add(new TextLocation
                        {
                            Position = position,
                            Size = (uint)pattern.Length,
                            Format = format,
                            Key = "SCAN"
                        });
                        
                        Console.WriteLine($"Deep scan: Found {format} text at position {position}");
                    }
                }
            }
        }

        /// <summary>
        /// Look for text with complex hex spacing patterns
        /// </summary>
        private static void FindHexTextWithSpacing(byte[] data, string text, TextLayerInfo textInfo, long blockStart)
        {
            // Skip if text is empty or data is too small
            if (string.IsNullOrEmpty(text) || data.Length < text.Length * 4)
                return;
                
            // Try to find patterns with variable spacing
            for (int spacing = 1; spacing <= 3; spacing++)
            {
                int byteSize = text.Length * (1 + spacing);
                
                for (int i = 0; i <= data.Length - byteSize; i++)
                {
                    bool match = true;
                    for (int j = 0; j < text.Length; j++)
                    {
                        // Check if the character matches and is followed by 'spacing' zero bytes
                        int pos = i + j * (1 + spacing);
                        
                        if (pos >= data.Length || data[pos] != (byte)text[j])
                        {
                            match = false;
                            break;
                        }
                        
                        // Check for zero padding
                        for (int k = 1; k <= spacing; k++)
                        {
                            if (pos + k >= data.Length || data[pos + k] != 0)
                            {
                                match = false;
                                break;
                            }
                        }
                        
                        if (!match)
                            break;
                    }
                    
                    if (match)
                    {
                        long position = blockStart + i;
                        string formatName = $"HEX-{1+spacing}";
                        
                        // Check if we already have this location
                        if (!textInfo.TextLocations.Exists(loc => 
                            loc.Position == position && loc.Format == formatName))
                        {
                            textInfo.TextLocations.Add(new TextLocation
                            {
                                Position = position,
                                Size = (uint)(text.Length * (1 + spacing)),
                                Format = formatName,
                                Key = "SCAN"
                            });
                            
                            Console.WriteLine($"Deep scan: Found {formatName} text at position {position}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Update text with variable formatting
        /// </summary>
        private static void UpdateVariableFormattedText(FileStream fs, BinaryWriter writer, TextLocation location, string oldText, string newText)
        {
            try
            {
                fs.Position = location.Position;
                
                if (location.Format == "UTF16")
                {
                    // Standard UTF-16 encoding
                    byte[] newBytes = Encoding.BigEndianUnicode.GetBytes(newText);
                    
                    // Check if new text fits in the allocated space
                    if (newBytes.Length <= location.Size)
                    {
                        writer.Write(newBytes);
                        
                        // Zero fill any remaining space
                        int remaining = (int)(location.Size - newBytes.Length);
                        for (int i = 0; i < remaining; i++)
                        {
                            writer.Write((byte)0);
                        }
                        
                        Console.WriteLine($"Updated UTF16 text at position {location.Position}");
                    }
                    else
                    {
                        Console.WriteLine($"WARNING: Cannot update UTF16 text at position {location.Position} - new text too long");
                    }
                }
                else if (location.Format == "ASCII")
                {
                    // Standard ASCII encoding
                    byte[] newBytes = Encoding.ASCII.GetBytes(newText);
                    
                    if (newBytes.Length <= location.Size)
                    {
                        writer.Write(newBytes);
                        
                        // Zero fill any remaining space
                        int remaining = (int)(location.Size - newBytes.Length);
                        for (int i = 0; i < remaining; i++)
                        {
                            writer.Write((byte)0);
                        }
                        
                        Console.WriteLine($"Updated ASCII text at position {location.Position}");
                    }
                    else
                    {
                        Console.WriteLine($"WARNING: Cannot update ASCII text at position {location.Position} - new text too long");
                    }
                }
                else if (location.Format == "UTF16-HEX")
                {
                    // Format with 4 bytes per character: char, 0, 0, 0
                    if (newText.Length <= oldText.Length)
                    {
                        for (int i = 0; i < newText.Length; i++)
                        {
                            writer.Write((byte)newText[i]);
                            writer.Write((byte)0);
                            writer.Write((byte)0);
                            writer.Write((byte)0);
                        }
                        
                        // Fill any remaining space with zeros
                        int remainingChars = oldText.Length - newText.Length;
                        for (int i = 0; i < remainingChars * 4; i++)
                        {
                            writer.Write((byte)0);
                        }
                        
                        Console.WriteLine($"Updated UTF16-HEX text at position {location.Position}");
                    }
                    else
                    {
                        Console.WriteLine($"WARNING: Cannot update UTF16-HEX text at position {location.Position} - new text too long");
                    }
                }
                else if (location.Format == "ASCII-HEX")
                {
                    // Format with 2 bytes per character: char, 0
                    if (newText.Length <= oldText.Length)
                    {
                        for (int i = 0; i < newText.Length; i++)
                        {
                            writer.Write((byte)newText[i]);
                            writer.Write((byte)0);
                        }
                        
                        // Fill any remaining space with zeros
                        int remainingChars = oldText.Length - newText.Length;
                        for (int i = 0; i < remainingChars * 2; i++)
                        {
                            writer.Write((byte)0);
                        }
                        
                        Console.WriteLine($"Updated ASCII-HEX text at position {location.Position}");
                    }
                    else
                    {
                        Console.WriteLine($"WARNING: Cannot update ASCII-HEX text at position {location.Position} - new text too long");
                    }
                }
                else if (location.Format.StartsWith("HEX-"))
                {
                    // Variable spacing format (HEX-2, HEX-3, HEX-4)
                    string[] parts = location.Format.Split('-');
                    if (parts.Length == 2 && int.TryParse(parts[1], out int bytesPerChar))
                    {
                        if (newText.Length <= oldText.Length)
                        {
                            for (int i = 0; i < newText.Length; i++)
                            {
                                writer.Write((byte)newText[i]);
                                
                                // Write zeroes for padding
                                for (int j = 1; j < bytesPerChar; j++)
                                {
                                    writer.Write((byte)0);
                                }
                            }
                            
                            // Fill any remaining space with zeros
                            int remainingChars = oldText.Length - newText.Length;
                            for (int i = 0; i < remainingChars * bytesPerChar; i++)
                            {
                                writer.Write((byte)0);
                            }
                            
                            Console.WriteLine($"Updated {location.Format} text at position {location.Position}");
                        }
                        else
                        {
                            Console.WriteLine($"WARNING: Cannot update {location.Format} text at position {location.Position} - new text too long");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"WARNING: Invalid format {location.Format} at position {location.Position}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating text at position {location.Position}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Найти и заменить текст в формате PostScript/PDF
        /// </summary>
        private static void UpdatePostScriptText(FileStream fs, BinaryWriter writer, TextLocation location, 
            string oldText, string newText)
        {
            try
            {
                // Проверяем, что шаблоны одной длины для простой замены
                if (oldText.Length != newText.Length)
                {
                    Console.WriteLine($"WARNING: Cannot update PS text at position {location.Position} - text length mismatch");
                    return;
                }
        
                fs.Position = location.Position;
        
                if (location.Format == "PS-UTF16")
                {
                    // Формат: UTF-16 с маркером BOM
                    byte[] newTextBytes = Encoding.BigEndianUnicode.GetBytes(newText);
                    writer.Write(newTextBytes);
                    Console.WriteLine($"Updated PS-UTF16 text at position {location.Position}");
                }
                else
                {
                    Console.WriteLine($"WARNING: Unknown PS format {location.Format} at position {location.Position}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating PS text at position {location.Position}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Специальный метод для обновления текста в формате PostScript с конкретной структурой
        /// </summary>
        private static void UpdatePostScriptSpecificFormat(FileStream fs, BinaryWriter writer, TextLocation location, 
            string oldText, string newText)
        {
            try
            {
                // Проверка на равенство длин, так как старые и новые значения должны быть одной длины
                // чтобы сохранить структуру файла
                if (oldText.Length != newText.Length)
                {
                    Console.WriteLine($"WARNING: Cannot update PostScript format at position 0x{location.Position:X8} - text length must match");
                    return;
                }
        
                // Позиционируемся на точное место в файле
                fs.Position = location.Position;
        
                // Записываем новый текст в UTF-16BE формате
                byte[] newBytes = Encoding.BigEndianUnicode.GetBytes(newText);
                writer.Write(newBytes);
        
                // Явно сбрасываем буфер на диск
                writer.Flush();
        
                Console.WriteLine($"Updated {location.Format} text at position 0x{location.Position:X8}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating PostScript format at position 0x{location.Position:X8}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Специальный метод для поиска текста в формате PostScript с конкретной структурой
        /// </summary>
        private static void FindPostScriptSpecificFormats(byte[] data, string text, List<TextLocation> textLocations)
        {
            // Конкретный шаблон, который мы ищем: "/Text (\xFE\xFF" + текст + "\x0D)"
            byte[] searchPattern = new byte[] 
            {
                0x2F, 0x54, 0x65, 0x78, 0x74, 0x20, 0x28, 0xFE, 0xFF // "/Text (\xFE\xFF"
            };
            
            byte[] utf16Text = Encoding.BigEndianUnicode.GetBytes(text);
            
            // Ищем все вхождения шаблона
            for (int i = 0; i <= data.Length - searchPattern.Length - utf16Text.Length - 3; i++)
            {
                // Проверяем совпадение с шаблоном
                bool patternMatch = true;
                for (int j = 0; j < searchPattern.Length; j++)
                {
                    if (data[i + j] != searchPattern[j])
                    {
                        patternMatch = false;
                        break;
                    }
                }
                
                if (patternMatch)
                {
                    // Шаблон найден, проверяем текст после него
                    int textPos = i + searchPattern.Length;
                    
                    // Проверяем совпадение с UTF-16 текстом
                    bool textMatch = true;
                    for (int j = 0; j < utf16Text.Length; j++)
                    {
                        if (textPos + j >= data.Length || data[textPos + j] != utf16Text[j])
                        {
                            textMatch = false;
                            break;
                        }
                    }
                    
                    if (textMatch)
                    {
                        // Проверяем, есть ли после текста последовательность "\x0D)" (0x0D, 0x29)
                        int endPos = textPos + utf16Text.Length;
                        if (endPos + 2 <= data.Length && data[endPos] == 0x0D && data[endPos + 1] == 0x29)
                        {
                            Console.WriteLine($"Found exact PostScript format at position 0x{textPos:X8} with text '{text}' and ending with 0x0D 0x29");
                            
                            // Добавляем найденную локацию с уникальным ключом
                            textLocations.Add(new TextLocation
                            {
                                Position = textPos,
                                Size = (uint)utf16Text.Length,
                                Format = "PS-TEXT-EOL",
                                Key = "PS-SPECIFIC"
                            });
                        }
                    }
                }
            }
            
            // Шаблон для поиска: "0 (\xFE\xFF" + текст + "\x0D)"
            byte[] searchPattern2 = new byte[] 
            {
                0x30, 0x20, 0x28, 0xFE, 0xFF // "0 (\xFE\xFF"
            };
            
            // Ищем все вхождения второго шаблона
            for (int i = 0; i <= data.Length - searchPattern2.Length - utf16Text.Length - 3; i++)
            {
                // Проверяем совпадение с шаблоном
                bool patternMatch = true;
                for (int j = 0; j < searchPattern2.Length; j++)
                {
                    if (data[i + j] != searchPattern2[j])
                    {
                        patternMatch = false;
                        break;
                    }
                }
                
                if (patternMatch)
                {
                    // Шаблон найден, проверяем текст после него
                    int textPos = i + searchPattern2.Length;
                    
                    // Проверяем совпадение с UTF-16 текстом
                    bool textMatch = true;
                    for (int j = 0; j < utf16Text.Length; j++)
                    {
                        if (textPos + j >= data.Length || data[textPos + j] != utf16Text[j])
                        {
                            textMatch = false;
                            break;
                        }
                    }
                    
                    if (textMatch)
                    {
                        // Проверяем, есть ли после текста последовательность "\x0D)" (0x0D, 0x29)
                        int endPos = textPos + utf16Text.Length;
                        if (endPos + 2 <= data.Length && data[endPos] == 0x0D && data[endPos + 1] == 0x29)
                        {
                            Console.WriteLine($"Found exact PostScript format at position 0x{textPos:X8} with text '{text}' and ending with 0x0D 0x29");
                            
                            // Добавляем найденную локацию с уникальным ключом
                            textLocations.Add(new TextLocation
                            {
                                Position = textPos,
                                Size = (uint)utf16Text.Length,
                                Format = "PS-NUMBER-EOL",
                                Key = "PS-SPECIFIC"
                            });
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Modify text in all text layers that match the search text
        /// </summary>
        public static bool ModifyTextLayers(string inputPath, string outputPath, string searchText, string replaceText)
        {
            try
            {
                // Сначала находим все текстовые слои обычным методом
                List<TextLayerInfo> textLayers = FindTextLayers(inputPath);
                
                if (textLayers.Count == 0)
                {
                    Console.WriteLine("No text layers found using standard method, trying deep scan...");
                    
                    // Если не найдено стандартным способом, создаем искусственный слой и сканируем весь файл
                    var artificialLayer = new TextLayerInfo
                    {
                        LayerIndex = -1,
                        LayerName = "Auto-detected text layer",
                        Text = searchText
                    };
                    
                    // Выполняем полное сканирование файла
                    List<TextLocation> allTextLocations = FullFileScanForText(inputPath, searchText);
                    
                    if (allTextLocations.Count > 0)
                    {
                        artificialLayer.TextLocations = allTextLocations;
                        textLayers.Add(artificialLayer);
                        Console.WriteLine($"Deep scan found {allTextLocations.Count} occurrences of text '{searchText}'");
                    }
                }
                
                if (textLayers.Count == 0)
                {
                    Console.WriteLine("No text containing the search text found in the PSD file.");
                    return false;
                }
                
                // Фильтруем слои, содержащие искомый текст
                var matchingLayers = textLayers.FindAll(layer => layer.Text != null && layer.Text.Contains(searchText));
                
                if (matchingLayers.Count == 0)
                {
                    Console.WriteLine($"No layers found containing text: '{searchText}'");
                    return false;
                }
                
                Console.WriteLine($"Found {matchingLayers.Count} layers with text containing '{searchText}':");
                foreach (var layer in matchingLayers)
                {
                    Console.WriteLine($"- Layer {layer.LayerIndex}: '{layer.LayerName}' with text: '{layer.Text}'");
                    Console.WriteLine($"  Text locations found: {layer.TextLocations.Count}");
                }
                
                // Копируем входной файл в выходной
                File.Copy(inputPath, outputPath, true);
                
                // Открываем выходной файл для модификации
                using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite))
                using (var writer = new BinaryWriter(fs))
                {
                    // Обрабатываем каждый соответствующий слой
                    foreach (var layer in matchingLayers)
                    {
                        string newText = layer.Text.Replace(searchText, replaceText);
                        Console.WriteLine($"Replacing text in layer {layer.LayerIndex} from '{layer.Text}' to '{newText}'");
                        
                        // Сортируем местоположения текста по позиции, чтобы избежать проблем с перекрытием
                        layer.TextLocations.Sort((a, b) => a.Position.CompareTo(b.Position));
                        
                        // Обновляем каждое местоположение текста
                        foreach (var location in layer.TextLocations)
                        {
                            // Выбираем метод обновления в зависимости от формата
                            if (location.Format == "PS-UTF16")
                            {
                                UpdatePostScriptText(fs, writer, location, layer.Text, newText);
                            }
                            else if (location.Format.StartsWith("SPACED-"))
                            {
                                // Для формата с пробелами между символами
                                string[] parts = location.Format.Split('-');
                                if (parts.Length == 2 && int.TryParse(parts[1], out int spacing))
                                {
                                    UpdateSpacedText(fs, writer, location, layer.Text, newText, spacing);
                                }
                                else
                                {
                                    Console.WriteLine($"WARNING: Invalid format {location.Format} at position {location.Position}");
                                }
                            }
                            else if (location.Format == "PDF-UTF16" || location.Format == "BIN-UTF16")
                            {
                                UpdateExtendedFormatText(fs, writer, location, layer.Text, newText);
                            }
                            // В цикле обновления текста добавляем новое условие:
                            else if (location.Format == "PS-SPECIAL")
                            {
                                UpdateSpecialPostScriptFormat(fs, writer, location, layer.Text, newText);
                            }
                            // В цикле обновления текста добавляем новое условие:
                            else if (location.Format == "PS-TEXT-EOL" || location.Format == "PS-NUMBER-EOL")
                            {
                                UpdatePostScriptSpecificFormat(fs, writer, location, layer.Text, newText);
                            }
                            else
                            {
                                // Для стандартных форматов
                                UpdateVariableFormattedText(fs, writer, location, layer.Text, newText);
                            }
                        }
                    }
                }
                
                Console.WriteLine($"Text layer modifications complete. File saved to {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error modifying text layers: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }
       

        /// <summary>
        /// Обновить текст с заданным интервалом между символами
        /// </summary>
        private static void UpdateSpacedText(FileStream fs, BinaryWriter writer, TextLocation location, 
                                            string oldText, string newText, int spacing)
        {
            try
            {
                fs.Position = location.Position;
                
                if (newText.Length <= oldText.Length)
                {
                    for (int i = 0; i < newText.Length; i++)
                    {
                        writer.Write((byte)newText[i]);
                        
                        // Записываем нули в промежутках
                        for (int j = 0; j < spacing; j++)
                        {
                            writer.Write((byte)0);
                        }
                    }
                    
                    // Заполняем оставшееся пространство нулями
                    int remainingChars = oldText.Length - newText.Length;
                    for (int i = 0; i < remainingChars * (spacing + 1); i++)
                    {
                        writer.Write((byte)0);
                    }
                    
                    Console.WriteLine($"Updated SPACED-{spacing} text at position {location.Position}");
                }
                else
                {
                    Console.WriteLine($"WARNING: Cannot update SPACED-{spacing} text at position {location.Position} - new text too long");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating spaced text at position {location.Position}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update text at a specific location in the file
        /// </summary>
        private static void UpdateTextAtLocation(FileStream fs, BinaryWriter writer, TextLocation location, string oldText, string newText)
        {
            try
            {
                fs.Position = location.Position;
                
                if (location.Format == "UTF16")
                {
                    // UTF-16 format
                    byte[] newBytes = Encoding.BigEndianUnicode.GetBytes(newText);
                    
                    // Check if the new text size matches the old size
                    if (newBytes.Length == location.Size)
                    {
                        // Direct replacement
                        writer.Write(newBytes);
                        Console.WriteLine($"Updated UTF16 text at position {location.Position}");
                    }
                    else if (location.Key == TEXT_CONTENT_KEY)
                    {
                        // This is the main text descriptor - need to update length too
                        // Move back 4 bytes to update the length field
                        fs.Position -= 4;
                        
                        // Write new length (character count)
                        WriteUInt32BE(writer, (uint)newText.Length);
                        
                        // Write new text
                        writer.Write(newBytes);
                        
                        Console.WriteLine($"Updated UTF16 text with new length at position {location.Position}");
                    }
                    else
                    {
                        Console.WriteLine($"WARNING: Cannot update UTF16 text at position {location.Position} - size mismatch");
                    }
                }
                else if (location.Format == "ASCII")
                {
                    // ASCII format
                    byte[] newBytes = Encoding.ASCII.GetBytes(newText);
                    
                    // Check if sizes match
                    if (newBytes.Length == location.Size)
                    {
                        writer.Write(newBytes);
                        Console.WriteLine($"Updated ASCII text at position {location.Position}");
                    }
                    else
                    {
                        Console.WriteLine($"WARNING: Cannot update ASCII text at position {location.Position} - size mismatch");
                    }
                }
                else if (location.Format == "UTF16-HEX")
                {
                    // UTF16-HEX format (char byte followed by 3 zero bytes)
                    if (newText.Length == oldText.Length)
                    {
                        // Write each character as a byte followed by 3 zero bytes
                        for (int i = 0; i < newText.Length; i++)
                        {
                            writer.Write((byte)newText[i]);
                            writer.Write((byte)0);
                            writer.Write((byte)0);
                            writer.Write((byte)0);
                        }
                        
                        Console.WriteLine($"Updated UTF16-HEX text at position {location.Position}");
                    }
                    else
                    {
                        Console.WriteLine($"WARNING: Cannot update UTF16-HEX text at position {location.Position} - length mismatch");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating text at position {location.Position}: {ex.Message}");
            }
        }
        
        

/// <summary>
/// Добавляет известные местоположения текста
/// </summary>
private static void AddKnownTextLocations(List<TextLocation> textLocations, string textToFind)
{
    // Список известных местоположений текста
    Dictionary<long, string> knownLocations = new Dictionary<long, string>
    {
        { 0x1570, "UTF16" },  // PostScript format
        { 0xA820, "UTF16" }   // PostScript/PDF area
    };
    
    foreach (var location in knownLocations)
    {
        // Проверяем, что это место еще не добавлено
        if (!textLocations.Exists(loc => loc.Position == location.Key))
        {
            textLocations.Add(new TextLocation
            {
                Position = location.Key,
                Size = (uint)(textToFind.Length * 2), // UTF-16 использует 2 байта на символ
                Format = location.Value,
                Key = "KNOWN"
            });
            
            Console.WriteLine($"Added known text location at position 0x{location.Key:X8}");
        }
    }
}

/// <summary>
/// Изменяет текст в слое по его индексу
/// </summary>
public static bool ModifyTextLayerByIndex(string inputPath, string outputPath, int layerIndex, string newText)
{
    try
    {
        // Сначала находим все текстовые слои
        List<TextLayerInfo> textLayers = FindTextLayers(inputPath);
        
        if (textLayers.Count == 0 || layerIndex < 0 || layerIndex >= textLayers.Count)
        {
            Console.WriteLine("Указанный индекс текстового слоя не найден.");
            return false;
        }
        
        // Получаем выбранный слой
        var layer = textLayers[layerIndex];
        string oldText = layer.Text;
        
        // Копируем входной файл в выходной
        File.Copy(inputPath, outputPath, true);
        
        // Открываем выходной файл для модификации
        using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite))
        using (var writer = new BinaryWriter(fs))
        {
            Console.WriteLine($"Изменение текста слоя {layerIndex} с '{oldText}' на '{newText}'");
            
            // Обрабатываем каждую позицию текста
            foreach (var location in layer.TextLocations)
            {
                Console.WriteLine($"Обновление текста в формате {location.Format} на позиции 0x{location.Position:X8}");
                
                try
                {
                    // Позиционируемся на точное место в файле
                    fs.Position = location.Position;
                    
                    // Используем разные методы обновления в зависимости от формата текста
                    if (location.Format == "UTF16")
                    {
                        byte[] newBytes = Encoding.BigEndianUnicode.GetBytes(newText);
                        
                        if (newBytes.Length <= location.Size)
                        {
                            writer.Write(newBytes);
                            
                            // Заполняем оставшееся пространство нулями
                            int remaining = (int)(location.Size - newBytes.Length);
                            for (int i = 0; i < remaining; i++)
                            {
                                writer.Write((byte)0);
                            }
                            
                            Console.WriteLine($"Обновлен UTF16 текст на позиции 0x{location.Position:X8}");
                        }
                        else
                        {
                            Console.WriteLine($"ПРЕДУПРЕЖДЕНИЕ: Невозможно обновить UTF16 текст на позиции 0x{location.Position:X8} - новый текст слишком длинный");
                        }
                    }
                    else if (location.Format == "ASCII")
                    {
                        byte[] newBytes = Encoding.ASCII.GetBytes(newText);
                        
                        if (newBytes.Length <= location.Size)
                        {
                            writer.Write(newBytes);
                            
                            // Заполняем оставшееся пространство нулями
                            int remaining = (int)(location.Size - newBytes.Length);
                            for (int i = 0; i < remaining; i++)
                            {
                                writer.Write((byte)0);
                            }
                            
                            Console.WriteLine($"Обновлен ASCII текст на позиции 0x{location.Position:X8}");
                        }
                        else
                        {
                            Console.WriteLine($"ПРЕДУПРЕЖДЕНИЕ: Невозможно обновить ASCII текст на позиции 0x{location.Position:X8} - новый текст слишком длинный");
                        }
                    }
                    else if (location.Format == "UTF16-HEX" || location.Format == "PS-TEXT-EOL" || location.Format == "PS-NUMBER-EOL" || 
                             location.Format == "PS-UTF16" || location.Format == "PS-SPECIAL" || location.Format == "BIN-UTF16")
                    {
                        // Для специальных форматов
                        if (oldText.Length == newText.Length)
                        {
                            byte[] newBytes = Encoding.BigEndianUnicode.GetBytes(newText);
                            writer.Write(newBytes);
                            Console.WriteLine($"Обновлен {location.Format} текст на позиции 0x{location.Position:X8}");
                        }
                        else
                        {
                            Console.WriteLine($"ПРЕДУПРЕЖДЕНИЕ: Невозможно обновить {location.Format} текст на позиции 0x{location.Position:X8} - длина текста должна совпадать");
                        }
                    }
                    else if (location.Format.StartsWith("HEX-") || location.Format.StartsWith("SPACED-"))
                    {
                        // Формат с пробелами/HEX
                        string[] parts = location.Format.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int bytesPerChar))
                        {
                            if (newText.Length <= oldText.Length)
                            {
                                for (int i = 0; i < newText.Length; i++)
                                {
                                    writer.Write((byte)newText[i]);
                                    
                                    // Записываем нули для заполнения
                                    for (int j = 1; j < bytesPerChar; j++)
                                    {
                                        writer.Write((byte)0);
                                    }
                                }
                                
                                // Заполняем оставшееся пространство нулями
                                int remainingChars = oldText.Length - newText.Length;
                                for (int i = 0; i < remainingChars * bytesPerChar; i++)
                                {
                                    writer.Write((byte)0);
                                }
                                
                                Console.WriteLine($"Обновлен {location.Format} текст на позиции 0x{location.Position:X8}");
                            }
                            else
                            {
                                Console.WriteLine($"ПРЕДУПРЕЖДЕНИЕ: Невозможно обновить {location.Format} текст на позиции 0x{location.Position:X8} - новый текст слишком длинный");
                            }
                        }
                    }
                    
                    // Принудительно записываем буфер на диск
                    writer.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при обновлении текста на позиции 0x{location.Position:X8}: {ex.Message}");
                }
            }
            
            // Дополнительно: обновляем особые области вручную
            Console.WriteLine("Performing forced update of known text locations...");
            UpdateSpecificTextLocations(fs, writer, oldText, newText);
        }
        
        // Дополнительная проверка - всегда обновляем известные позиции в конце
        Console.WriteLine("Performing additional direct update of critical text locations...");
        DirectTextUpdate(outputPath, oldText, newText);
        
        Console.WriteLine($"Модификация текстовых слоев завершена. Файл сохранен в {outputPath}");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка при модификации текстового слоя: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        return false;
    }
}

/// <summary>
/// Обновляет специфические области текста, которые могут не обнаруживаться автоматически
/// </summary>
private static void UpdateSpecificTextLocations(FileStream fs, BinaryWriter writer, string oldText, string newText)
{
    try
    {
        // Если длины не совпадают, выдаем предупреждение, но всё равно пытаемся обновить
        if (oldText.Length != newText.Length)
        {
            Console.WriteLine("WARNING: Text lengths don't match. This may cause issues.");
        }
        
        // Список известных проблемных областей
        Dictionary<long, string> specificLocations = new Dictionary<long, string>
        {
            { 0x1570, "UTF16" }, // Позиция 5488
            { 0xA820, "UTF16" }  // Позиция 43040
        };
        
        // Обновляем каждую специфическую область
        foreach (var location in specificLocations)
        {
            try
            {
                Console.WriteLine($"Updating specific location at 0x{location.Key:X8}...");
                
                // Сохраняем текущую позицию
                long currentPosition = fs.Position;
                
                // Устанавливаем позицию на известное местоположение
                fs.Position = location.Key;
                
                if (location.Value == "UTF16")
                {
                    // Записываем текст в UTF-16 формате
                    byte[] newBytes = Encoding.BigEndianUnicode.GetBytes(newText);
                    writer.Write(newBytes);
                    writer.Flush(); // Принудительно записываем на диск
                    Console.WriteLine($"Successfully updated UTF16 text at position 0x{location.Key:X8}");
                }
                else if (location.Value == "ASCII")
                {
                    // Записываем текст в ASCII формате
                    byte[] newBytes = Encoding.ASCII.GetBytes(newText);
                    writer.Write(newBytes);
                    writer.Flush(); // Принудительно записываем на диск
                    Console.WriteLine($"Successfully updated ASCII text at position 0x{location.Key:X8}");
                }
                
                // Восстанавливаем позицию
                fs.Position = currentPosition;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating specific location at 0x{location.Key:X8}: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in UpdateSpecificTextLocations: {ex.Message}");
    }
}

/// <summary>
/// Прямое обновление текста в известных позициях
/// </summary>
public static bool DirectTextUpdate(string filePath, string oldText, string newText)
{
    try
    {
        // Открываем файл для записи
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
        using (var writer = new BinaryWriter(fs))
        {
            // Список известных позиций
            long[] positions = new long[] { 0x1570, 0xA820 };
            
            foreach (long position in positions)
            {
                // Устанавливаем позицию
                fs.Position = position;
                
                // Записываем новый текст в UTF-16 формате
                byte[] newBytes = Encoding.BigEndianUnicode.GetBytes(newText);
                writer.Write(newBytes);
                writer.Flush();
                
                Console.WriteLine($"Direct update of text at position 0x{position:X8}");
            }
        }
        
        Console.WriteLine("Direct text update complete.");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during direct text update: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        return false;
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
            return (int)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
        }
        
        private static void WriteUInt32BE(BinaryWriter writer, uint value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }
        #endregion
    }
}