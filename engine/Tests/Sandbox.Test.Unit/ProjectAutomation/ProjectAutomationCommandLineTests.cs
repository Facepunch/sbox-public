using System;
using System.IO;
using System.Text.Json;

namespace ProjectAutomationTests;

[TestClass]
public class ProjectAutomationCommandLineTests
{
	private static readonly string InvocationDirectory = Path.GetFullPath(
		Path.Combine( Path.GetTempPath(), "sbox-cli-parser-tests" ) );

	[TestMethod]
	public void Help_IsTerminalSuccess()
	{
		var invocation = ProjectAutomationCommandLine.Parse( ["--help"], InvocationDirectory );

		Assert.AreEqual( ProjectAutomationAction.Help, invocation.Action );
		Assert.IsFalse( invocation.Json );
		Assert.IsNull( invocation.ProjectPath );
		Assert.IsTrue( string.IsNullOrEmpty( invocation.Error ) );
	}

	[TestMethod]
	public void Version_IsTerminalSuccess()
	{
		var invocation = ProjectAutomationCommandLine.Parse( ["--version"], InvocationDirectory );

		Assert.AreEqual( ProjectAutomationAction.Version, invocation.Action );
		Assert.IsFalse( invocation.Json );
		Assert.IsNull( invocation.ProjectPath );
		Assert.IsTrue( string.IsNullOrEmpty( invocation.Error ) );
	}

	[TestMethod]
	public void Compile_ParsesCanonicalGrammar()
	{
		var projectPath = Path.Combine( InvocationDirectory, "project.sbproj" );
		var invocation = ProjectAutomationCommandLine.Parse(
			["project", "compile", "--project", projectPath],
			InvocationDirectory );

		Assert.AreEqual( ProjectAutomationAction.Compile, invocation.Action );
		Assert.IsFalse( invocation.Json );
		AssertPathEqual( projectPath, invocation.ProjectPath );
		Assert.IsTrue( string.IsNullOrEmpty( invocation.Error ) );
	}

	[TestMethod]
	public void Compile_ParsesJsonInEitherOptionOrder()
	{
		var projectPath = Path.Combine( InvocationDirectory, "project.sbproj" );
		var before = ProjectAutomationCommandLine.Parse(
			["project", "compile", "--json", "--project", projectPath],
			InvocationDirectory );
		var after = ProjectAutomationCommandLine.Parse(
			["project", "compile", "--project", projectPath, "--json"],
			InvocationDirectory );

		Assert.AreEqual( ProjectAutomationAction.Compile, before.Action );
		Assert.AreEqual( ProjectAutomationAction.Compile, after.Action );
		Assert.IsTrue( before.Json );
		Assert.IsTrue( after.Json );
		AssertPathEqual( projectPath, before.ProjectPath );
		AssertPathEqual( projectPath, after.ProjectPath );
	}

	[TestMethod]
	public void RelativeProject_UsesCapturedInvocationDirectoryWithSpacesAndUnicode()
	{
		var invocationDirectory = Path.Combine( InvocationDirectory, "caller space unicodé" );
		var relativePath = Path.Combine( "..", "projects", "project space ☃", "compile-fixture.sbproj" );
		var expected = Path.GetFullPath( relativePath, invocationDirectory );

		var invocation = ProjectAutomationCommandLine.Parse(
			["project", "compile", "--project", relativePath],
			invocationDirectory );

		Assert.AreEqual( ProjectAutomationAction.Compile, invocation.Action );
		AssertPathEqual( expected, invocation.ProjectPath );
	}

	[TestMethod]
	public void NonSbprojPath_StillParsesForProjectFailureClassification()
	{
		var invocation = ProjectAutomationCommandLine.Parse(
			["project", "compile", "--project", "project.txt"],
			InvocationDirectory );

		Assert.AreEqual( ProjectAutomationAction.Compile, invocation.Action );
		AssertPathEqual( Path.Combine( InvocationDirectory, "project.txt" ), invocation.ProjectPath );
	}

	[TestMethod]
	public void InvalidSyntax_ReturnsUsageError()
	{
		AssertUsageError();
		AssertUsageError( "unknown" );
		AssertUsageError( "PROJECT", "compile", "--project", "project.sbproj" );
		AssertUsageError( "project" );
		AssertUsageError( "project", "unknown" );
		AssertUsageError( "project", "compile" );
		AssertUsageError( "project", "compile", "--project" );
		AssertUsageError( "project", "compile", "--project", "" );
		AssertUsageError( "project", "compile", "--project", "   " );
		AssertUsageError( "project", "compile", "--project", "--json" );
		AssertUsageError( "project", "compile", "--project", "project.sbproj", "--project", "other.sbproj" );
		AssertUsageError( "project", "compile", "--project", "project.sbproj", "--json", "--json" );
		AssertUsageError( "project", "compile", "--project", "project.sbproj", "--unknown" );
		AssertUsageError( "project", "compile", "--project", "project.sbproj", "unexpected" );
		AssertUsageError( "project", "compile", "--project", "project.sbproj", "--timeout", "1s" );
		AssertUsageError( "project", "compile", "--project", "project.sbproj", "--log-file", "build.log" );
		AssertUsageError( "project", "test", "--project", "project.sbproj" );
		AssertUsageError( "project", "validate", "--project", "project.sbproj" );
		AssertUsageError( "project", "run", "--project", "project.sbproj" );
		AssertUsageError( "project", "export", "--project", "project.sbproj" );
		AssertUsageError( "project", "exec", "--project", "project.sbproj" );
		AssertUsageError( "--help", "extra" );
		AssertUsageError( "--version", "extra" );
	}

