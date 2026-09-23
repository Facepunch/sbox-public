using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectAutomationTests;

[TestClass, DoNotParallelize]
[TestCategory( "ProjectAutomation" )]
public class ProjectAutomationProcessTests
{
	private static readonly TimeSpan FastTimeout = TimeSpan.FromSeconds( 20 );
	private static readonly TimeSpan LoadFailureTimeout = TimeSpan.FromMinutes( 2 );
	private static readonly TimeSpan CompileTimeout = TimeSpan.FromMinutes( 5 );

	[TestMethod]
	public async Task Help_ProcessContract()
	{
		using var fixture = CliFixture.Create();
		var result = await RunCliAsync( fixture.CallerDirectory, FastTimeout, "--help" );

		Assert.AreEqual( 0, result.ExitCode, result.Describe() );
		Assert.IsTrue( result.Stdout.Contains( "project compile", StringComparison.OrdinalIgnoreCase ), result.Describe() );
		Assert.IsTrue( result.Stdout.Contains( "--project", StringComparison.Ordinal ), result.Describe() );
		Assert.AreEqual( string.Empty, result.Stderr, result.Describe() );
	}

	[TestMethod]
	public async Task Version_ProcessContract()
	{
		using var fixture = CliFixture.Create();
		var result = await RunCliAsync( fixture.CallerDirectory, FastTimeout, "--version" );

		Assert.AreEqual( 0, result.ExitCode, result.Describe() );
		var lines = result.Stdout.Split(
			['\r', '\n'],
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		Assert.AreEqual( 1, lines.Length, result.Describe() );
		StringAssert.StartsWith( lines[0], "sbox-cli ", result.Describe() );
		Assert.AreEqual( string.Empty, result.Stderr, result.Describe() );
	}

	[TestMethod]
	public async Task MissingAndUnknownArguments_AreUsageErrorsAndTerminate()
	{
		using var fixture = CliFixture.Create();
		var missing = await RunCliAsync(
			fixture.CallerDirectory,
			FastTimeout,
			"project", "compile", "--json" );
		using ( var document = AssertJsonUsageError( missing ) )
		{
			StringAssert.Contains(
				document.RootElement.GetProperty( "diagnostics" )[0].GetProperty( "message" ).GetString(),
				"--project",
				missing.Describe() );
		}

		var unknown = await RunCliAsync(
			fixture.CallerDirectory,
			FastTimeout,
			"project", "compile", "--project", "unused.sbproj", "--timeout", "1s" );
		Assert.AreEqual( 2, unknown.ExitCode, unknown.Describe() );
		Assert.AreEqual( string.Empty, unknown.Stdout, unknown.Describe() );
		StringAssert.Contains( unknown.Stderr, "--timeout", unknown.Describe() );
	}

	[TestMethod]
	public async Task MissingProject_JsonProcessContract()
	{
		using var fixture = CliFixture.Create();
		var missingPath = Path.Combine( fixture.ProjectDirectory, "missing.sbproj" );
		var relativePath = Path.GetRelativePath( fixture.CallerDirectory, missingPath );
		var result = await RunCliAsync(
			fixture.CallerDirectory,
			FastTimeout,
			"project", "compile", "--project", relativePath, "--json" );

		using var document = AssertJsonCompileResult( result, 1, "project_failure", missingPath );
		var diagnostic = document.RootElement.GetProperty( "diagnostics" ).EnumerateArray().Single();
		Assert.AreEqual( "SBOXCLI_PROJECT", diagnostic.GetProperty( "id" ).GetString(), result.Describe() );
	}

	[TestMethod]
	public async Task NonSbprojFile_IsProjectFailureRatherThanUsageError()
	{
		using var fixture = CliFixture.Create();
		var nonProjectPath = Path.Combine( fixture.ProjectDirectory, "notes.txt" );
		File.WriteAllText( nonProjectPath, "not a project manifest" );
		var relativePath = Path.GetRelativePath( fixture.CallerDirectory, nonProjectPath );
		var result = await RunCliAsync(
			fixture.CallerDirectory,
			FastTimeout,
			"project", "compile", "--project", relativePath, "--json" );

		using var document = AssertJsonCompileResult( result, 1, "project_failure", nonProjectPath );
	}

	[TestMethod]
	public async Task MalformedProject_JsonProcessContract()
	{
		using var fixture = CliFixture.Create();
		fixture.WriteMalformedProjectManifest();
		var relativePath = Path.GetRelativePath( fixture.CallerDirectory, fixture.ProjectPath );
		var result = await RunCliAsync(
			fixture.CallerDirectory,
			LoadFailureTimeout,
			"project", "compile", "--project", relativePath, "--json" );

		using var document = AssertJsonCompileResult( result, 1, "project_failure", fixture.ProjectPath );
		Assert.IsFalse( document.RootElement.GetProperty( "success" ).GetBoolean(), result.Describe() );
	}

	[TestMethod]
	public async Task RelativeUnicodeLegacyProject_WithCodeEditorAndLocalLibrary_CompilesTwiceWithoutMutation()
	{
		string fixtureRoot;
		using ( var fixture = CliFixture.Create() )
		{
			fixtureRoot = fixture.RootDirectory;
			var projectConfigBefore = File.ReadAllText( fixture.ProjectPath );
			var relativePath = Path.GetRelativePath( fixture.CallerDirectory, fixture.ProjectPath );
			Assert.IsTrue( relativePath.Contains( "project space unicodé", StringComparison.Ordinal ) );

			// Two complete native-host lifecycles catch shutdown leaks that a single invocation misses.
			for ( var iteration = 0; iteration < 2; ++iteration )
			{
				if ( iteration == 0 )
				{
					var result = await RunCliAsync(
						fixture.CallerDirectory,
						CompileTimeout,
						"project", "compile", "--project", relativePath, "--json" );

					using var document = AssertJsonCompileResult( result, 0, "success", fixture.ProjectPath );
					var errors = document.RootElement.GetProperty( "diagnostics" )
						.EnumerateArray()
						.Where( x => x.GetProperty( "severity" ).GetString() == "error" )
						.ToArray();
					Assert.AreEqual( 0, errors.Length, result.Describe() );
				}
				else
				{
					var result = await RunCliAsync(
						fixture.CallerDirectory,
						CompileTimeout,
						"project", "compile", "--project", relativePath );

					Assert.AreEqual( 0, result.ExitCode, result.Describe() );
					Assert.AreEqual(
						$"Project compiled successfully: {fixture.ProjectPath}{Environment.NewLine}",
						result.Stdout,
						result.Describe() );
					StringAssert.Contains( result.Stderr, "Compiling project code...", result.Describe() );
				}

				Assert.AreEqual(
					projectConfigBefore,
					File.ReadAllText( fixture.ProjectPath ),
					"Project configuration upgrades must remain in memory during automation." );
			}
		}

		Assert.IsFalse(
			Directory.Exists( fixtureRoot ),
			"A completed CLI process retained a handle beneath the owned fixture root." );
	}

	[TestMethod]
	public async Task LegacyProject_InvalidEditorCodeUsesInMemoryUpgradeWithoutSavingConfig()
	{
		using var fixture = CliFixture.Create();
		var originalProjectFile = fixture.WriteLegacyProjectManifest();
		fixture.WriteBrokenEditorSource();
		var relativePath = Path.GetRelativePath( fixture.CallerDirectory, fixture.ProjectPath );
		var result = await RunCliAsync(
			fixture.CallerDirectory,
			CompileTimeout,
			"project", "compile", "--project", relativePath, "--json" );

		using var document = AssertJsonCompileResult( result, 1, "project_failure", fixture.ProjectPath );
		var diagnostic = document.RootElement.GetProperty( "diagnostics" )
			.EnumerateArray()
			.Single( x => x.GetProperty( "id" ).GetString() == "CS0103" );
		AssertExactProperties( diagnostic, "severity", "id", "message", "file", "line", "column" );
		Assert.AreEqual( "error", diagnostic.GetProperty( "severity" ).GetString(), result.Describe() );
		AssertPathEqual( fixture.EditorSourcePath, diagnostic.GetProperty( "file" ).GetString(), result.Describe() );
		Assert.IsTrue( diagnostic.GetProperty( "line" ).GetInt32() > 0, result.Describe() );
		Assert.IsTrue( diagnostic.GetProperty( "column" ).GetInt32() > 0, result.Describe() );
		CollectionAssert.AreEqual(
			originalProjectFile,
			File.ReadAllBytes( fixture.ProjectPath ),
			"Automation must upgrade the legacy schema in memory without rewriting the project file." );
	}

	private static JsonDocument AssertJsonUsageError( CliProcessResult result )
	{
		Assert.AreEqual( 2, result.ExitCode, result.Describe() );
		Assert.AreEqual( string.Empty, result.Stderr, result.Describe() );
		var document = JsonDocument.Parse( result.Stdout );
		var root = document.RootElement;
		AssertExactProperties( root,
			"schemaVersion", "command", "success", "exitCode", "category",
			"projectPath", "engineVersion", "durationMilliseconds", "diagnostics" );
		Assert.AreEqual( JsonValueKind.Null, root.GetProperty( "command" ).ValueKind, result.Describe() );
		Assert.IsFalse( root.GetProperty( "success" ).GetBoolean(), result.Describe() );
		Assert.AreEqual( 2, root.GetProperty( "exitCode" ).GetInt32(), result.Describe() );
		Assert.AreEqual( "usage_error", root.GetProperty( "category" ).GetString(), result.Describe() );
		Assert.AreEqual( JsonValueKind.Null, root.GetProperty( "projectPath" ).ValueKind, result.Describe() );
		Assert.AreEqual( JsonValueKind.Null, root.GetProperty( "engineVersion" ).ValueKind, result.Describe() );
		Assert.AreEqual( 0L, root.GetProperty( "durationMilliseconds" ).GetInt64(), result.Describe() );
		var diagnostic = root.GetProperty( "diagnostics" ).EnumerateArray().Single();
		AssertExactProperties( diagnostic, "severity", "id", "message", "file", "line", "column" );
		Assert.AreEqual( "SBOXCLI_USAGE", diagnostic.GetProperty( "id" ).GetString(), result.Describe() );
		return document;
	}

	private static JsonDocument AssertJsonCompileResult(
		CliProcessResult result,
		int expectedExitCode,
		string expectedCategory,
		string expectedProjectPath )
	{
		Assert.AreEqual( expectedExitCode, result.ExitCode, result.Describe() );
		Assert.AreEqual( string.Empty, result.Stderr, result.Describe() );

		// Parsing the entire stream rejects leading/trailing engine log text and a second JSON document.
		var document = JsonDocument.Parse( result.Stdout );
		var root = document.RootElement;
		AssertExactProperties( root,
			"schemaVersion", "command", "success", "exitCode", "category",
			"projectPath", "engineVersion", "durationMilliseconds", "diagnostics" );
		Assert.AreEqual( 1, root.GetProperty( "schemaVersion" ).GetInt32(), result.Describe() );
		Assert.AreEqual( "project.compile", root.GetProperty( "command" ).GetString(), result.Describe() );
		Assert.AreEqual( expectedExitCode == 0, root.GetProperty( "success" ).GetBoolean(), result.Describe() );
		Assert.AreEqual( expectedExitCode, root.GetProperty( "exitCode" ).GetInt32(), result.Describe() );
		Assert.AreEqual( expectedCategory, root.GetProperty( "category" ).GetString(), result.Describe() );
		AssertPathEqual( expectedProjectPath, root.GetProperty( "projectPath" ).GetString(), result.Describe() );
		Assert.IsTrue( Path.IsPathFullyQualified( root.GetProperty( "projectPath" ).GetString() ), result.Describe() );
		Assert.IsTrue( root.GetProperty( "durationMilliseconds" ).GetInt64() >= 0, result.Describe() );
		Assert.IsTrue(
			root.GetProperty( "engineVersion" ).ValueKind is JsonValueKind.String or JsonValueKind.Null,
			result.Describe() );
		Assert.AreEqual( JsonValueKind.Array, root.GetProperty( "diagnostics" ).ValueKind, result.Describe() );
		return document;
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

	private static void AssertPathEqual( string expected, string actual, string message )
	{
		Assert.IsNotNull( actual, message );
		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		Assert.IsTrue(
			string.Equals( Path.GetFullPath( expected ), Path.GetFullPath( actual ), comparison ),
			$"Expected path '{expected}', got '{actual}'.{Environment.NewLine}{message}" );
	}

	private static async Task<CliProcessResult> RunCliAsync(
		string workingDirectory,
		TimeSpan timeout,
		params string[] arguments )
	{
		var gamePath = Environment.GetEnvironmentVariable(
			"FACEPUNCH_ENGINE",
			EnvironmentVariableTarget.Process );
		Assert.IsFalse( string.IsNullOrWhiteSpace( gamePath ), "FACEPUNCH_ENGINE is required." );

		var executable = Path.Combine(
			Path.GetFullPath( gamePath ),
			OperatingSystem.IsWindows() ? "sbox-cli.exe" : "sbox-cli" );
		Assert.IsTrue( File.Exists( executable ), $"Source-built CLI not found: {executable}" );

		var argumentSnapshot = arguments.ToArray();
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = executable,
				WorkingDirectory = workingDirectory,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			}
		};

		foreach ( var argument in argumentSnapshot )
			process.StartInfo.ArgumentList.Add( argument );

		var timer = Stopwatch.StartNew();
		var started = false;
		try
		{
			started = process.Start();
			Assert.IsTrue( started, $"Failed to start {executable}." );
			var pid = process.Id;

			// Drain both streams immediately; waiting for exit first can deadlock when a pipe fills.
			var stdoutTask = process.StandardOutput.ReadToEndAsync();
			var stderrTask = process.StandardError.ReadToEndAsync();

			using var timeoutSource = new CancellationTokenSource( timeout );
			try
			{
				await process.WaitForExitAsync( timeoutSource.Token );
			}
			catch ( OperationCanceledException ) when ( timeoutSource.IsCancellationRequested )
			{
				await TerminateExactProcessTreeAsync( process );
				var timedOutOutput = await DrainAsync( stdoutTask, stderrTask );
				Assert.Fail(
					$"sbox-cli PID {pid} exceeded {timeout}." + Environment.NewLine +
					$"arguments: {string.Join( " ", argumentSnapshot )}" + Environment.NewLine +
					$"stdout:{Environment.NewLine}{timedOutOutput.Stdout}" + Environment.NewLine +
					$"stderr:{Environment.NewLine}{timedOutOutput.Stderr}" );
			}

			var output = await DrainAsync( stdoutTask, stderrTask );
			timer.Stop();
			return new CliProcessResult(
				pid,
				process.ExitCode,
				output.Stdout,
				output.Stderr,
				timer.Elapsed,
				executable,
				argumentSnapshot );
		}
		finally
		{
			// This is the exact Process returned by Start. Never enumerate or terminate by image name:
			// an unrelated Editor may be running on the same machine.
			if ( started && !process.HasExited )
				await TerminateExactProcessTreeAsync( process );
		}
	}

