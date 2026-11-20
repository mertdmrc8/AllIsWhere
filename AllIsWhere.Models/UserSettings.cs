using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AllIsWhere.Models;

public sealed class UserSettings
{
	public List<string> SearchRoots { get; set; } = new List<string>();

	public string Language { get; set; } = "tr";

	public bool ShowHiddenFolders { get; set; }

	public string Theme { get; set; } = "Light";

	public double ResultFontSize { get; set; } = 14.0;

	public int MaxResults { get; set; } = 1000;

	public int SearchDebounceMs { get; set; } = 100;

	public bool StartWithWindows { get; set; } = true;

	public string ScanPerformance { get; set; } = "Balanced";

	public DateTime? LastUpdateCheckUtc { get; set; }

	public List<SavedSearch> SavedSearches { get; set; } = new List<SavedSearch>();

	public List<string> ExcludedFolders { get; set; } = new List<string>();

	public void Normalize()
	{
		Language = (string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "tr");
		SearchRoots = (SearchRoots ?? new List<string>()).Where((string path) => !string.IsNullOrWhiteSpace(path)).Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		SavedSearches = (from @group in (SavedSearches ?? new List<SavedSearch>()).Where((SavedSearch item) => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Query)).GroupBy((SavedSearch item) => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
			select @group.Last() with
			{
				Name = @group.Key.Trim(),
				Query = @group.Last().Query.Trim()
			}).Take(100).ToList();
		ExcludedFolders = (from path in ExcludedFolders ?? new List<string>()
			where !string.IsNullOrWhiteSpace(path)
			select Path.TrimEndingDirectorySeparator(path.Trim())).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (!string.Equals(Theme, "Dark", StringComparison.OrdinalIgnoreCase))
		{
			Theme = "Light";
		}
		double resultFontSize = ResultFontSize;
		int num;
		if (resultFontSize <= 12.0)
		{
			num = 12;
		}
		else if (resultFontSize <= 14.0)
		{
			num = 14;
		}
		else
		{
			num = ((!(resultFontSize <= 16.0)) ? 18 : 16);
		}
		ResultFontSize = num;
		int maxResults = MaxResults;
		if (maxResults <= 1000)
		{
			if (maxResults <= 250)
			{
				num = 250;
			}
			else
			{
				num = ((maxResults > 500) ? 1000 : 500);
			}
		}
		else
		{
			num = ((maxResults > 2500) ? 5000 : 2500);
		}
		MaxResults = num;
		num = SearchDebounceMs;
		if (num <= 50)
		{
			maxResults = ((num > 0) ? 50 : 0);
		}
		else
		{
			maxResults = ((num > 100) ? 200 : 100);
		}
		SearchDebounceMs = maxResults;
		string scanPerformance = ScanPerformance;
		string scanPerformance2;
		if (scanPerformance == "Fast")
		{
			scanPerformance2 = "Fast";
		}
		else
		{
			scanPerformance2 = ((!(scanPerformance == "LowResource")) ? "Balanced" : "LowResource");
		}
		ScanPerformance = scanPerformance2;
	}
}
