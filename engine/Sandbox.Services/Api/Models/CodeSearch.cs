namespace Sandbox.Services;

/// <summary>
/// A page of code-search results from listed packages. Private code requires authenticated admin mode.
/// </summary>
public class CodeSearchResult
{
	/// <summary>
	/// Total number of matching files across all pages.
	/// </summary>
	public long TotalCount { get; set; }

	/// <summary>
	/// Matching source files for this page.
	/// </summary>
	public List<CodeSearchFile> Files { get; set; } = new();
}

/// <summary>
/// One matching source file.
/// </summary>
public class CodeSearchFile
{
	/// <summary>
	/// The package this file belongs to, e.g. "facepunch.sandbox".
	/// </summary>
	public string Ident { get; set; }

	/// <summary>
	/// Package-relative path, e.g. "code/Player.cs".
	/// </summary>
	public string Path { get; set; }

	/// <summary>
	/// The source file's name.
	/// </summary>
	public string FileName { get; set; }

	/// <summary>
	/// The package's type name, e.g. "game" or "library".
	/// </summary>
	public string PackageType { get; set; }

	/// <summary>
	/// Which part of the package the file belongs to: "Editor", "UnitTest" or "Game".
	/// </summary>
	public string CodeKind { get; set; }

	/// <summary>
	/// The indexed package revision.
	/// </summary>
	public long AssetVersionId { get; set; }

	/// <summary>
	/// True when the package has not made its source public.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never )]
	public bool IsPrivate { get; set; }

	/// <summary>
	/// The full source text of the file.
	/// </summary>
	public string Code { get; set; }
}
