using System.Text;

namespace Facepunch.Native;

/// <summary>
/// Schema compiler integration. Writes the .projectinfo file that schemacompiler_parent reads, adds
/// the generated .gen_cpp files to the project, and adds the build step that produces them.
/// </summary>
public static class SchemaCompiler
{
	public const int InfoVersion = 1;
	public const string GeneratedRoot = "_generated_code";

	/// <summary>The tool a schematised module shells out to, and the child it spawns to do the work.</summary>
	public const string Tool = "schemacompiler_parent";
	public const string ToolChild = "schemacompiler";

	/// <summary>
	/// A schematised module runs the schema compiler as a build step, so it waits for the tools. Without this
	/// a build that does both can start the step while the executable is still being written.
	/// </summary>
	public static void Order( List<Module> modules )
	{
		var tools = modules.Where( m => m.Name is Tool or ToolChild ).ToList();
		if ( tools.Count == 0 ) return;

		// The tools and everything they link cannot wait for the tools.
		var building = Solution.WithDependencies( tools ).ToHashSet();

		foreach ( var module in modules )
		{
			if ( !module.Schema.Any || building.Contains( module ) ) continue;

			foreach ( var tool in tools )
				if ( !module.Dependencies.Contains( tool ) ) module.Dependencies.Add( tool );
		}
	}

	// The .schema extension is checked by schemacompiler.exe.
	public static string InfoPath( Module module ) => $"{module.Dir}/{module.Name}.schema";

	/// <summary>Schema paths are written as /-prefixed src paths, like any other module path.</summary>
	private static List<string> Resolved( Module module, List<string> paths ) =>
		[.. paths.Select( x => Paths.Resolve( module.Dir, x ) )];

	public static void Apply( Module module, Options options )
	{
		if ( !module.Schema.Any ) return;

		module.Schema.Files = Resolved( module, module.Schema.Files );
		module.Schema.PreIncludes = Resolved( module, module.Schema.PreIncludes );

		foreach ( var config in module.Configs )
		{
			foreach ( var file in module.Schema.Files )
			{
				// These use the PCH: the schema compiler emits an #include of it, and only /Yu makes that
				// resolve from the generated code folder.
				module.ResolvedFiles.Add( new SourceFile
				{
					Path = Generated( module, config, file ),
					Kind = FileKind.Compile,
					ExcludeFrom = [.. module.Configs.Where( c => c != config ).Select( c => c.Name )]
				} );
			}

			module.ResolvedFiles.Add( new SourceFile
			{
				Path = Anchor( module, config ),
				Kind = FileKind.Compile,
				NoPch = true,
				ExcludeFrom = [.. module.Configs.Where( c => c != config ).Select( c => c.Name )]
			} );
		}

		var info = InfoPath( module );
		var exe = Paths.Relative( module.Dir, $"devtools/bin/{Paths.Platform}/schemacompiler_parent.exe" );

		module.ResolvedFiles.Add( new SourceFile
		{
			Path = info,
			Kind = FileKind.None,
			Build = new CustomBuild
			{
				Message = $"Schema Compiler: {module.Name} [%Config%]",
				Command = $@"{exe} -schema_proj {Path.GetFileName( info )} -config %config% -sentinel ""$(IntermediateOutputPath)\schema_sentinel.txt""",
				Inputs =
				[
					.. module.Schema.Files.Select( f => Paths.Relative( module.Dir, f ) ),
					Vcxproj.FileName( module )
				],
				Outputs = [@"$(IntermediateOutputPath)\schema_sentinel.txt"],
				PotentialOutputs =
				[
					.. module.Schema.Files.Select( f => Paths.Relative( module.Dir, Generated( module, module.DebugConfig, f ) ).Replace( '\\', '/' ) )
				]
			}
		} );
	}

	public static void WriteInfo( Module module )
	{
		if ( !module.Schema.Any ) return;

		var kv = new Kv();
		kv.Open( "schema_project" );
		kv.Value( "file_version", InfoVersion.ToString() );
		kv.Value( "project_name", module.Name );
		kv.Value( "project_path", Paths.Absolute( module.Dir ).Replace( '\\', '/' ) );
		kv.Value( "src_dir_abs_path", Paths.SrcDir.Replace( '\\', '/' ) );
		kv.Value( "platform_name", Paths.Platform );
		kv.Value( "platform_name_lower", Paths.Platform );
		kv.Value( "compiler_name", "VS2026" );
		kv.Value( "compiler_name_lower", "vs2026" );
		kv.Value( "touch_unchanged_outputs", "0" );

		kv.Open( "configs" );
		foreach ( var config in module.Configs ) WriteConfig( kv, module, config );
		kv.Close();

		kv.Value( "schemacompiler_config_path", Paths.Relative( module.Dir, "devtools/bin" ).Replace( '\\', '/' ) );
		kv.Close();

		var path = Path.Combine( Paths.SrcDir, InfoPath( module ) );
		var content = kv.ToString();
		if ( File.Exists( path ) && File.ReadAllText( path ) == content ) return;

		File.WriteAllText( path, content, new UTF8Encoding( false ) );
	}

