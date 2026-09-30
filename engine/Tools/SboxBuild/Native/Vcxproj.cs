using System.Security.Cryptography;
using System.Text;

namespace Facepunch.Native;

/// <summary>
/// Writes a module out as a .vcxproj (+ .filters).
/// </summary>
public static class Vcxproj
{
	public const string Xmlns = "http://schemas.microsoft.com/developer/msbuild/2003";

	public static string FileName( Module module ) => $"{module.Name}_{Paths.Platform}.vcxproj";

	/// <summary>Path to the project file, relative to src/.</summary>
	public static string ProjectPath( Module module ) => $"{module.Dir}/{FileName( module )}";

	public static Guid ProjectGuid( Module module )
	{
		var hash = MD5.HashData( Encoding.UTF8.GetBytes( $"sbox/{module.Name}/{Paths.Platform}" ) );
		return new Guid( hash );
	}

	public static void Write( Module module, Options options )
	{
		var xml = new Xml();
		xml.Line( @"<?xml version=""1.0"" encoding=""utf-8""?>" );
		xml.Open( $@"<Project DefaultTargets=""Build"" ToolsVersion=""4.0"" xmlns=""{Xmlns}"">" );

		xml.Open( @"<ItemGroup Label=""ProjectConfigurations"">" );
		foreach ( var config in module.Configs )
		{
			xml.Open( $@"<ProjectConfiguration Include=""{config.Name}|x64"">" );
			xml.Element( "Configuration", config.Name );
			xml.Element( "Platform", "x64" );
			xml.Close( "</ProjectConfiguration>" );
		}
		xml.Close( "</ItemGroup>" );

		xml.Open( @"<PropertyGroup Label=""Globals"">" );
		xml.Element( "ProjectName", module.Name );
		xml.Element( "ProjectGuid", $"{{{ProjectGuid( module ).ToString( "D" ).ToUpperInvariant()}}}" );
		xml.Element( "IgnoreWarnCompileDuplicatedFilename", "true" );
		xml.Element( "WindowsTargetPlatformVersion", Win64.WindowsSdk );
		xml.Close( "</PropertyGroup>" );

		xml.Line( @"<Import Project=""$(VCTargetsPath)\Microsoft.Cpp.Default.props"" />" );

		foreach ( var config in module.Configs )
		{
			xml.Open( $@"<PropertyGroup {Condition( config )} Label=""Configuration"">" );
			foreach ( var (key, value) in config.Settings ) xml.Element( key, value );
			xml.Close( "</PropertyGroup>" );
		}

		xml.Line( @"<Import Project=""$(VCTargetsPath)\Microsoft.Cpp.props"" />" );
		xml.Line( @"<ImportGroup Label=""ExtensionSettings"" />" );

		foreach ( var config in module.Configs )
		{
			xml.Open( $@"<ImportGroup {Condition( config )} Label=""PropertySheets"">" );
			xml.Line( @"<Import Project=""$(UserRootDir)\Microsoft.Cpp.$(Platform).user.props"" Condition=""exists('$(UserRootDir)\Microsoft.Cpp.$(Platform).user.props')"" Label=""LocalAppDataPlatform"" />" );
			xml.Close( "</ImportGroup>" );
		}

		xml.Line( @"<PropertyGroup Label=""UserMacros"" />" );

		xml.Open( "<PropertyGroup>" );
		foreach ( var config in module.Configs )
			foreach ( var (key, value) in config.Properties )
				xml.Element( key, value, Condition( config ) );
		xml.Close( "</PropertyGroup>" );

		foreach ( var config in module.Configs ) WriteConfig( xml, module, config );

		WriteFiles( xml, module );

		xml.Line( @"<Import Project=""$(VCTargetsPath)\Microsoft.Cpp.targets"" />" );
		xml.Line( @"<ImportGroup Label=""ExtensionTargets"" />" );

		WriteStaleCheck( xml, module );
		xml.Close( "</Project>" );

		Write( Path.Combine( Paths.SrcDir, ProjectPath( module ) ), xml.ToString() );
		WriteFilters( module );
	}