	[TestMethod]
	public void InvalidSyntax_PreservesJsonOutputIntent()
	{
		var invocation = ProjectAutomationCommandLine.Parse(
			["project", "compile", "--unknown", "--json"],
			InvocationDirectory );

		Assert.AreEqual( ProjectAutomationAction.Error, invocation.Action );
		Assert.IsTrue( invocation.Json );
		StringAssert.Contains( invocation.Error, "--unknown" );
	}

	private static void AssertUsageError( params string[] arguments )
	{
		var invocation = ProjectAutomationCommandLine.Parse( arguments, InvocationDirectory );

		Assert.AreEqual(
			ProjectAutomationAction.Error,
			invocation.Action,
			$"Expected usage error for: {string.Join( " ", arguments )}" );
		Assert.IsFalse(
			string.IsNullOrWhiteSpace( invocation.Error ),
			$"Expected a useful error for: {string.Join( " ", arguments )}" );
	}

	private static void AssertPathEqual( string expected, string actual )
	{
		Assert.IsNotNull( actual );
		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		Assert.IsTrue(
			string.Equals( Path.GetFullPath( expected ), Path.GetFullPath( actual ), comparison ),
			$"Expected path '{expected}', got '{actual}'." );
	}
}

[TestClass, DoNotParallelize]
public class ProjectAutomationCliFrontDoorTests
{
	private static readonly string InvocationDirectory = Path.GetFullPath(
		Path.Combine( Path.GetTempPath(), "sbox-cli-front-door-tests" ) );

	[TestMethod]
	public void Help_WritesDocumentedGrammarToStdoutOnly()
	{
		var output = Run( "--help" );

		Assert.AreEqual( ProjectAutomationExitCode.Success, output.ExitCode );
		Assert.AreEqual( ProjectAutomationCommandLine.HelpText + Environment.NewLine, output.Stdout );
		Assert.AreEqual( string.Empty, output.Stderr );
	}

	[TestMethod]
	public void Version_WritesOneStableLineToStdoutOnly()
	{
		var output = Run( "--version" );

		Assert.AreEqual( ProjectAutomationExitCode.Success, output.ExitCode );
		Assert.AreEqual( "sbox-cli 0000000" + Environment.NewLine, output.Stdout );
		Assert.AreEqual( string.Empty, output.Stderr );
	}

	[TestMethod]
	public void HumanUsageError_WritesDiagnosticAndHelpToStderrOnly()
	{
		var output = Run();

		Assert.AreEqual( ProjectAutomationExitCode.UsageError, output.ExitCode );
		Assert.AreEqual( string.Empty, output.Stdout );
		StringAssert.StartsWith(
			output.Stderr,
			"error SBOXCLI_USAGE: Expected 'project compile', '--help', or '--version'." + Environment.NewLine );
		StringAssert.Contains( output.Stderr, ProjectAutomationCommandLine.HelpText );
	}

	[TestMethod]
	public void JsonUsageError_WritesOneExactDocumentToStdoutOnly()
	{
		var output = Run( "project", "compile", "--json" );

		Assert.AreEqual( ProjectAutomationExitCode.UsageError, output.ExitCode );
		Assert.AreEqual( string.Empty, output.Stderr );
		using var document = JsonDocument.Parse( output.Stdout );
		var root = document.RootElement;
		Assert.AreEqual( JsonValueKind.Null, root.GetProperty( "command" ).ValueKind );
		Assert.IsFalse( root.GetProperty( "success" ).GetBoolean() );
		Assert.AreEqual( ProjectAutomationExitCode.UsageError, root.GetProperty( "exitCode" ).GetInt32() );
		Assert.AreEqual( ProjectAutomationCategory.UsageError, root.GetProperty( "category" ).GetString() );
		Assert.AreEqual( JsonValueKind.Null, root.GetProperty( "projectPath" ).ValueKind );
		Assert.AreEqual( JsonValueKind.Null, root.GetProperty( "engineVersion" ).ValueKind );
		Assert.AreEqual( 0L, root.GetProperty( "durationMilliseconds" ).GetInt64() );
		var diagnostic = root.GetProperty( "diagnostics" ).EnumerateArray().Single();
		Assert.AreEqual( "error", diagnostic.GetProperty( "severity" ).GetString() );
		Assert.AreEqual( "SBOXCLI_USAGE", diagnostic.GetProperty( "id" ).GetString() );
	}

	private static (int ExitCode, string Stdout, string Stderr) Run( params string[] arguments )
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();
		var exitCode = ProjectAutomationCli.Run(
			arguments,
			InvocationDirectory,
			InvocationDirectory,
			stdout,
			stderr );
		return (exitCode, stdout.ToString(), stderr.ToString());
	}
}
