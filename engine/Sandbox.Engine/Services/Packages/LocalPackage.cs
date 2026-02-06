
namespace Sandbox;

/// <summary>
/// A mock package, this is a package wrapped around a <see cref="Project"/>
/// </summary>
internal sealed class LocalPackage : Package
{
	public Project Project { get; set; }

	/// <summary>
	/// The path to this project's assets
	/// </summary>
	public string ContentPath => Project.GetAssetsPath();

	/// <summary>
	/// The path to this project's code
	/// </summary>
	public string CodePath => Project.GetCodePath();

	/// <summary>
	/// The path to this project's localization files
	/// </summary>
	public string LocalizationPath => Project.GetLocalizationPath();

	/// <summary>
	/// True if this package is shipped with the game - so everyone should have it
	/// </summary>
	public bool IsBuiltIn => Project.IsBuiltIn;

	/// <summary>
	/// A filesystem into which compiled assemblies are written
	/// </summary>
	public MemoryFileSystem AssemblyFileSystem => Project.AssemblyFileSystem;


	public Compiler Compiler => Project.Compiler;
	public Compiler EditorCompiler => Project.EditorCompiler;

	public LocalPackage( Project project )
	{
		this.Project = project;
	}

	/// <summary>
	/// Retrieve meta directly from the project instead of the package
	/// </summary>
	public override T GetMeta<T>( string keyName, T defaultValue = default( T ) )
	{
		return Project.Config.GetMetaOrDefault( keyName, defaultValue );
	}

	/// <summary>
	/// Return true if we need the "base" package including. There are a few situations:
	/// 
	/// 1. We're the root gamemode
	/// 
	/// </summary>
	public bool NeedsLocalBasePackage()
	{
		if ( Project.Config.Type != "game" )
			return false;

		return true;
	}

	internal override IEnumerable<string> EnumeratePackageReferences()
	{
		if ( Project.Config.Type == "game" && !Project.IsBuiltIn )
		{
			foreach ( var library in Project.Libraries.Where( x => x.HasCodePath() || x.HasEditorPath() ) )
			{
				yield return library.Package.FullIdent;
			}
		}

		foreach ( var package in base.EnumeratePackageReferences() )
		{
			yield return package;
		}
	}

	internal void UpdateFromPackage( Package cachedPackage )
	{
		Summary = cachedPackage.Summary;
		Description = cachedPackage.Description;
		Thumb = cachedPackage.Thumb;
		ThumbWide = cachedPackage.ThumbWide;
		ThumbTall = cachedPackage.ThumbTall;
		VideoThumb = cachedPackage.VideoThumb;
		Screenshots = cachedPackage.Screenshots;
		Org = cachedPackage.Org;
	}
}