	/// <summary>
	/// Fails the build when the project is older than the definition it came from, so a stale project
	/// cannot be built by accident. MSBuild skips the target entirely while the sentinel is newer, so
	/// there is no cost in the normal case.
	/// </summary>
	private static void WriteStaleCheck( Xml xml, Module module )
	{
		var definition = Registry.Definition( module );
		if ( definition is null ) return;

		var name = Path.GetFileName( definition );
		xml.Open( $@"<Target Name=""CheckProjectDefinition"" BeforeTargets=""ClCompile"" Inputs=""{name}"" Outputs=""{Registry.Sentinel( module )}"">" );
		xml.Line( $@"<Error Text=""{name} changed. Run Developer-GenerateSolutions.bat"" />" );
		xml.Close( "</Target>" );
	}

	private static void WriteConfig( Xml xml, Module module, Config config )
	{
		xml.Open( $"<ItemDefinitionGroup {Condition( config )}>" );

		Event( xml, "PreBuildEvent", config.PreBuild );

		xml.Open( "<ClCompile>" );
		if ( config.Options.Count > 0 ) xml.Element( "AdditionalOptions", string.Join( ' ', config.Options ) + " %(AdditionalOptions)" );
		xml.Element( "AdditionalIncludeDirectories", Join( config.Includes.Select( x => Paths.Relative( module.Dir, x ) ) ) );
		xml.Element( "PreprocessorDefinitions", Join( config.Defines ) );
		if ( config.Warnings.Count > 0 ) xml.Element( "DisableSpecificWarnings", Join( config.Warnings ) );
		if ( config.ForceIncludes.Count > 0 ) xml.Element( "ForcedIncludeFiles", Join( config.ForceIncludes ) );
		foreach ( var (key, value) in config.Cl ) xml.Element( key, value );
		xml.Close( "</ClCompile>" );

		if ( config.Rc.Count > 0 )
		{
			xml.Open( "<ResourceCompile>" );
			foreach ( var (key, value) in config.Rc ) xml.Element( key, value );
			xml.Close( "</ResourceCompile>" );
		}

		if ( module.Kind == ModuleKind.Lib )
		{
			xml.Open( "<Lib>" );
			foreach ( var (key, value) in config.Lib ) xml.Element( key, value );
			xml.Close( "</Lib>" );
		}
		else
		{
			xml.Open( "<Link>" );
			if ( config.LinkOptions.Count > 0 ) xml.Element( "AdditionalOptions", string.Join( ' ', config.LinkOptions ) + " %(AdditionalOptions)" );
			if ( config.LinkLibs.Count > 0 ) xml.Element( "AdditionalDependencies", Join( config.LinkLibs ) + ";%(AdditionalDependencies)" );
			if ( config.LibDirs.Count > 0 ) xml.Element( "AdditionalLibraryDirectories", Join( config.LibDirs.Select( x => x.StartsWith( '$' ) ? x : Paths.Relative( module.Dir, x ) ) ) );
			if ( config.IgnoreLibs.Count > 0 ) xml.Element( "IgnoreSpecificDefaultLibraries", Join( config.IgnoreLibs ) );
			foreach ( var (key, value) in config.Link ) xml.Element( key, value );
			xml.Close( "</Link>" );
		}

		Event( xml, "PreLinkEvent", config.PreLink );
		Event( xml, "PostBuildEvent", config.PostBuild );

		xml.Close( "</ItemDefinitionGroup>" );
	}

	private static void Event( Xml xml, string name, BuildEvent buildEvent )
	{
		if ( buildEvent.Commands.Count == 0 ) return;

		xml.Open( $"<{name}>" );
		if ( buildEvent.Description is not null ) xml.Element( "Message", buildEvent.Description );
		xml.Element( "Command", string.Concat( buildEvent.Commands.Select( x => x + "\r\n" ) ) );
		xml.Close( $"</{name}>" );
	}

