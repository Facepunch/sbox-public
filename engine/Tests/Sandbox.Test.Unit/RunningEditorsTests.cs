using Sandbox.LauncherUI;
using Sandbox.Mcp.Transport;
using System;
using System.Diagnostics;

[TestClass]
public class RunningEditorsTests
{
	[TestMethod]
	public void ProjectFromArgumentsReadsLastProjectAndPreservesSpaces()
	{
		var args = new[]
		{
			"/path/to/sbox-dev",
			"-project",
			"/first/project/.sbproj",
			"-project",
			"/home/user/My Sbox Games/Test Game/.sbproj",
			""
		};

		Assert.AreEqual( "/home/user/My Sbox Games/Test Game/.sbproj", RunningEditors.ProjectFromArguments( args ) );
	}

	[TestMethod]
	public void ProjectFromArgumentsReturnsNullWhenMissingOrTrailing()
	{
		Assert.IsNull( RunningEditors.ProjectFromArguments( ["/path/to/sbox-dev"] ) );
		Assert.IsNull( RunningEditors.ProjectFromArguments( ["/path/to/sbox-dev", "-project"] ) );
		Assert.IsNull( RunningEditors.ProjectFromArguments( ["/path/to/sbox-dev", "-project", ""] ) );
	}

	[TestMethod]
	public void ProjectFromCommandLineHandlesQuotesAndWordBoundaries()
	{
		Assert.AreEqual( @"C:\My Projects\Game\.sbproj",
			RunningEditors.ProjectFromCommandLine( @"sbox-dev.exe -project ""C:\My Projects\Game\.sbproj"" -flag" ) );

		Assert.AreEqual( @"C:\Games\second.sbproj",
			RunningEditors.ProjectFromCommandLine( @"sbox-dev.exe -project C:\Games\first.sbproj -project C:\Games\second.sbproj" ) );

		Assert.AreEqual( @"C:\Real\.sbproj",
			RunningEditors.ProjectFromCommandLine( @"sbox-dev.exe -project C:\Real\.sbproj --my-project-dir C:\Fake" ) );

		Assert.IsNull( RunningEditors.ProjectFromCommandLine( @"sbox-dev.exe -projectdir C:\Fake" ) );
	}

	[TestMethod]
	public void McpWirePipeNameIsStableAcrossProcessInstances()
	{
		using var current = Process.GetCurrentProcess();
		using var byId = Process.GetProcessById( Environment.ProcessId );

		Assert.AreEqual( McpWire.PipeName( current ), McpWire.PipeName( byId ) );
	}
}
