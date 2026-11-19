using System.IO;

namespace CompilingTests;

public partial class CompilerTest
{
	[TestMethod]
	public async Task SandboxScriptIsAvailableToGameCode()
	{
		using var group = new CompileGroup( "SandboxScript" );
		var settings = new Compiler.Configuration { Whitelist = true };
		group.CreateCompiler( "script-test", Path.GetFullPath( "data/code/scripting" ), settings );
		await group.BuildAsync();
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
	}

	[TestMethod]
	public async Task BreenIsNotAvailableToGameCode()
	{
		using var group = new CompileGroup( "BreenAccess" );
		var settings = new Compiler.Configuration { Whitelist = true };
		var compiler = group.CreateCompiler( "breen-access", Path.GetFullPath( "data/code/breen-access" ), settings );
		compiler.AddReference( "Breen" );
		await group.BuildAsync();
		Assert.IsFalse( group.BuildResult.Success, "Even an explicit Breen reference must fail addon access checks." );
		Assert.IsTrue( compiler.Diagnostics.Any( x => x.Id == "SB500" && x.GetMessage().Contains( "Breen" ) ), group.BuildResult.BuildDiagnosticsString() );
	}
}
