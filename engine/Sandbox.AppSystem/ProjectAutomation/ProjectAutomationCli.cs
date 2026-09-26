using Microsoft.CodeAnalysis;
using Sandbox.Diagnostics;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Sandbox;

/// <summary>
/// One-shot, non-interactive s&amp;box project automation entry point.
/// </summary>
internal static class ProjectAutomationCli
{
	private const string CompileCommand = "project.compile";

	public static int Run(
		string[] args,
		string invocationDirectory,
		string gameRoot,
		TextWriter stdout,
		TextWriter stderr )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( invocationDirectory );
		ArgumentException.ThrowIfNullOrWhiteSpace( gameRoot );
		ArgumentNullException.ThrowIfNull( stdout );
		ArgumentNullException.ThrowIfNull( stderr );

		var invocation = ProjectAutomationCommandLine.Parse( args, invocationDirectory );

		if ( invocation.Action == ProjectAutomationAction.Help )
		{
			stdout.WriteLine( ProjectAutomationCommandLine.HelpText );
			return ProjectAutomationExitCode.Success;
		}

		if ( invocation.Action == ProjectAutomationAction.Version )
		{
			try
			{
				Application.TryLoadVersionInfo( gameRoot );
				stdout.WriteLine( $"sbox-cli {Application.Version}" );
				return ProjectAutomationExitCode.Success;
			}
			catch ( Exception e )
			{
				e = Unwrap( e );
				stderr.WriteLine( $"error SBOXCLI_INTERNAL: {e.GetType().Name}: {e.Message}" );
				return ProjectAutomationExitCode.InternalError;
			}
		}

		if ( invocation.Action == ProjectAutomationAction.Error )
		{
			var result = CreateResult(
				command: null,
				success: false,
				exitCode: ProjectAutomationExitCode.UsageError,
				category: ProjectAutomationCategory.UsageError,
				projectPath: null,
				engineVersion: null,
				durationMilliseconds: 0,
				diagnostics: [CreateDiagnostic( "error", "SBOXCLI_USAGE", invocation.Error )] );

			ProjectAutomationOutput.Write( result, invocation.Json, stdout, stderr );
			if ( !invocation.Json )
				stderr.WriteLine( ProjectAutomationCommandLine.HelpText );

			return result.ExitCode;
		}