	private static void WriteFiles( Xml xml, Module module )
	{
		if ( module.Libraries.Count > 0 )
		{
			xml.Open( "<ItemGroup>" );
			foreach ( var library in module.Libraries )
				xml.Line( $@"<Library Include=""{Paths.Relative( module.Dir, library )}"" />" );
			xml.Close( "</ItemGroup>" );
		}

		foreach ( var group in module.ResolvedFiles.GroupBy( f => f.Kind ) )
		{
			xml.Open( "<ItemGroup>" );

			// The file that creates the precompiled header goes first, everything else needs it to exist.
			foreach ( var file in group.OrderByDescending( f => f.CreatePch ) )
			{
				var element = group.Key switch
				{
					FileKind.Compile => "ClCompile",
					FileKind.Include => "ClInclude",
					FileKind.Resource => "ResourceCompile",
					FileKind.Natvis => "Natvis",
					_ => "None"
				};

				var path = Paths.Relative( module.Dir, file.Path );
				var metadata = new List<(string Key, string Value, string Condition)>();

				if ( file.CreatePch ) metadata.Add( ("PrecompiledHeader", "Create", null) );
				else if ( file.NoPch || group.Key != FileKind.Compile && module.PrecompiledHeader is not null )
				{
					if ( module.PrecompiledHeader is not null )
					{
						metadata.Add( ("PrecompiledHeader", "NotUsing", null) );
						metadata.Add( ("ForcedIncludeFiles", "", null) );
					}
				}

				if ( file.CompileAs is not null ) metadata.Add( ("CompileAs", file.CompileAs, null) );
				if ( file.Msvc.Count > 0 ) metadata.Add( ("AdditionalOptions", string.Join( ' ', file.Msvc ) + " %(AdditionalOptions)", null) );
				if ( file.Defines.Count > 0 ) metadata.Add( ("PreprocessorDefinitions", Join( file.Defines ) + ";%(PreprocessorDefinitions)", null) );
				if ( file.Includes.Count > 0 ) metadata.Add( ("AdditionalIncludeDirectories", Join( file.Includes ) + ";%(AdditionalIncludeDirectories)", null) );

				foreach ( var configName in file.ExcludeFrom )
					metadata.Add( ("ExcludedFromBuild", "true", Condition( configName )) );

				if ( file.Build is not null )
				{
					WriteCustomBuild( xml, module, file );
					continue;
				}

				if ( metadata.Count == 0 )
				{
					xml.Line( $@"<{element} Include=""{path}"" />" );
					continue;
				}

				xml.Open( $@"<{element} Include=""{path}"">" );
				foreach ( var (key, value, condition) in metadata ) xml.Element( key, value, condition );
				xml.Close( $"</{element}>" );
			}

			xml.Close( "</ItemGroup>" );
		}
	}

	private static void WriteCustomBuild( Xml xml, Module module, SourceFile file )
	{
		var path = Paths.Relative( module.Dir, file.Path );
		xml.Open( $@"<CustomBuild Include=""{path}"">" );

		foreach ( var config in module.Configs )
		{
			var condition = Condition( config );
			var build = file.Build;
			if ( build.Message is not null ) xml.Element( "Message", Expand( build.Message, config ), condition );
			xml.Element( "Command", Expand( build.Command, config ), condition );
			if ( build.Inputs.Count > 0 ) xml.Element( "AdditionalInputs", Expand( Join( build.Inputs ), config ), condition );
			xml.Element( "Outputs", Expand( Join( build.Outputs ), config ), condition );
			if ( build.PotentialOutputs.Count > 0 ) xml.Element( "PotentialOutputs", Expand( Join( build.PotentialOutputs ), config ), condition );
		}

		xml.Close( "</CustomBuild>" );
	}

