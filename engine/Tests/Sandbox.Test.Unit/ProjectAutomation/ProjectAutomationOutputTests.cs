using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ProjectAutomationTests;

[TestClass]
public class ProjectAutomationOutputTests
{
	private static readonly string ProjectPath = Path.GetFullPath(
		Path.Combine( Path.GetTempPath(), "project space unicodé", "fixture.sbproj" ) );

	[TestMethod]
	public void JsonSuccess_HasExactV1ShapeAndUsesOnlyStdout()
	{
		var result = CreateResult(
			success: true,
			exitCode: 0,
			category: "success",
			diagnostics: [] );

		var output = Write( result, json: true );

		Assert.AreEqual( string.Empty, output.Stderr );
		using var document = JsonDocument.Parse( output.Stdout );
		var root = document.RootElement;
		AssertExactProperties( root,
			"schemaVersion", "command", "success", "exitCode", "category",
			"projectPath", "engineVersion", "durationMilliseconds", "diagnostics" );
		Assert.AreEqual( 1, root.GetProperty( "schemaVersion" ).GetInt32() );
		Assert.AreEqual( "project.compile", root.GetProperty( "command" ).GetString() );
		Assert.IsTrue( root.GetProperty( "success" ).GetBoolean() );
		Assert.AreEqual( 0, root.GetProperty( "exitCode" ).GetInt32() );
		Assert.AreEqual( "success", root.GetProperty( "category" ).GetString() );
		Assert.AreEqual( ProjectPath, root.GetProperty( "projectPath" ).GetString() );
		Assert.AreEqual( "test-version", root.GetProperty( "engineVersion" ).GetString() );
		Assert.AreEqual( 17L, root.GetProperty( "durationMilliseconds" ).GetInt64() );
		Assert.AreEqual( 0, root.GetProperty( "diagnostics" ).GetArrayLength() );
	}

	[TestMethod]
	public void JsonFailure_HasExactDiagnosticShapeAndEscapesUntrustedText()
	{
		var message = "A \"quoted\" error\r\nwith a slash \\ and unicodé ☃";
		var diagnostic = new ProjectAutomationDiagnostic
		{
			Severity = "error",
			Id = "CS0103",
			Message = message,
			File = Path.Combine( Path.GetDirectoryName( ProjectPath ), "Editor", "Broken.cs" ),
			Line = 3,
			Column = 22
		};
		var result = CreateResult(
			success: false,
			exitCode: 1,
			category: "project_failure",
			diagnostics: [diagnostic] );

		var output = Write( result, json: true );

		Assert.AreEqual( string.Empty, output.Stderr );
		using var document = JsonDocument.Parse( output.Stdout );
		var root = document.RootElement;
		AssertExactProperties( root,
			"schemaVersion", "command", "success", "exitCode", "category",
			"projectPath", "engineVersion", "durationMilliseconds", "diagnostics" );
		Assert.IsFalse( root.GetProperty( "success" ).GetBoolean() );
		Assert.AreEqual( 1, root.GetProperty( "exitCode" ).GetInt32() );
		Assert.AreEqual( "project_failure", root.GetProperty( "category" ).GetString() );

		var written = root.GetProperty( "diagnostics" ).EnumerateArray().Single();
		AssertExactProperties( written, "severity", "id", "message", "file", "line", "column" );
		Assert.AreEqual( "error", written.GetProperty( "severity" ).GetString() );
		Assert.AreEqual( "CS0103", written.GetProperty( "id" ).GetString() );
		Assert.AreEqual( message, written.GetProperty( "message" ).GetString() );
		Assert.AreEqual( diagnostic.File, written.GetProperty( "file" ).GetString() );
		Assert.AreEqual( 3, written.GetProperty( "line" ).GetInt32() );
		Assert.AreEqual( 22, written.GetProperty( "column" ).GetInt32() );
	}