	private static async Task TerminateExactProcessTreeAsync( Process process )
	{
		try
		{
			if ( !process.HasExited )
				process.Kill( entireProcessTree: true );
		}
		catch ( InvalidOperationException )
		{
			// The exact process exited between HasExited and Kill.
		}

		if ( !process.HasExited )
			await process.WaitForExitAsync().WaitAsync( TimeSpan.FromSeconds( 15 ) );
	}

	private static async Task<(string Stdout, string Stderr)> DrainAsync(
		Task<string> stdoutTask,
		Task<string> stderrTask )
	{
		await Task.WhenAll( stdoutTask, stderrTask ).WaitAsync( TimeSpan.FromSeconds( 15 ) );
		return (await stdoutTask, await stderrTask);
	}

	private sealed record CliProcessResult(
		int Pid,
		int ExitCode,
		string Stdout,
		string Stderr,
		TimeSpan Elapsed,
		string Executable,
		string[] Arguments )
	{
		public string Describe()
		{
			return
				$"Executable: {Executable}{Environment.NewLine}" +
				$"PID: {Pid}{Environment.NewLine}" +
				$"Arguments: {string.Join( " ", Arguments )}{Environment.NewLine}" +
				$"Exit: {ExitCode}{Environment.NewLine}" +
				$"Elapsed: {Elapsed}{Environment.NewLine}" +
				$"stdout:{Environment.NewLine}{Stdout}{Environment.NewLine}" +
				$"stderr:{Environment.NewLine}{Stderr}";
		}
	}

