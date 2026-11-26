using Microsoft.CodeAnalysis;

namespace Sandbox;

/// <summary>
/// Formats compiler diagnostics for build reports and editor tools.
/// </summary>
public static class CompilerDiagnosticExtensions
{
	/// <summary>
	/// Returns the original location, or <see cref="Location.None"/> if its span extends past the source text.
	/// </summary>
	public static Location GetDisplayLocation( this Diagnostic diagnostic )
	{
		var location = diagnostic.Location;
		if ( location.SourceTree is { } tree && location.SourceSpan.End > tree.GetText().Length )
			return Location.None;

		return location;
	}

	internal static string FormatBuildDiagnostic( this Diagnostic diagnostic )
	{
		var span = diagnostic.GetDisplayLocation().GetLineSpan();
		var message = diagnostic.GetMessage();
		if ( span.IsValid )
			return $"{diagnostic.Severity} | {message} - {span.Path}:{span.StartLinePosition}";

		var path = diagnostic.Location.SourceTree?.FilePath;
		var fallback = $"{diagnostic.Severity} | {diagnostic.Id}: {message}";
		return string.IsNullOrEmpty( path ) ? fallback : $"{fallback} - {path}";
	}

	internal static string FormatForClipboard( this Diagnostic diagnostic )
	{
		// Roslyn's clipboard representation resolves the location again.
		return diagnostic.GetDisplayLocation() == diagnostic.Location ? diagnostic.ToString() : diagnostic.FormatBuildDiagnostic();
	}
}
