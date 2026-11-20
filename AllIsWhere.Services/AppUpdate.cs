using System;

namespace AllIsWhere.Services;

public sealed record AppUpdate(Version Version, string DownloadUrl, string Sha256, string ReleaseNotes);