	private sealed class CliFixture : IDisposable
	{
		private const string GameProjectJson = """
			{
			  "Title": "CLI Compile Fixture",
			  "Type": "game",
			  "Org": "local",
			  "Ident": "cli_compile_fixture",
			  "Schema": 0,
			  "PackageReferences": [],
			  "Metadata": {
			    "Compiler": {
			      "TreatWarningsAsErrors": false
			    }
			  }
			}
			""";

		private const string LibraryProjectJson = """
			{
			  "Title": "CLI Fixture Library",
			  "Type": "library",
			  "Org": "local",
			  "Ident": "cli_fixture_library",
			  "Schema": 1,
			  "PackageReferences": [],
			  "Metadata": {
			    "Compiler": {
			      "TreatWarningsAsErrors": false
			    }
			  }
			}
			""";

		private const string LegacyGameProjectJson = """
			{
			  "Title": "Legacy CLI Compile Fixture",
			  "Org": "local",
			  "Ident": "cli_compile_fixture",
			  "Schema": 0,
			  "PackageReferences": [],
			  "Metadata": {
			    "Compiler": {
			      "TreatWarningsAsErrors": false
			    }
			  }
			}
			""";

		private const string GameCode = """
			namespace CliCompileFixture;

			public sealed class RuntimeProbe
			{
				public int Value => global::CliCompileLibrary.LibraryProbe.Answer;
			}
			""";

