class LayerNameInfo
{
    public int LayerIndex { get; set; }
            
    // ASCII name (standard Pascal string)
    public string AsciiName { get; set; }
    public long AsciiNamePosition { get; set; }
            
    // Unicode name (in "luni" block)
    public bool HasUnicodeName { get; set; }
    public string UnicodeName { get; set; }
    public long UnicodeNamePosition { get; set; }
            
    // Descriptor name (in "tdta" or other blocks)
    public bool HasDescriptorName { get; set; }
    public string DescriptorName { get; set; }
    public List<long> DescriptorNamePositions { get; set; } = new List<long>();
            
    // Additional blocks that might contain name references
    public List<AdditionalNameBlock> AdditionalNameInfoBlocks { get; set; } = new List<AdditionalNameBlock>();
            
    // For debugging
    public List<string> BlockKeys { get; set; } = new List<string>();
}