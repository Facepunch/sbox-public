using System;
using System.Collections.Generic;

namespace Sandbox.SolutionGenerator;

public class ProjectInfo
{
	public string Name { get; set; }
	public string PackageIdent { get; set; }
	public string Path { get; set; }
	public string Type { get; set; }
	public string CsprojPath { get; set; }
	public string Guid { get; set; }
	public bool IsEditorProject { get; set; }
	public bool IsUnitTestProject { get; set; }
	public string SandboxProjectFilePath { get; set; }
	public Compiler.Configuration Settings { get; set; }

	public string Folder { get; set; }

	public List<string> References { get; set; } = new List<string>
	{
		"Sandbox.System.dll",
		"Sandbox.Engine.dll",
		"Sandbox.Filesystem.dll",
		"Sandbox.Reflection.dll",
		"Sandbox.Mounting.dll",
		"Microsoft.AspNetCore.Components.dll",
	};

	public List<string> PackageReferences { get; set; } = new List<string>();

	public List<string> GlobalStatic { get; set; } = new List<string>();
	public List<string> GlobalUsing { get; set; } = new List<string>();
	public List<string> IncludeFiles { get; set; } = new();
	public List<string> IgnoreFiles { get; set; } = new();

	public ProjectInfo( string type, string packageIdent, string name, string path, Compiler.Configuration settings )
	{
		Type = type;
		Name = name;
		Path = path;
		PackageIdent = packageIdent;
		CsprojPath = System.IO.Path.Combine( Path, Name + ".csproj" );
		Guid = GetHashFromProjectName( Name );
		Settings = settings;
	}

	private string GetHashFromProjectName( string name )
	{
		var r = new Random( name.FastHash() );
		var guidseed = new byte[16];
		r.NextBytes( guidseed );

		var Guid = new Guid( guidseed );

		return Guid.ToString( "B" ).ToUpper();
	}

}
