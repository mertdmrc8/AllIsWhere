using System.IO;

namespace AllIsWhere.Models;

public sealed class UserSettings
{
    public List<string> SearchRoots { get; set; } = new();
    public string Language { get; set; } = "tr";
    public bool ShowHiddenFolders { get; set; }
    public string Theme { get; set; } = "Light";
    public double ResultFontSize { get; set; } = 14;
    public int MaxResults { get; set; } = 1000;
    public int SearchDebounceMs { get; set; } = 100;
    public bool StartWithWindows { get; set; } = true;
    public string ScanPerformance { get; set; } = "Balanced";

    public void Normalize()
    {
        Language = string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "tr";
        SearchRoots = (SearchRoots ?? new List<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.TrimEndingDirectorySeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.Equals(Theme, "Dark", StringComparison.OrdinalIgnoreCase)) Theme = "Light";
        ResultFontSize = ResultFontSize switch { <= 12 => 12, <= 14 => 14, <= 16 => 16, _ => 18 };
        MaxResults = MaxResults switch { <= 250 => 250, <= 500 => 500, <= 1000 => 1000, <= 2500 => 2500, _ => 5000 };
        SearchDebounceMs = SearchDebounceMs switch { <= 0 => 0, <= 50 => 50, <= 100 => 100, _ => 200 };
        ScanPerformance = ScanPerformance switch
        {
            "Fast" => "Fast",
            "LowResource" => "LowResource",
            _ => "Balanced"
        };
    }
}
