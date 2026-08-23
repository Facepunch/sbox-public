namespace Facepunch.Native;

/// <summary>
/// Settings for one build configuration of one module. Values are MSBuild property values, written
/// into the .vcxproj as-is.
/// </summary>
public sealed class Config( string name )
{
	public string Name = name;

	/// <summary>Lowercase name, used by tools that take a config argument (schemacompiler).</summary>
	public string Lower => Name.ToLowerInvariant();

	public List<string> Defines = [];
	public List<string> Includes = [];
	public List<string> Options = [];
	public List<string> Warnings = [];
	public List<string> ForceIncludes = [];

	public List<string> LinkOptions = [];
	public List<string> LinkLibs = [];
	public List<string> LibDirs = [];
	public List<string> IgnoreLibs = [];

	public Dictionary<string, string> Cl = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, string> Link = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, string> Lib = new( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, string> Rc = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Project-level properties (OutDir, IntDir, LinkIncremental...).</summary>
	public Dictionary<string, string> Properties = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Properties in the Label="Configuration" group (ConfigurationType, PlatformToolset...).</summary>
	public Dictionary<string, string> Settings = new( StringComparer.OrdinalIgnoreCase );

	public BuildEvent PreBuild = new(), PreLink = new(), PostBuild = new();

	public void Define( params string[] values ) => Defines.AddRange( values );
	public void Include( params string[] values ) => Includes.AddRange( values );
	public void Option( params string[] values ) => Options.AddRange( values );
	public void NoWarn( params string[] values ) => Warnings.AddRange( values );
	public void LinkLib( params string[] values ) => LinkLibs.AddRange( values );
	public void LibDir( params string[] values ) => LibDirs.AddRange( values );
	public void ForceInclude( params string[] values ) => ForceIncludes.AddRange( values );

	public Config Clone() => new( Name )
	{
		Defines = [.. Defines],
		Includes = [.. Includes],
		Options = [.. Options],
		Warnings = [.. Warnings],
		ForceIncludes = [.. ForceIncludes],
		LinkOptions = [.. LinkOptions],
		LinkLibs = [.. LinkLibs],
		LibDirs = [.. LibDirs],
		IgnoreLibs = [.. IgnoreLibs],
		Cl = new( Cl, StringComparer.OrdinalIgnoreCase ),
		Link = new( Link, StringComparer.OrdinalIgnoreCase ),
		Lib = new( Lib, StringComparer.OrdinalIgnoreCase ),
		Rc = new( Rc, StringComparer.OrdinalIgnoreCase ),
		Properties = new( Properties, StringComparer.OrdinalIgnoreCase ),
		Settings = new( Settings, StringComparer.OrdinalIgnoreCase ),
		PreBuild = PreBuild.Clone(),
		PreLink = PreLink.Clone(),
		PostBuild = PostBuild.Clone()
	};
}

public sealed class BuildEvent
{
	public List<string> Commands = [];
	public string Description;

	public void Add( string command ) => Commands.Add( command );
	public BuildEvent Clone() => new() { Commands = [.. Commands], Description = Description };
}
