using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sandbox.SolutionGenerator;

public class VSCodeSettings
{
	[JsonPropertyName( "files.associations" )]
	public Dictionary<string, string> FilesAssociations { get; set; } = [];

	[JsonPropertyName( "slang.additionalSearchPaths" )]
	public string[] SlangIncludePaths { get; set; } = [];

	[JsonPropertyName( "slang.predefinedMacros" )]
	public string[] SlangDefines { get; set; } = [];

	[JsonPropertyName( "slang.workspaceFlavor" )]
	public string SlangWorkspaceFlavor { get; set; } = "vfx";
}
