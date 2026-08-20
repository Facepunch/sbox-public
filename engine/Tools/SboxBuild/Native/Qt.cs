namespace Facepunch.Native;

/// <summary>
/// Qt support. A module sets Qt = true and the headers that need moc are found by looking for Q_OBJECT,
/// rather than being listed by hand.
/// </summary>
public static class Qt
{
	public const string Root = "thirdparty/qt5";
	public const string Generated = "obj/moc";

	private static readonly string[] Defines =
	[
		"UNICODE", "WIN32", "QT_LARGEFILE_SUPPORT", "QT_DLL", "QT_NO_DEBUG", "QT_GUI_LIB", "QT_CORE_LIB",
		"QT_THREAD_SUPPORT", "QT_BUILD", "VALVE_QT5"
	];

	private static readonly string[] Includes = ["QtCore", "QtGui", "", "ActiveQt"];

	public static void Apply( Module module )
	{
		if ( !module.Qt ) return;

		var moc = Paths.Relative( module.Dir, $"{Root}/bin/{Paths.Platform}/moc.exe" );
		var arguments = string.Join( ' ', Defines.Select( d => $"-D{d}" ) )
			+ " " + string.Join( ' ', Includes.Select( i => $"-I;{Paths.Relative( module.Dir, $"{Root}/include/{i}".TrimEnd( '/' ) )};" ) )
			+ " -I;.";

		// Straight onto the configurations: the module's own list was folded into them by Finish().
		foreach ( var config in module.Configs ) config.Include( $"{module.Dir}/{Generated}" );
		module.ReleaseConfig.Define( "QT_NO_DEBUG" );

		// .ui files become ui/ui_<name>.h next to the project, which is how sources include them.
		foreach ( var form in module.ResolvedFiles.Where( f => f.Path.EndsWith( ".ui", StringComparison.OrdinalIgnoreCase ) ).ToList() )
		{
			var name = Path.GetFileNameWithoutExtension( form.Path );
			var uic = Paths.Relative( module.Dir, $"{Root}/bin/{Paths.Platform}/uic.exe" );
			var output = @"$(ProjectDir)ui\ui_" + name + ".h";

			form.Kind = FileKind.None;
			form.Build = new CustomBuild
			{
				Message = $"Qt uic: {Path.GetFileName( form.Path )}",
				Command = $"{uic} %(FullPath) -o {output}",
				Outputs = [output]
			};
		}

		// .qrc files become a compiled resource source.
		foreach ( var resource in module.ResolvedFiles.Where( f => f.Path.EndsWith( ".qrc", StringComparison.OrdinalIgnoreCase ) ).ToList() )
		{
			var name = Path.GetFileNameWithoutExtension( resource.Path );
			var rcc = Paths.Relative( module.Dir, $"{Root}/bin/{Paths.Platform}/rcc.exe" );
			var output = "$(ProjectDir)" + Generated.Replace( '/', '\\' ) + @"\qrc_" + name + ".cpp";

			resource.Kind = FileKind.None;
			resource.Build = new CustomBuild
			{
				Message = $"Qt rcc: {Path.GetFileName( resource.Path )}",
				Command = $"{rcc} -no-compress %(FullPath) -o {output}",
				Outputs = [output]
			};

			module.ResolvedFiles.Add( new SourceFile { Path = $"{module.Dir}/{Generated}/qrc_{name}.cpp", Kind = FileKind.Compile, NoPch = true } );
		}

		// A source declaring Q_OBJECT includes its own .moc, so it stays a normal compiled file and the step
		// hangs off an empty carrier beside the output: a file is either compiled or a build step, not both.
		foreach ( var source in module.ResolvedFiles.Where( f => NeedsSourceMoc( module, f ) ).ToList() )
		{
			var name = Path.GetFileNameWithoutExtension( source.Path );
			var output = $@"$(ProjectDir){Generated.Replace( '/', '\\' )}\{name}.moc";
			var carrier = $"{module.Dir}/{Generated}/{name}.moc.input";

			var path = Paths.Absolute( carrier );
			Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			if ( !File.Exists( path ) ) File.WriteAllText( path, "" );

			module.ResolvedFiles.Add( new SourceFile
			{
				Path = carrier,
				Kind = FileKind.None,
				Build = new CustomBuild
				{
					Message = $"Qt moc: {Path.GetFileName( source.Path )}",
					Command = $"{moc} {arguments} {Paths.Relative( module.Dir, source.Path )} -o {output}",
					Outputs = [output],
					Inputs = [Paths.Relative( module.Dir, source.Path )]
				}
			} );
		}

		foreach ( var header in module.ResolvedFiles.Where( f => NeedsMoc( module, f ) ).ToList() )
		{
			var name = Path.GetFileNameWithoutExtension( header.Path );
			var output = $@"$(ProjectDir){Generated.Replace( '/', '\\' )}\moc_{name}.cpp";

			header.Build = new CustomBuild
			{
				Message = $"Qt moc: {Path.GetFileName( header.Path )}",
				Command = $"{moc} {arguments} %(FullPath) -o {output}",
				Outputs = [output]
			};

			// With the module's precompiled header, which is force included: moc output includes only the
			// header it was generated from, and that header leans on what the rest of the module includes.
			module.ResolvedFiles.Add( new SourceFile
			{
				Path = $"{module.Dir}/{Generated}/moc_{name}.cpp",
				Kind = FileKind.Compile
			} );
		}
	}