	[TestMethod]
	public void JsonUnavailableValues_ArePresentAsNull()
	{
		var result = new ProjectAutomationResult
		{
			SchemaVersion = 1,
			Command = "project.compile",
			Success = false,
			ExitCode = 3,
			Category = "internal_error",
			ProjectPath = null,
			EngineVersion = null,
			DurationMilliseconds = 0,
			Diagnostics =
			[
				new ProjectAutomationDiagnostic
				{
					Severity = "error",
					Id = "SBOXCLI_INTERNAL",
					Message = "Bootstrap failed.",
					File = null,
					Line = null,
					Column = null
				}
			]
		};

		var output = Write( result, json: true );
		using var document = JsonDocument.Parse( output.Stdout );
		var root = document.RootElement;
		Assert.AreEqual( JsonValueKind.Null, root.GetProperty( "projectPath" ).ValueKind );
		Assert.AreEqual( JsonValueKind.Null, root.GetProperty( "engineVersion" ).ValueKind );
		var diagnostic = root.GetProperty( "diagnostics" ).EnumerateArray().Single();
		Assert.AreEqual( JsonValueKind.Null, diagnostic.GetProperty( "file" ).ValueKind );
		Assert.AreEqual( JsonValueKind.Null, diagnostic.GetProperty( "line" ).ValueKind );
		Assert.AreEqual( JsonValueKind.Null, diagnostic.GetProperty( "column" ).ValueKind );
	}

	[TestMethod]
	public void HumanSuccess_UsesOneExactStdoutLine()
	{
		var output = Write( CreateResult( true, 0, "success", [] ), json: false );

		Assert.AreEqual( $"Project compiled successfully: {ProjectPath}{Environment.NewLine}", output.Stdout );
		Assert.AreEqual( string.Empty, output.Stderr );
	}

	[TestMethod]
	public void HumanFailure_UsesStderrAndOneBasedLocation()
	{
		var diagnostic = Diagnostic(
			Path.Combine( Path.GetDirectoryName( ProjectPath ), "Editor", "Broken.cs" ),
			3,
			5,
			"CS0103",
			"The name does not exist" );
		var output = Write(
			CreateResult( false, 1, "project_failure", [diagnostic] ),
			json: false );

		Assert.AreEqual( string.Empty, output.Stdout );
		Assert.AreEqual(
			$"{diagnostic.File}(3,5): error CS0103: The name does not exist{Environment.NewLine}",
			output.Stderr );
	}

	[TestMethod]
	public void HumanFailureWithoutDiagnostics_UsesCategoryFallback()
	{
		var output = Write(
			CreateResult( false, 3, "internal_error", [] ),
			json: false );

		Assert.AreEqual( string.Empty, output.Stdout );
		Assert.AreEqual( $"sbox-cli failed (internal_error).{Environment.NewLine}", output.Stderr );
	}

	private static ProjectAutomationResult CreateResult(
		bool success,
		int exitCode,
		string category,
		ProjectAutomationDiagnostic[] diagnostics )
	{
		return new ProjectAutomationResult
		{
			SchemaVersion = 1,
			Command = "project.compile",
			Success = success,
			ExitCode = exitCode,
			Category = category,
			ProjectPath = ProjectPath,
			EngineVersion = "test-version",
			DurationMilliseconds = 17,
			Diagnostics = diagnostics
		};
	}

	private static ProjectAutomationDiagnostic Diagnostic(
		string file,
		int line,
		int column,
		string id,
		string message )
	{
		return new ProjectAutomationDiagnostic
		{
			Severity = "error",
			Id = id,
			Message = message,
			File = file,
			Line = line,
			Column = column
		};
	}

	private static (string Stdout, string Stderr) Write( ProjectAutomationResult result, bool json )
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();
		ProjectAutomationOutput.Write( result, json, stdout, stderr );
		return (stdout.ToString(), stderr.ToString());
	}

	private static void AssertExactProperties( JsonElement value, params string[] expected )
	{
		var actual = value.EnumerateObject().Select( x => x.Name ).ToArray();
		Assert.AreEqual(
			expected.Length,
			actual.Length,
			$"Unexpected JSON shape: {string.Join( ", ", actual )}" );
		CollectionAssert.AreEquivalent( expected, actual );
	}
}