		private const string GameEditorCode = """
			namespace CliCompileFixture.Editor;

			public sealed class EditorProbe
			{
				public int RuntimeValue => new global::CliCompileFixture.RuntimeProbe().Value;
				public string LibraryEditorValue => global::CliCompileLibrary.Editor.LibraryEditorProbe.Text;
			}
			""";

		private const string BrokenGameEditorCode = """
			namespace CliCompileFixture.Editor;

			public sealed class BrokenEditorProbe
			{
				public int Value => MissingEditorOnlySymbol;
			}
			""";

		private const string LibraryCode = """
			namespace CliCompileLibrary;

			public static class LibraryProbe
			{
				public const int Answer = 42;
			}
			""";

		private const string LibraryEditorCode = """
			namespace CliCompileLibrary.Editor;

			public static class LibraryEditorProbe
			{
				public const string Text = "library editor compiled";
			}
			""";

		public string RootDirectory { get; }
		public string CallerDirectory { get; }
		public string ProjectDirectory { get; }
		public string ProjectPath { get; }
		public string EditorSourcePath { get; }

		private CliFixture()
		{
			RootDirectory = Path.GetFullPath( Path.Combine(
				Path.GetTempPath(),
				$"sbox-cli-test-{Guid.NewGuid():N}" ) );
			if ( !IsOwnedTestRoot( RootDirectory ) )
				throw new InvalidOperationException( $"Refusing to create unsafe test root: {RootDirectory}" );

			CallerDirectory = Path.Combine( RootDirectory, "caller" );
			ProjectDirectory = Path.Combine( RootDirectory, "projects", "project space unicodé" );
			ProjectPath = Path.Combine( ProjectDirectory, "compile-fixture.sbproj" );
			EditorSourcePath = Path.Combine( ProjectDirectory, "Editor", "EditorProbe.cs" );
			var libraryDirectory = Path.Combine( ProjectDirectory, "Libraries", "fixture library" );

			Directory.CreateDirectory( CallerDirectory );
			Directory.CreateDirectory( Path.Combine( ProjectDirectory, "Code" ) );
			Directory.CreateDirectory( Path.Combine( ProjectDirectory, "Editor" ) );
			Directory.CreateDirectory( Path.Combine( libraryDirectory, "Code" ) );
			Directory.CreateDirectory( Path.Combine( libraryDirectory, "Editor" ) );

			File.WriteAllText( ProjectPath, GameProjectJson );
			File.WriteAllText( Path.Combine( ProjectDirectory, "Code", "RuntimeProbe.cs" ), GameCode );
			File.WriteAllText( EditorSourcePath, GameEditorCode );
			File.WriteAllText( Path.Combine( libraryDirectory, "library.sbproj" ), LibraryProjectJson );
			File.WriteAllText( Path.Combine( libraryDirectory, "Code", "LibraryProbe.cs" ), LibraryCode );
			File.WriteAllText( Path.Combine( libraryDirectory, "Editor", "LibraryEditorProbe.cs" ), LibraryEditorCode );
		}

