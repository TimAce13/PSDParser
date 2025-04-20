using System;
using System.IO;
using PsdReaderApp.Core;
using PsdReaderApp.Debugging;

namespace PsdReaderApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("PSD Layer Name Editor");
            Console.WriteLine("=====================\n");
            
            // Default file paths - update these to match your environment
            string inputPsdPath = "/Users/timofeyuv/Downloads/newpsd.psd";
            string outputPsdPath = "/Users/timofeyuv/Downloads/output-modified.psd";
            
            // Allow command-line arguments to override defaults
            if (args.Length >= 1) inputPsdPath = args[0];
            if (args.Length >= 2) outputPsdPath = args[1];
            
            // Check if user wants to debug the file
            if (args.Length >= 3 && args[2].ToLower() == "debug")
            {
                Console.WriteLine("Debug mode: Analyzing layer names in PSD file");
                PsdDebugger.AnalyzeLayerNames(inputPsdPath);
                return;
            }
            
            try
            {
                // First read all layers to display to the user
                var reader = new PsdReader(inputPsdPath);
                var layers = reader.ReadLayers();
                
                Console.WriteLine($"Found {layers.Count} layers in {inputPsdPath}:");
                for (int i = 0; i < layers.Count; i++)
                {
                    Console.WriteLine($"  [{i}] {layers[i].Name}");
                }
                
                // Get user input
                Console.Write("\nEnter layer index to rename: ");
                if (!int.TryParse(Console.ReadLine(), out int layerIndex) || layerIndex < 0 || layerIndex >= layers.Count)
                {
                    Console.WriteLine("Invalid layer index. Exiting.");
                    return;
                }
                
                Console.Write($"Current name: '{layers[layerIndex].Name}'\nNew name: ");
                string newName = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(newName))
                {
                    Console.WriteLine("New name cannot be empty. Exiting.");
                    return;
                }
                
                // Generate a unique output filename
                string fileName = Path.GetFileNameWithoutExtension(outputPsdPath);
                string extension = Path.GetExtension(outputPsdPath);
                string directory = Path.GetDirectoryName(outputPsdPath);
                string uniqueOutputPath = Path.Combine(directory, $"{fileName}_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}{extension}");
                
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
                            Console.WriteLine($"Verified: Layer {layerIndex} name is now '{updatedLayers[layerIndex].Name}'");
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
            
            // Wait for user to press a key before closing
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}