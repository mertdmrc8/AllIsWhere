using System;
using System.Collections.Generic;
using System.Text;

namespace AllIsWhere.Engine;

public sealed record SearchQueryTerms(IReadOnlyList<string> NameTerms, IReadOnlyList<string> PathTerms, IReadOnlySet<string> Extensions)
{
	public static SearchQueryTerms Parse(string query)
	{
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in Tokenize(query))
		{
			int num = item.IndexOf(':');
			if (num > 0)
			{
				string text = item.Substring(0, num);
				string text2 = item.Substring(num + 1).Trim();
				if (text2.Length > 0 && text.Equals("name", StringComparison.OrdinalIgnoreCase))
				{
					list.Add(text2);
					continue;
				}
				if (text2.Length > 0 && text.Equals("path", StringComparison.OrdinalIgnoreCase))
				{
					list2.Add(text2);
					continue;
				}
				if (text2.Length > 0 && text.Equals("ext", StringComparison.OrdinalIgnoreCase))
				{
					string[] array = text2.Split(',', 59, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
					foreach (string text3 in array)
					{
						string text4 = (text3.StartsWith('*') ? text3.Substring(1) : text3);
						text4 = (text4.StartsWith('.') ? text4 : ("." + text4));
						if (text4.Length > 1)
						{
							hashSet.Add(text4);
						}
					}
					continue;
				}
			}
			list.Add(item);
		}
		return new SearchQueryTerms(list, list2, hashSet);
	}

	private static IEnumerable<string> Tokenize(string query)
	{
		StringBuilder token = new StringBuilder();
		bool quoted = false;
		foreach (char c in query)
		{
			if (c == '"')
			{
				quoted = !quoted;
			}
			else if (char.IsWhiteSpace(c) && !quoted)
			{
				if (token.Length > 0)
				{
					yield return token.ToString();
					token.Clear();
				}
			}
			else
			{
				token.Append(c);
			}
		}
		if (token.Length > 0)
		{
			yield return token.ToString();
		}
	}
}