	private static void WriteFilters( Module module )
	{
		var xml = new Xml();
		xml.Line( @"<?xml version=""1.0"" encoding=""utf-8""?>" );
		xml.Open( $@"<Project ToolsVersion=""4.0"" xmlns=""{Xmlns}"">" );

		var filters = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
		var assigned = new List<(SourceFile File, string Filter)>();

		foreach ( var file in module.ResolvedFiles )
		{
			var filter = Filter( module, file );
			assigned.Add( (file, filter) );

			var parts = filter.Split( '\\' );
			for ( int i = 1; i <= parts.Length; i++ ) filters.Add( string.Join( '\\', parts[..i] ) );
		}

		xml.Open( "<ItemGroup>" );
		foreach ( var filter in filters )
		{
			var hash = MD5.HashData( Encoding.UTF8.GetBytes( filter ) );
			xml.Open( $@"<Filter Include=""{filter}"">" );
			xml.Element( "UniqueIdentifier", $"{{{new Guid( hash ).ToString( "D" ).ToUpperInvariant()}}}" );
			xml.Close( "</Filter>" );
		}
		xml.Close( "</ItemGroup>" );

		xml.Open( "<ItemGroup>" );
		foreach ( var (file, filter) in assigned )
		{
			var element = file.Kind switch
			{
				FileKind.Compile => "ClCompile",
				FileKind.Include => "ClInclude",
				FileKind.Resource => "ResourceCompile",
				_ => "None"
			};

			xml.Open( $@"<{element} Include=""{Paths.Relative( module.Dir, file.Path )}"">" );
			xml.Element( "Filter", filter );
			xml.Close( $"</{element}>" );
		}
		xml.Close( "</ItemGroup>" );
		xml.Close( "</Project>" );

		Write( Path.Combine( Paths.SrcDir, $"{module.Dir}/{FileName( module )}.filters" ), xml.ToString() );
	}

	private static string Filter( Module module, SourceFile file )
	{
		var dir = Path.GetDirectoryName( file.Path )?.Replace( '\\', '/' ) ?? "";
		var group = file.Kind == FileKind.Include ? "Header Files" : "Source Files";

		if ( dir.Equals( module.Dir, StringComparison.OrdinalIgnoreCase ) ) return group;
		if ( dir.StartsWith( module.Dir + "/", StringComparison.OrdinalIgnoreCase ) )
			return $"{group}\\{dir[(module.Dir.Length + 1)..].Replace( '/', '\\' )}";

		return $"{group}\\External";
	}

	public static string Condition( Config config ) => Condition( config.Name );

	public static string Condition( string configName ) =>
		$@"Condition=""'$(Configuration)|$(Platform)'=='{configName}|x64'""";

	/// <summary>%config% is the lowercase config name, for tools that take a config argument.</summary>
	public static string Expand( string value, Config config ) =>
		value?.Replace( "%config%", config.Lower ).Replace( "%Config%", config.Name );

	public static string Join( IEnumerable<string> values ) => string.Join( ';', values.Distinct( StringComparer.OrdinalIgnoreCase ) );

	private static void Write( string path, string content )
	{
		if ( File.Exists( path ) && File.ReadAllText( path ) == content ) return;

		Directory.CreateDirectory( Path.GetDirectoryName( path ) );
		File.WriteAllText( path, content, new UTF8Encoding( true ) );
	}
}

/// <summary>Indent-tracking XML writer. Values are escaped, element names are not.</summary>
public sealed class Xml
{
	private readonly StringBuilder builder = new();
	private int indent;

	public void Line( string text ) => builder.Append( new string( ' ', indent * 2 ) ).Append( text ).Append( "\r\n" );

	public void Open( string text )
	{
		Line( text );
		indent++;
	}

	public void Close( string text )
	{
		indent--;
		Line( text );
	}

	public void Element( string name, string value, string condition = null )
	{
		var attribute = condition is null ? "" : $" {condition}";
		if ( string.IsNullOrEmpty( value ) ) Line( $"<{name}{attribute} />" );
		else Line( $"<{name}{attribute}>{Escape( value )}</{name}>" );
	}

	public static string Escape( string value ) => value
		.Replace( "&", "&amp;" )
		.Replace( "<", "&lt;" )
		.Replace( ">", "&gt;" )
		.Replace( "\"", "&quot;" )
		.Replace( "\r", "&#x0D;" )
		.Replace( "\n", "&#x0A;" );

	public override string ToString() => builder.ToString();
}