		var compileResult = Compile( invocation, gameRoot, stderr );
		ProjectAutomationOutput.Write( compileResult, invocation.Json, stdout, stderr );
		return compileResult.ExitCode;
	}

	private static ProjectAutomationResult Compile(
		ProjectAutomationInvocation invocation,
		string gameRoot,
		TextWriter stderr )
	{
		var stopwatch = Stopwatch.StartNew();
		var diagnostics = new List<ProjectAutomationDiagnostic>();
		var success = false;
		var exitCode = ProjectAutomationExitCode.InternalError;
		var category = ProjectAutomationCategory.InternalError;
		string engineVersion = null;
		var processOut = Console.Out;
		var processError = Console.Error;
		var consoleRedirected = false;
		var operationCompleted = false;

		if ( !File.Exists( invocation.ProjectPath ) )
		{
			diagnostics.Add( CreateDiagnostic(
				"error",
				"SBOXCLI_PROJECT",
				$"Project file was not found: {invocation.ProjectPath}",
				invocation.ProjectPath ) );
			exitCode = ProjectAutomationExitCode.ProjectFailure;
			category = ProjectAutomationCategory.ProjectFailure;

			return Finish();
		}

		if ( !string.Equals( Path.GetExtension( invocation.ProjectPath ), ".sbproj", StringComparison.OrdinalIgnoreCase ) )
		{
			diagnostics.Add( CreateDiagnostic(
				"error",
				"SBOXCLI_PROJECT",
				$"Project path must name a .sbproj file: {invocation.ProjectPath}",
				invocation.ProjectPath ) );
			exitCode = ProjectAutomationExitCode.ProjectFailure;
			category = ProjectAutomationCategory.ProjectFailure;

			return Finish();
		}

		try
		{
			Application.IsAutomation = true;
			Application.TryLoadVersionInfo( gameRoot );
			engineVersion = Application.Version;

			// Keep the process streams owned by this command even when lower layers write to
			// Console directly. The injected writers still reference the original streams.
			Console.SetOut( TextWriter.Null );
			Console.SetError( TextWriter.Null );
			consoleRedirected = true;

			// Bootstrap also honors IsAutomation and leaves this disabled.
			Logging.PrintToConsole = false;

			ReportProgress( "Initializing s&box project compiler..." );
			using ( new ToolAppSystem( gameRoot ) )
			{
				SyncContext.RunBlocking( Project.InitializeBuiltIn(
						syncPackageManager: false,
						saveUpgradedConfigs: false ) );

				try
				{
					var project = Project.AddFromFile(
						invocation.ProjectPath,
						active: false,
						saveUpgradedConfig: false );

					Project.Current = project;
					project.Active = true;

					Project.AddLocalLibraries(
						project,
						throwOnInvalid: true,
						saveUpgradedConfigs: false );

					SyncContext.RunBlocking( Project.PrepareForCompileAsync(
						project,
						invocation.Json ? null : ReportProgress,
						CancellationToken.None,
						throwOnPackageFailure: true ) );
				}
				catch ( Exception e )
				{
					throw new ProjectPreparationException( Unwrap( e ) );
				}

				try
				{
					ReportProgress( "Compiling project code..." );
					CompileGroup.SuppressBuildNotifications = true;
					Project.CompileGroup.PrintErrorsInConsole = false;
					var compileSucceeded = SyncContext.RunBlocking( Project.CompileAsync() );

					diagnostics.AddRange( ReadCompileDiagnostics() );

					if ( !compileSucceeded && diagnostics.Count == 0 )
					{
						diagnostics.Add( CreateDiagnostic(
							"error",
							"SBOXCLI_COMPILE",
							"The s&box compiler failed without reporting a diagnostic." ) );
					}

					success = compileSucceeded;
					exitCode = compileSucceeded
						? ProjectAutomationExitCode.Success
						: ProjectAutomationExitCode.ProjectFailure;
					category = compileSucceeded
						? ProjectAutomationCategory.Success
						: ProjectAutomationCategory.ProjectFailure;
					operationCompleted = true;
				}
				catch ( Exception e )
				{
					throw new ProjectCompilationException( Unwrap( e ) );
				}

				// The using statement disposes the native host after this point. No result is
				// emitted until that shutdown has completed successfully.
			}
		}
		catch ( ProjectPreparationException e )
		{
			diagnostics.Add( CreateDiagnostic(
				"error",
				"SBOXCLI_PROJECT",
				e.InnerException?.Message ?? e.Message,
				invocation.ProjectPath ) );
			exitCode = ProjectAutomationExitCode.ProjectFailure;
			category = ProjectAutomationCategory.ProjectFailure;
		}
		catch ( ProjectCompilationException e )
		{
			diagnostics.Add( CreateDiagnostic(
				"error",
				"SBOXCLI_COMPILE",
				e.InnerException?.Message ?? e.Message,
				invocation.ProjectPath ) );
			exitCode = ProjectAutomationExitCode.ProjectFailure;
			category = ProjectAutomationCategory.ProjectFailure;
		}
		catch ( Exception e )
		{
			e = Unwrap( e );
			success = false;
			exitCode = ProjectAutomationExitCode.InternalError;
			category = ProjectAutomationCategory.InternalError;
			diagnostics.Add( CreateDiagnostic(
				"error",
				operationCompleted ? "SBOXCLI_SHUTDOWN" : "SBOXCLI_INTERNAL",
				operationCompleted
					? $"Engine shutdown failed: {e.GetType().Name}: {e.Message}"
					: $"{e.GetType().Name}: {e.Message}" ) );
		}
		finally
		{
			if ( consoleRedirected )
			{
				Console.SetOut( processOut );
				Console.SetError( processError );
			}

			Application.IsAutomation = false;
		}

		return Finish();

		void ReportProgress( string message )
		{
			if ( !invocation.Json )
				stderr.WriteLine( message );
		}

		ProjectAutomationResult Finish()
		{
			stopwatch.Stop();
			return CreateResult(
				CompileCommand,
				success,
				exitCode,
				category,
				invocation.ProjectPath,
				engineVersion,
				stopwatch.ElapsedMilliseconds,
				OrderDiagnostics( diagnostics ) );
		}
	}

	private static IEnumerable<ProjectAutomationDiagnostic> ReadCompileDiagnostics()
	{
		var diagnostics = Project.GetCompileDiagnostics()
			.Select( FromCompilerDiagnostic )
			.ToList();

		foreach ( var compiler in Project.CompileGroup.Compilers )
		{
			if ( compiler.Output?.Exception is not { } exception )
				continue;

			exception = Unwrap( exception );
			diagnostics.Add( CreateDiagnostic(
				"error",
				"SBOXCLI_COMPILER",
				$"{compiler.Name}: {exception.GetType().Name}: {exception.Message}" ) );
		}

		return diagnostics;
	}

	private static ProjectAutomationDiagnostic FromCompilerDiagnostic( Diagnostic diagnostic )
	{
		string file = null;
		int? line = null;
		int? column = null;

		if ( diagnostic.Location is { IsInSource: true } location )
		{
			var span = location.GetLineSpan();
			file = string.IsNullOrWhiteSpace( span.Path ) ? null : span.Path;
			line = span.StartLinePosition.Line + 1;
			column = span.StartLinePosition.Character + 1;
		}

		return CreateDiagnostic(
			diagnostic.Severity.ToString().ToLowerInvariant(),
			diagnostic.Id,
			diagnostic.GetMessage( CultureInfo.InvariantCulture ),
			file,
			line,
			column );
	}

	private static IReadOnlyList<ProjectAutomationDiagnostic> OrderDiagnostics(
		IEnumerable<ProjectAutomationDiagnostic> diagnostics )
	{
		return diagnostics
			.OrderBy( x => SeverityOrder( x.Severity ) )
			.ThenBy( x => x.File is null ? 1 : 0 )
			.ThenBy( x => x.File, StringComparer.OrdinalIgnoreCase )
			.ThenBy( x => x.Line ?? int.MaxValue )
			.ThenBy( x => x.Column ?? int.MaxValue )
			.ThenBy( x => x.Id, StringComparer.Ordinal )
			.ThenBy( x => x.Message, StringComparer.Ordinal )
			.ToArray();
	}

	private static int SeverityOrder( string severity ) => severity switch
	{
		"error" => 0,
		"warning" => 1,
		"info" => 2,
		"hidden" => 3,
		_ => 4
	};

	private static ProjectAutomationDiagnostic CreateDiagnostic(
		string severity,
		string id,
		string message,
		string file = null,
		int? line = null,
		int? column = null )
	{
		return new ProjectAutomationDiagnostic
		{
			Severity = severity,
			Id = id,
			Message = message,
			File = file,
			Line = line,
			Column = column
		};
	}

	private static ProjectAutomationResult CreateResult(
		string command,
		bool success,
		int exitCode,
		string category,
		string projectPath,
		string engineVersion,
		long durationMilliseconds,
		IReadOnlyList<ProjectAutomationDiagnostic> diagnostics )
	{
		return new ProjectAutomationResult
		{
			Command = command,
			Success = success,
			ExitCode = exitCode,
			Category = category,
			ProjectPath = projectPath,
			EngineVersion = engineVersion,
			DurationMilliseconds = durationMilliseconds,
			Diagnostics = diagnostics
		};
	}

	private static Exception Unwrap( Exception exception )
	{
		while ( exception is AggregateException { InnerExceptions.Count: 1 } aggregate )
			exception = aggregate.InnerExceptions[0];

		return exception;
	}

	private sealed class ProjectPreparationException : Exception
	{
		public ProjectPreparationException( Exception innerException )
			: base( "Project preparation failed.", innerException )
		{
		}
	}

	private sealed class ProjectCompilationException : Exception
	{
		public ProjectCompilationException( Exception innerException )
			: base( "Project compilation failed.", innerException )
		{
		}
	}
}