	private static void WriteConfig( Kv kv, Module module, Config config )
	{
		kv.Open( config.Name );

		kv.Open( "defines" );
		int index = 0;
		foreach ( var define in config.Defines.Concat( Architecture ).Distinct() ) kv.Value( $"{index++:000}", define );
		kv.Close();

		kv.Open( "includes" );
		index = 0;
		foreach ( var include in config.Includes.Distinct() ) kv.Value( $"{index++:000}", Paths.Relative( module.Dir, include ).Replace( '\\', '/' ) );
		kv.Close();

		kv.Open( "pchs" );
		if ( module.PrecompiledHeader is not null )
		{
			kv.Open( "0" );
			// Forward slashes: the schema compiler writes this into an #include, where a backslash before
			// a letter is an escape sequence.
			kv.Value( "pch_include_filename", module.PrecompiledHeader.Replace( '\\', '/' ) );
			kv.Value( "pch_creator_filename", Path.GetFileName( Path.ChangeExtension( module.PrecompiledHeader, ".cpp" ) ) );
			kv.Close();
		}
		kv.Close();

		kv.Open( "schema_files" );
		index = 0;
		foreach ( var file in module.Schema.Files )
		{
			kv.Open( $"{index++:000}" );
			kv.Value( "file", Paths.Relative( module.Dir, file ).Replace( '\\', '/' ) );
			kv.Value( "generated_file", Paths.Relative( module.Dir, Generated( module, config, file ) ).Replace( '\\', '/' ) );
			kv.Value( "symbol_name", Symbol( file ) );
			kv.Value( "pch_file_index", module.PrecompiledHeader is null ? "-1" : "0" );
			kv.Value( "is_cpp", file.EndsWith( ".cpp", StringComparison.OrdinalIgnoreCase ) ? "1" : "0" );
			kv.Close();
		}
		kv.Close();

		kv.Value( "schemacompiler_anchor_path", Paths.Relative( module.Dir, Anchor( module, config ) ).Replace( '\\', '/' ) );
		kv.Value( "schemacompiler_pre_include_files", List( module.Schema.PreIncludes.Select( f => Paths.Relative( module.Dir, f ).Replace( '\\', '/' ) ) ) );
		kv.Value( "schemacompiler_anchor_includes", List( module.Schema.AnchorIncludes ) );
		kv.Value( "schemacompiler_global_types", List( module.Schema.GlobalTypes ) );
		kv.Value( "schemacompiler_omit_types", List( module.Schema.OmitTypes ) );

		kv.Close();
	}

	/// <summary>
	/// The schema compiler runs its own preprocessor, which does not define what the platform compiler
	/// does. Without these it takes different #if branches than MSVC and disagrees about class members.
	/// </summary>
	private static readonly string[] Architecture =
	[
		"__amd64__=1", "__amd64=1", "__x86_64__=1", "__x86_64=1", "_M_X64=1", "_M_AMD64=1"
	];

	// No leading separator: the schema compiler compares the whole value against "*" to mean every type.
	private static string List( IEnumerable<string> values ) => string.Join( ';', values.Distinct() );

	private static string Symbol( string file ) => Path.GetFileName( file ).Replace( '.', '_' );

	private static string Generated( Module module, Config config, string file ) =>
		$"{GeneratedRoot}/{module.Name}/{Paths.Platform}/{config.Lower}/{Symbol( file )}_schema.gen_cpp";

	private static string Anchor( Module module, Config config ) =>
		$"{GeneratedRoot}/{module.Name}/{Paths.Platform}/{config.Lower}/{module.Name}_schema_anchor.gen_cpp";
}

/// <summary>Minimal KeyValues writer.</summary>
public sealed class Kv
{
	private readonly StringBuilder builder = new();
	private int indent;

	public void Open( string name )
	{
		Line( $"\"{name}\"" );
		Line( "{" );
		indent++;
	}

	public void Close()
	{
		indent--;
		Line( "}" );
	}

	public void Value( string key, string value ) => Line( $"\"{key}\"\t\t\"{value}\"" );

	private void Line( string text ) => builder.Append( new string( '\t', indent ) ).Append( text ).Append( '\n' );

	public override string ToString() => builder.ToString();
}
