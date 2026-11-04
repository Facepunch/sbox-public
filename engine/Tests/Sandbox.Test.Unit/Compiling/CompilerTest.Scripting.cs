using System.IO;

namespace CompilingTests;

public partial class CompilerTest
{
	[TestMethod]
	public async Task SandboxScriptIsAvailableToGameCode()
	{
		using var access = new AccessControl();
		using var group = new CompileGroup( "SandboxScript" ) { AccessControl = access };
		var settings = new Compiler.Configuration { Whitelist = true };
		group.CreateCompiler( "script-test", Path.GetFullPath( "data/code/scripting" ), settings );
		await group.BuildAsync();
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
	}

	[TestMethod]
	public async Task BreenIsNotAvailableToGameCode()
	{
		using var access = new AccessControl();
		// Make metadata resolvable without whitelisting it, so this tests permissions rather than
		// the resolver's separate refusal to load arbitrary third-party assemblies from disk.
		using var metadata = access.TrustUnsafe( File.ReadAllBytes( typeof( Breen.Script ).Assembly.Location ) );
		using var group = new CompileGroup( "BreenAccess" ) { AccessControl = access };
		var settings = new Compiler.Configuration { Whitelist = false };
		var compiler = group.CreateCompiler( "breen-access", Path.GetFullPath( "data/code/breen-access" ), settings );
		compiler.AddReference( "Breen" );
		await group.BuildAsync();
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		using var binary = new MemoryStream( compiler.Output.AssemblyData );
		var verification = access.VerifyAssembly( binary, out var trusted );
		trusted?.Dispose();
		Assert.IsFalse( verification.Success );
		Assert.IsTrue( verification.WhitelistErrors.Any( e => e.Name.Contains( "Breen" ) ), string.Join( "\n", verification.Errors.Concat( verification.WhitelistErrors.Select( e => e.Name ) ) ) );
		settings.Whitelist = true;
		compiler.SetConfiguration( settings );
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsFalse( group.BuildResult.Success, "Even an explicit Breen reference must fail addon access checks." );
	}
}
