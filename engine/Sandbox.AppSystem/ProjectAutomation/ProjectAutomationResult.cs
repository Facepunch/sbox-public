using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sandbox;

internal static class ProjectAutomationExitCode
{
	public const int Success = 0;
	public const int ProjectFailure = 1;
	public const int UsageError = 2;
	public const int InternalError = 3;
}

internal static class ProjectAutomationCategory
{
	public const string Success = "success";
	public const string ProjectFailure = "project_failure";
	public const string UsageError = "usage_error";
	public const string InternalError = "internal_error";
}

internal sealed class ProjectAutomationResult
{
	public int SchemaVersion { get; init; } = 1;
	public string Command { get; init; }
	public bool Success { get; init; }
	public int ExitCode { get; init; }
	public string Category { get; init; }
	public string ProjectPath { get; init; }
	public string EngineVersion { get; init; }
	public long DurationMilliseconds { get; init; }
	public IReadOnlyList<ProjectAutomationDiagnostic> Diagnostics { get; init; } = [];
}

internal sealed class ProjectAutomationDiagnostic
{
	public string Severity { get; init; }
	public string Id { get; init; }
	public string Message { get; init; }
	public string File { get; init; }
	public int? Line { get; init; }
	public int? Column { get; init; }
}

internal static class ProjectAutomationOutput
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public static void Write(
		ProjectAutomationResult result,
		bool json,
		TextWriter stdout,
		TextWriter stderr )
	{
		ArgumentNullException.ThrowIfNull( result );
		ArgumentNullException.ThrowIfNull( stdout );
		ArgumentNullException.ThrowIfNull( stderr );

		if ( json )
		{
			stdout.WriteLine( JsonSerializer.Serialize( result, JsonOptions ) );
			return;
		}

		foreach ( var diagnostic in result.Diagnostics )
		{
			stderr.WriteLine( FormatDiagnostic( diagnostic ) );
		}

		if ( result.Success )
		{
			stdout.WriteLine( $"Project compiled successfully: {result.ProjectPath}" );
		}
		else if ( result.Diagnostics.Count == 0 )
		{
			stderr.WriteLine( $"sbox-cli failed ({result.Category})." );
		}
	}

	private static string FormatDiagnostic( ProjectAutomationDiagnostic diagnostic )
	{
		var location = diagnostic.File;
		if ( location is not null && diagnostic.Line is not null )
		{
			location += diagnostic.Column is not null
				? $"({diagnostic.Line},{diagnostic.Column})"
				: $"({diagnostic.Line})";
		}

		var prefix = location is null ? "" : $"{location}: ";
		return $"{prefix}{diagnostic.Severity} {diagnostic.Id}: {diagnostic.Message}";
	}
}
