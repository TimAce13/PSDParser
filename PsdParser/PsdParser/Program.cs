using System;
using System.IO;
using System.Collections.Generic;
using PsdReaderApp.Core;
using PsdReaderApp.Debugging;

namespace PsdReaderApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("PSD Layer Editor");
            Console.WriteLine("=================\n");

            // Default file paths - update these to match your environment
            string inputPsdPath = "C:\\Users\\timuv\\Downloads\\testpsdfile.psd";
            string outputPsdPath = "C:\\Users\\timuv\\Downloads\\output-modified.psd";

            // Allow command-line arguments to override defaults
            if (args.Length >= 1) inputPsdPath = args[0];
            if (args.Length >= 2) outputPsdPath = args[1];

            // Check for debug mode
            if (args.Length >= 3 && args[2].ToLower() == "debug")
            {
                Console.WriteLine("Debug mode: Analyzing layer names in PSD file");
                PsdDebugger.AnalyzeLayerNames(inputPsdPath);
                return;
            }

            // Check for text layer mode
            if (args.Length >= 3 && args[2].ToLower() == "textlayers")
            {
                Console.WriteLine("Text Layer mode: Finding and displaying text layers");
                var textLayers = PsdTextLayer.FindTextLayers(inputPsdPath);

                if (textLayers.Count > 0)
                {
                    Console.WriteLine($"\nFound {textLayers.Count} text layers:");
                    for (int i = 0; i < textLayers.Count; i++)
                    {
                        Console.WriteLine(
                            $"  [{i}] Layer {textLayers[i].LayerIndex}: '{textLayers[i].LayerName}' - Text: '{textLayers[i].Text}'");
                    }
                }
                else
                {
                    Console.WriteLine("No text layers found in the file.");
                }

                return;
            }

            // Show main menu
            while (true)
            {
                Console.WriteLine("\nSelect an operation:");
                Console.WriteLine("1. Rename layer");
                Console.WriteLine("2. Modify text layer content");
                Console.WriteLine("3. Exit");
                Console.Write("\nEnter option (1-3): ");

                string option = Console.ReadLine()?.Trim();

                if (option == "1")
                {
                    RenameLayerOperation(inputPsdPath, outputPsdPath);
                }
                else if (option == "2")
                {
                    ModifyTextLayerOperation(inputPsdPath, outputPsdPath);
                }
                else if (option == "3")
                {
                    break;
                }
                else
                {
                    Console.WriteLine("Invalid option. Please try again.");
                }
            }

            Console.WriteLine("\nProgram terminated. Press any key to exit...");
            Console.ReadKey();
        }

        static void RenameLayerOperation(string inputPsdPath, string outputPsdPath)
        {
            try
            {
                // First read all layers to display to the user
                var reader = new PsdReader(inputPsdPath);
                var layers = reader.ReadLayers();

                Console.WriteLine($"\nFound {layers.Count} layers in {inputPsdPath}:");
                for (int i = 0; i < layers.Count; i++)
                {
                    Console.WriteLine($"  [{i}] {layers[i].Name}");
                }

                // Get user input
                Console.Write("\nEnter layer index to rename: ");
                if (!int.TryParse(Console.ReadLine(), out int layerIndex) || layerIndex < 0 ||
                    layerIndex >= layers.Count)
                {
                    Console.WriteLine("Invalid layer index. Operation cancelled.");
                    return;
                }

                Console.Write($"Current name: '{layers[layerIndex].Name}'\nNew name: ");
                string newName = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(newName))
                {
                    Console.WriteLine("New name cannot be empty. Operation cancelled.");
                    return;
                }

                // Generate a unique output filename
                string fileName = Path.GetFileNameWithoutExtension(outputPsdPath);
                string extension = Path.GetExtension(outputPsdPath);
                string directory = Path.GetDirectoryName(outputPsdPath);
                string uniqueOutputPath = Path.Combine(directory,
                    $"{fileName}_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}{extension}");

                // Confirm with the user
                Console.Write($"\nRename layer {layerIndex} from '{layers[layerIndex].Name}' to '{newName}'? (Y/N): ");
                string confirmation = Console.ReadLine()?.Trim().ToUpper() ?? "";
                if (confirmation != "Y" && confirmation != "YES")
                {
                    Console.WriteLine("Operation cancelled by user.");
                    return;
                }

                // Delete the output file if it exists
                if (File.Exists(uniqueOutputPath))
                {
                    File.Delete(uniqueOutputPath);
                }

                // Use the Unicode-aware layer renamer
                Console.WriteLine($"\nRenaming layer {layerIndex} to '{newName}'...");
                bool success = PsdLayer.RenameLayer(inputPsdPath, uniqueOutputPath, layerIndex, newName);

                if (success)
                {
                    Console.WriteLine($"\nOperation completed successfully! File saved to {uniqueOutputPath}");

                    // Verify the changes
                    if (File.Exists(uniqueOutputPath))
                    {
                        Console.WriteLine("\nVerifying changes in the output file...");
                        var verifyReader = new PsdReader(uniqueOutputPath);
                        var updatedLayers = verifyReader.ReadLayers();

                        if (updatedLayers.Count == layers.Count)
                        {
                            Console.WriteLine(
                                $"Verified: Layer {layerIndex} name is now '{updatedLayers[layerIndex].Name}'");
                        }
                        else
                        {
                            Console.WriteLine("Warning: Could not verify changes - layer count mismatch.");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("\nOperation failed!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void ModifyTextLayerOperation(string inputPsdPath, string outputPsdPath)
{
    try
    {
        // Находим все текстовые слои
        Console.WriteLine("\nСканирование текстовых слоев...");
        var textLayers = PsdTextLayer.FindTextLayers(inputPsdPath);

        if (textLayers.Count == 0)
        {
            Console.WriteLine("Текстовые слои не найдены. Операция отменена.");
            return;
        }

        Console.WriteLine($"\nНайдено {textLayers.Count} текстовых слоев:");
        for (int i = 0; i < textLayers.Count; i++)
        {
            Console.WriteLine(
                $"  [{i}] Слой {textLayers[i].LayerIndex}: '{textLayers[i].LayerName}' - Текст: '{textLayers[i].Text}'");
        }

        // Запрашиваем индекс слоя для изменения
        Console.Write("\nВведите индекс текстового слоя для изменения: ");
        if (!int.TryParse(Console.ReadLine(), out int textLayerIndex) || textLayerIndex < 0 || textLayerIndex >= textLayers.Count)
        {
            Console.WriteLine("Некорректный индекс слоя. Операция отменена.");
            return;
        }

        // Получаем выбранный слой
        var selectedLayer = textLayers[textLayerIndex];
        
        Console.WriteLine($"Текущий текст: '{selectedLayer.Text}'");
        Console.Write("Новый текст: ");
        string newText = Console.ReadLine();

        if (newText == null)
        {
            Console.WriteLine("Новый текст не может быть null. Операция отменена.");
            return;
        }

        // Генерируем уникальное имя выходного файла
        string fileName = Path.GetFileNameWithoutExtension(outputPsdPath);
        string extension = Path.GetExtension(outputPsdPath);
        string directory = Path.GetDirectoryName(outputPsdPath);
        string uniqueOutputPath = Path.Combine(directory,
            $"{fileName}_text_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}{extension}");

        // Подтверждение
        Console.Write($"\nИзменить текст в слое {textLayerIndex} '{selectedLayer.LayerName}' с '{selectedLayer.Text}' на '{newText}'? (Y/N): ");
        string confirmation = Console.ReadLine()?.Trim().ToUpper() ?? "";
        if (confirmation != "Y" && confirmation != "YES")
        {
            Console.WriteLine("Операция отменена пользователем.");
            return;
        }

        // Удаляем выходной файл, если он существует
        if (File.Exists(uniqueOutputPath))
        {
            File.Delete(uniqueOutputPath);
        }

        // Выполняем замену текста
        Console.WriteLine($"\nИзменение текста слоя {textLayerIndex} с '{selectedLayer.Text}' на '{newText}'...");
        bool success = PsdTextLayer.ModifyTextLayerByIndex(inputPsdPath, uniqueOutputPath, textLayerIndex, newText);

        if (success)
        {
            Console.WriteLine($"\nОперация успешно завершена! Файл сохранен в {uniqueOutputPath}");

            // Проверяем изменения
            Console.WriteLine("\nПроверка изменений в выходном файле...");
            var verifyLayers = PsdTextLayer.FindTextLayers(uniqueOutputPath);

            if (verifyLayers.Count > 0)
            {
                Console.WriteLine("\nТекстовые слои в модифицированном файле:");
                foreach (var layer in verifyLayers)
                {
                    Console.WriteLine(
                        $"  Слой {layer.LayerIndex}: '{layer.LayerName}' - Текст: '{layer.Text}'");
                }
            }
            else
            {
                Console.WriteLine("Предупреждение: Текстовые слои не найдены в выходном файле.");
            }
        }
        else
        {
            Console.WriteLine("\nОперация не удалась!");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nОшибка: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
}
        
        
    }
}
            
//
// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Text;
//
// namespace PsdTextScanner
// {
//     class Program
//     {
//         static void Main(string[] args)
//         {
//             Console.WriteLine("PSD Text Scanner");
//             Console.WriteLine("===============\n");
//             
//             string psdFilePath = "C:\\Users\\timuv\\Downloads\\testpsdfile.psd";
//             
//             if (args.Length >= 1)
//                 psdFilePath = args[0];
//             
//             Console.WriteLine($"Scanning file: {psdFilePath}");
//             
//             if (!File.Exists(psdFilePath))
//             {
//                 Console.WriteLine("Error: File not found!");
//                 return;
//             }
//             
//             Console.Write("Enter text to search for: ");
//             string searchText = Console.ReadLine();
//             
//             if (string.IsNullOrEmpty(searchText))
//             {
//                 Console.WriteLine("Search text cannot be empty!");
//                 return;
//             }
//             
//             ScanEntireFile(psdFilePath, searchText);
//             
//             Console.WriteLine("\nScan complete. Press any key to exit...");
//             Console.ReadKey();
//         }
//         
//         static void ScanEntireFile(string filePath, string searchText)
//         {
//             Console.WriteLine($"\nScanning for '{searchText}' in various encodings...\n");
//             
//             try
//             {
//                 // Read the entire file into memory
//                 byte[] fileData = File.ReadAllBytes(filePath);
//                 Console.WriteLine($"File size: {fileData.Length} bytes");
//                 
//                 // Create search patterns
//                 byte[] utf16Pattern = Encoding.BigEndianUnicode.GetBytes(searchText);
//                 byte[] asciiPattern = Encoding.ASCII.GetBytes(searchText);
//                 
//                 Console.WriteLine("\n=== UTF-16 (Big Endian) Matches ===");
//                 FindAllPatterns(fileData, utf16Pattern, "UTF-16 BE", 500);
//                 
//                 Console.WriteLine("\n=== ASCII Matches ===");
//                 FindAllPatterns(fileData, asciiPattern, "ASCII", 500);
//                 
//                 // Check for hex patterns with different spacing
//                 for (int spacing = 1; spacing <= 4; spacing++)
//                 {
//                     Console.WriteLine($"\n=== Hex Pattern with {spacing} Byte Spacing ===");
//                     FindHexPatterns(fileData, searchText, spacing, 500);
//                 }
//                 
//                 // Look for PostScript patterns
//                 Console.WriteLine("\n=== PostScript Format Patterns ===");
//                 FindPostScriptPatterns(fileData, searchText, 500);
//                 
//                 // Dump context around specific byte positions
//                 Console.WriteLine("\n=== Known Problematic Positions ===");
//                 DumpHexContext(fileData, 0x1570, 64); // Around position 0x1570
//                 DumpHexContext(fileData, 0xA820, 64); // Around position 0xA820
//             }
//             catch (Exception ex)
//             {
//                 Console.WriteLine($"Error: {ex.Message}");
//                 Console.WriteLine(ex.StackTrace);
//             }
//         }
//         
//         static void FindAllPatterns(byte[] data, byte[] pattern, string format, int contextSize)
//         {
//             if (pattern.Length == 0 || data.Length < pattern.Length)
//                 return;
//             
//             int matchCount = 0;
//             List<int> positions = new List<int>();
//             
//             for (int i = 0; i <= data.Length - pattern.Length; i++)
//             {
//                 bool match = true;
//                 for (int j = 0; j < pattern.Length; j++)
//                 {
//                     if (data[i + j] != pattern[j])
//                     {
//                         match = false;
//                         break;
//                     }
//                 }
//                 
//                 if (match)
//                 {
//                     matchCount++;
//                     positions.Add(i);
//                     Console.WriteLine($"Match #{matchCount} at position 0x{i:X8} ({i})");
//                     DumpHexContext(data, i, contextSize);
//                 }
//             }
//             
//             Console.WriteLine($"Total {format} matches: {matchCount}");
//         }
//         
//         static void FindHexPatterns(byte[] data, string text, int spacing, int contextSize)
//         {
//             if (string.IsNullOrEmpty(text) || data.Length < text.Length * (spacing + 1))
//                 return;
//             
//             int matchCount = 0;
//             
//             for (int i = 0; i <= data.Length - text.Length * (spacing + 1); i++)
//             {
//                 bool match = true;
//                 for (int j = 0; j < text.Length; j++)
//                 {
//                     int pos = i + j * (spacing + 1);
//                     
//                     // Check if the character matches
//                     if (pos >= data.Length || data[pos] != (byte)text[j])
//                     {
//                         match = false;
//                         break;
//                     }
//                     
//                     // Check if intermediate bytes are zeros
//                     bool allZeros = true;
//                     for (int k = 1; k <= spacing; k++)
//                     {
//                         if (pos + k >= data.Length || data[pos + k] != 0)
//                         {
//                             allZeros = false;
//                             break;
//                         }
//                     }
//                     
//                     if (!allZeros)
//                     {
//                         match = false;
//                         break;
//                     }
//                 }
//                 
//                 if (match)
//                 {
//                     matchCount++;
//                     Console.WriteLine($"Match #{matchCount} at position 0x{i:X8} ({i})");
//                     DumpHexContext(data, i, contextSize);
//                 }
//             }
//             
//             Console.WriteLine($"Total Hex-{spacing} matches: {matchCount}");
//         }
//         
//         static void FindPostScriptPatterns(byte[] data, string text, int contextSize)
//         {
//             byte[] utf16Text = Encoding.BigEndianUnicode.GetBytes(text);
//             
//             // Pattern: (/Text ( + FE FF + text)
//             byte[] ps1 = { 0x2F, 0x54, 0x65, 0x78, 0x74, 0x20, 0x28, 0xFE, 0xFF };
//             // Pattern: (0 ( + FE FF + text)
//             byte[] ps2 = { 0x30, 0x20, 0x28, 0xFE, 0xFF };
//             
//             int matchCount = 0;
//             
//             // Search for pattern 1
//             for (int i = 0; i <= data.Length - ps1.Length - utf16Text.Length - 3; i++)
//             {
//                 bool patternMatch = true;
//                 for (int j = 0; j < ps1.Length; j++)
//                 {
//                     if (data[i + j] != ps1[j])
//                     {
//                         patternMatch = false;
//                         break;
//                     }
//                 }
//                 
//                 if (patternMatch)
//                 {
//                     int textPos = i + ps1.Length;
//                     bool textMatch = true;
//                     
//                     for (int j = 0; j < utf16Text.Length; j++)
//                     {
//                         if (textPos + j >= data.Length || data[textPos + j] != utf16Text[j])
//                         {
//                             textMatch = false;
//                             break;
//                         }
//                     }
//                     
//                     if (textMatch)
//                     {
//                         matchCount++;
//                         Console.WriteLine($"PostScript Match #{matchCount} at position 0x{i:X8} ({i})");
//                         Console.WriteLine($"Text position: 0x{textPos:X8} ({textPos})");
//                         DumpHexContext(data, i, contextSize);
//                     }
//                 }
//             }
//             
//             // Search for pattern 2
//             for (int i = 0; i <= data.Length - ps2.Length - utf16Text.Length - 3; i++)
//             {
//                 bool patternMatch = true;
//                 for (int j = 0; j < ps2.Length; j++)
//                 {
//                     if (data[i + j] != ps2[j])
//                     {
//                         patternMatch = false;
//                         break;
//                     }
//                 }
//                 
//                 if (patternMatch)
//                 {
//                     int textPos = i + ps2.Length;
//                     bool textMatch = true;
//                     
//                     for (int j = 0; j < utf16Text.Length; j++)
//                     {
//                         if (textPos + j >= data.Length || data[textPos + j] != utf16Text[j])
//                         {
//                             textMatch = false;
//                             break;
//                         }
//                     }
//                     
//                     if (textMatch)
//                     {
//                         matchCount++;
//                         Console.WriteLine($"PostScript Match #{matchCount} at position 0x{i:X8} ({i})");
//                         Console.WriteLine($"Text position: 0x{textPos:X8} ({textPos})");
//                         DumpHexContext(data, i, contextSize);
//                     }
//                 }
//             }
//             
//             Console.WriteLine($"Total PostScript matches: {matchCount}");
//         }
//         
//         static void DumpHexContext(byte[] data, int position, int contextSize)
//         {
//             int halfContext = contextSize / 2;
//             int startPos = Math.Max(0, position - halfContext);
//             int endPos = Math.Min(data.Length - 1, position + halfContext);
//             
//             StringBuilder hexDump = new StringBuilder();
//             StringBuilder asciiDump = new StringBuilder();
//             
//             hexDump.AppendLine($"Position: 0x{position:X8} ({position})");
//             hexDump.AppendLine("Hex Dump:");
//             
//             const int bytesPerLine = 16;
//             
//             for (int i = startPos; i <= endPos; i += bytesPerLine)
//             {
//                 StringBuilder hexLine = new StringBuilder();
//                 StringBuilder asciiLine = new StringBuilder();
//                 
//                 hexLine.Append($"{i:X8}: ");
//                 
//                 for (int j = 0; j < bytesPerLine; j++)
//                 {
//                     int currentPos = i + j;
//                     
//                     if (currentPos <= endPos)
//                     {
//                         byte b = data[currentPos];
//                         
//                         // Highlight the match position
//                         if (currentPos == position)
//                         {
//                             hexLine.Append("[");
//                             hexLine.Append($"{b:X2}");
//                             hexLine.Append("]");
//                         }
//                         else
//                         {
//                             hexLine.Append($"{b:X2}");
//                         }
//                         
//                         hexLine.Append(" ");
//                         
//                         if (b >= 32 && b < 127)
//                             asciiLine.Append((char)b);
//                         else
//                             asciiLine.Append(".");
//                     }
//                     else
//                     {
//                         hexLine.Append("   ");
//                         asciiLine.Append(" ");
//                     }
//                 }
//                 
//                 hexDump.AppendLine($"{hexLine} | {asciiLine}");
//             }
//             
//             Console.WriteLine(hexDump.ToString());
//         }
//         
//         static void AnalyzeContext(byte[] data, int position, int contextSize)
//         {
//             int halfContext = contextSize / 2;
//             int startPos = Math.Max(0, position - halfContext);
//             int endPos = Math.Min(data.Length - 1, position + halfContext);
//             
//             // Try to identify patterns around the match
//             Console.WriteLine("Pattern Analysis:");
//             
//             // Look for "(/Text (" or "(0 (" pattern before
//             byte[] textPattern = { 0x2F, 0x54, 0x65, 0x78, 0x74, 0x20, 0x28 };
//             byte[] numPattern = { 0x30, 0x20, 0x28 };
//             
//             for (int i = startPos; i < position; i++)
//             {
//                 // Check for text pattern
//                 if (i + textPattern.Length <= position)
//                 {
//                     bool match = true;
//                     for (int j = 0; j < textPattern.Length; j++)
//                     {
//                         if (data[i + j] != textPattern[j])
//                         {
//                             match = false;
//                             break;
//                         }
//                     }
//                     
//                     if (match)
//                     {
//                         Console.WriteLine($"Found '/Text (' pattern at position 0x{i:X8} ({i})");
//                     }
//                 }
//                 
//                 // Check for number pattern
//                 if (i + numPattern.Length <= position)
//                 {
//                     bool match = true;
//                     for (int j = 0; j < numPattern.Length; j++)
//                     {
//                         if (data[i + j] != numPattern[j])
//                         {
//                             match = false;
//                             break;
//                         }
//                     }
//                     
//                     if (match)
//                     {
//                         Console.WriteLine($"Found '0 (' pattern at position 0x{i:X8} ({i})");
//                     }
//                 }
//             }
//             
//             // Look for ending patterns
//             for (int i = position; i < endPos; i++)
//             {
//                 // Check for ")
//                 if (data[i] == 0x29)
//                 {
//                     Console.WriteLine($"Found ')' character at position 0x{i:X8} ({i})");
//                     break;
//                 }
//             }
//         }
//     }
// }