		public static CliFixture Create() => new();

		public void WriteMalformedProjectManifest()
		{
			File.WriteAllText( ProjectPath, "{ \"Title\": \"unterminated\"" );
		}

		public byte[] WriteLegacyProjectManifest()
		{
			File.WriteAllText( ProjectPath, LegacyGameProjectJson );
			return File.ReadAllBytes( ProjectPath );
		}

		public void WriteBrokenEditorSource()
		{
			File.WriteAllText( EditorSourcePath, BrokenGameEditorCode );
		}

		public void Dispose()
		{
			if ( !Directory.Exists( RootDirectory ) )
				return;

			if ( !IsOwnedTestRoot( RootDirectory ) )
				throw new InvalidOperationException( $"Refusing to delete unsafe test root: {RootDirectory}" );

			Exception lastFailure = null;
			for ( var attempt = 0; attempt < 5; ++attempt )
			{
				try
				{
					Directory.Delete( RootDirectory, recursive: true );
					return;
				}
				catch ( IOException ex )
				{
					lastFailure = ex;
				}
				catch ( UnauthorizedAccessException ex )
				{
					lastFailure = ex;
				}

				Thread.Sleep( TimeSpan.FromMilliseconds( 100 * (attempt + 1) ) );
			}

			throw new IOException( $"Unable to delete owned test root: {RootDirectory}", lastFailure );
		}

		private static bool IsOwnedTestRoot( string path )
		{
			var full = Path.TrimEndingDirectorySeparator( Path.GetFullPath( path ) );
			var temp = Path.TrimEndingDirectorySeparator( Path.GetFullPath( Path.GetTempPath() ) );
			var comparison = OperatingSystem.IsWindows()
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal;
			var parent = Path.GetDirectoryName( full );

			return parent is not null
				&& string.Equals( parent, temp, comparison )
				&& Path.GetFileName( full ).StartsWith( "sbox-cli-test-", StringComparison.Ordinal );
		}
	}
}
