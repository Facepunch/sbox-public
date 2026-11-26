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
	/// True if this package is shipped with the game - so everyone should have it
	/// </summary>
	public bool IsBuiltIn => Project.IsBuiltIn;

	/// <summary>
	/// A filesystem into which compiled assemblies are written
	/// </summary>
	public MemoryFileSystem AssemblyFileSystem => Project.AssemblyFileSystem;


	public Compiler Compiler => Project.Compiler;
	public Compiler EditorCompiler => Project.EditorCompiler;

	/// <summary>
	/// Create the local package and read its presentation from the project.
	/// </summary>
	public LocalPackage( Project project )
	{
		this.Project = project;
		ReadPresentation();
	}

	/// <summary>
	/// Retrieve meta directly from the project instead of the package
	/// </summary>
	public override T GetMeta<T>( string keyName, T defaultValue = default( T ) )
	{
		return Project.Config.GetMetaOrDefault( keyName, defaultValue );
	}

	internal override IEnumerable<string> EnumerateInstallDependencies()
	{
		if ( Project.Config.Type is "game" or "addon" )
		{
			foreach ( var library in Project.Libraries.Where( x => x.HasCodePath() || x.HasEditorPath() ) )
			{
				yield return library.Package.FullIdent;
			}
		}

		foreach ( var package in base.EnumerateInstallDependencies() )
		{
			yield return package;
		}
	}

	/// <summary>
	/// Read available files from the project's presentation folder, without borrowing cloud metadata.
	/// </summary>
	void ReadPresentation()
	{
		var files = Project.FileSystem;
		Summary = null;
		Description = null;

		if ( files?.FileExists( "presentation/description.md" ) == true )
		{
			var text = files.ReadAllText( "presentation/description.md" ).Trim();
			Description = System.Net.WebUtility.HtmlEncode( text ).ReplaceLineEndings( "<br>" );
			Summary = text.Split( '\n', 2 )[0].TrimEnd( '\r' );
		}

		Thumb = PresentationImage( "presentation/thumb-square.png" );
		ThumbWide = PresentationImage( "presentation/thumb-wide.png" ) ?? Thumb;
		ThumbTall = PresentationImage( "presentation/thumb-tall.png" ) ?? Thumb;
		VideoThumb = null;
		Screenshots = (files?.FindFile( "presentation/screenshots" ) ?? [])
			.Where( x => System.IO.Path.GetExtension( x ).ToLowerInvariant() is ".png" or ".jpg" or ".gif" or ".webp" or ".svg" )
			.Order( StringComparer.OrdinalIgnoreCase )
			.Select( x => PresentationImage( $"presentation/screenshots/{x}" ) )
			.Select( x => new Screenshot { Url = x, Thumb = x } )
			.ToArray();
	}

	/// <summary>
	/// Qualify artwork by project so two addons can use the same image filenames.
	/// </summary>
	string PresentationImage( string path )
	{
		if ( Project.FileSystem?.FileExists( path ) != true ) return null;

		return $"project:{Project.Config.FullIdent}/{path}";
	}
}
