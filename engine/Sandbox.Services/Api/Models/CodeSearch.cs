namespace Sandbox.Services;

/// <summary>
/// A page of code-search results from the public API. Only open-source code from publicly listed
/// packages is searched.
/// </summary>
public class CodeSearchResult
{
	/// <summary>Total number of matching files across all pages.</summary>
	public long TotalCount { get; set; }

	public List<CodeSearchFile> Files { get; set; } = new();
}

/// <summary>One matching source file.</summary>
public class CodeSearchFile
{
	/// <summary>The package this file belongs to, e.g. "facepunch.sandbox".</summary>
	public string Ident { get; set; }

	/// <summary>Package-relative path, e.g. "code/Player.cs".</summary>
	public string Path { get; set; }

	public string FileName { get; set; }

	/// <summary>The package's type name, e.g. "game" or "library".</summary>
	public string PackageType { get; set; }

	/// <summary>Which part of the package the file belongs to: "Editor", "UnitTest" or "Game".</summary>
	public string CodeKind { get; set; }

	public long AssetVersionId { get; set; }

	/// <summary>The full source text of the file.</summary>
	public string Code { get; set; }
}