	/// <summary>A compiled source declaring Q_OBJECT gets a .moc of its own, which it includes itself.</summary>
	private static bool NeedsSourceMoc( Module module, SourceFile file )
	{
		if ( file.Kind != FileKind.Compile || file.Build is not null ) return false;
		if ( !file.Path.EndsWith( ".cpp", StringComparison.OrdinalIgnoreCase ) ) return false;

		var path = Path.Combine( Paths.SrcDir, file.Path );
		return File.Exists( path ) && File.ReadAllText( path ).Contains( "Q_OBJECT" );
	}

	/// <summary>
	/// A header declaring Q_OBJECT has to go through moc, but only for the module that owns it: its own
	/// directory or its own public folder. A header listed from elsewhere is only there for browsing, and
	/// moc'ing it again would duplicate the meta object in a second module.
	/// </summary>
	private static bool NeedsMoc( Module module, SourceFile file )
	{
		if ( file.Kind != FileKind.Include || file.Build is not null ) return false;
		if ( !Owns( module, file.Path ) ) return false;

		var path = Path.Combine( Paths.SrcDir, file.Path );
		if ( !File.Exists( path ) ) return false;

		return File.ReadAllText( path ).Contains( "Q_OBJECT" );
	}

	/// <summary>
	/// Whether a header belongs to this module rather than being listed from another one. It does if it
	/// sits in the module's own tree, or next to sources the module compiles, or in the public folder of
	/// either. Anything else is listed for browsing and belongs to whoever compiles it.
	/// </summary>
	private static bool Owns( Module module, string path )
	{
		if ( path.StartsWith( $"{module.Dir}/", StringComparison.OrdinalIgnoreCase ) ) return true;

		var directory = Folder( path );
		var owned = module.ResolvedFiles
			.Where( f => f.Kind == FileKind.Compile )
			.Select( f => Folder( f.Path ) )
			.Append( module.Dir )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToList();

		if ( owned.Contains( directory, StringComparer.OrdinalIgnoreCase ) ) return true;

		// public/<name> belongs to the module that compiles <name>
		if ( !directory.StartsWith( "public/", StringComparison.OrdinalIgnoreCase ) ) return false;

		var name = Leaf( directory );
		return name.Equals( module.Name, StringComparison.OrdinalIgnoreCase )
			|| owned.Any( f => Leaf( f ).Equals( name, StringComparison.OrdinalIgnoreCase ) );
	}

	private static string Folder( string path ) => path.Contains( '/' ) ? path[..path.LastIndexOf( '/' )] : "";

	private static string Leaf( string path ) => path.Contains( '/' ) ? path[(path.LastIndexOf( '/' ) + 1)..] : path;
}
