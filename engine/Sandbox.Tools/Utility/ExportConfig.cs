using System;

namespace Editor;

public class ExportConfig
{
	public Project Project { get; set; }

	/// <summary>
	/// Assemblies can reference asset packages. This is a list
	/// of packages that the compiled code references.
	/// </summary>
	public HashSet<string> CodePackages = new();

	/// <summary>
	/// If the compile process created any assemblies
	/// </summary>
	public Dictionary<string, object> AssemblyFiles { get; set; }

	/// <summary>
	/// Where are we putting the exported build?
	/// </summary>
	[Title( "Export Directory" ), Editor( "folder" )]
	public string TargetDir { get; set; }

	/// <summary>
	/// The target .exe name for this export
	/// </summary>
	[Title( "Executable Name" )]
	public string ExecutableName { get; set; }

	/// <summary>
	/// The icon for the target .exe
	/// </summary>
	[Title( "Executable Icon" )] // can't make this a .ico picker yet, we should really just convert from png ourselves though
	public string TargetIcon { get; set; }

	/// <summary>
	/// The image shown while the game starts and loads: a PNG or JPG file, or a texture. A 1920x1080 image fills a 16:9 window.
	/// </summary>
	[Title( "Startup Image" )]
	public string StartupImage { get; set; }

	/// <summary>
	/// The Steam AppID for the target .exe
	/// </summary>
	[Title( "Steam App ID" )]
	public uint AppId { get; set; }

	/// <summary>
	/// Game's build date
	/// </summary>
	public DateTime BuildDate { get; set; } = DateTime.Now;
}
