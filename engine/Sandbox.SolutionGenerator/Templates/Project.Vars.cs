using System.Collections.Generic;
using System.Linq;

namespace Sandbox.SolutionGenerator;

internal partial class Project
{
	public string ProjectName;

	public List<string> References;
	public List<string> GlobalStatic;
	public List<string> GlobalUsing;
	public List<string> IgnoreFolders;
	public List<string> IgnoreFiles = new();

	public string ProjectReferences;

	public bool Unsafe;

	/// <summary>
	/// Relative path to the game/bin/managed folder
	/// </summary>
	public string ManagedRoot;

	/// <summary>
	/// Relative path to the game/ folder
	/// </summary>
	public string GameRoot;

	public string RootNamespace;
	public string Nullable = "disable";
	public string DefineConstants = "SANDBOX;DEBUG;TRACE";
	public string NoWarn = "1701;1702;1591;8974";
	public string WarningsAsErrors = "";
	public bool TreatWarningsAsErrors = false;
	public bool IsEditorProject = false;
	public bool IsUnitTestProject = false;

	public List<string> PropertyGroupExtras
	{
		get
		{
			var p = new List<string>();
			var excludes = new List<string>();

			// Add folder excludes
			if ( IgnoreFolders?.Any() == true )
			{
				excludes.AddRange( IgnoreFolders.Select( x => $"**/{x}/**" ) );
			}

			// Add file pattern excludes
			if ( IgnoreFiles?.Any() == true )
			{
				excludes.AddRange( IgnoreFiles.Select( x => x.Replace( "/", "\\" ) ) );
			}

			if ( excludes.Any() )
			{
				var excludeString = string.Join( ";", excludes );
				p.Add( $"<DefaultItemExcludes>$(DefaultItemExcludes);{excludeString}</DefaultItemExcludes>" );
			}

			if ( IsUnitTestProject )
			{
				p.Add( $"<IsTestProject>true</IsTestProject>" );
			}

			return p;
		}
	}
}
