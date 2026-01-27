using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace CrashReporter;

/// <summary>
/// Extracts managed (.NET) stack frames from a minidump using ClrMD.
/// This allows us to see C# method names in crash reports instead of &lt;unknown&gt;.
/// </summary>
static class ManagedStackExtractor
{
	/// <summary>
	/// Attempts to extract managed stack traces from a minidump file on disk.
	/// Returns a formatted string containing all managed thread stacks, or null if extraction fails.
	/// </summary>
	public static string? ExtractManagedStacks( string minidumpPath )
	{
		try
		{
			if ( !File.Exists( minidumpPath ) )
			{
				Console.WriteLine( $"Minidump file not found: {minidumpPath}" );
				return null;
			}

			using var dataTarget = DataTarget.LoadDump( minidumpPath );
			return ExtractFromDataTarget( dataTarget, Path.GetFileName( minidumpPath ) );
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Failed to extract managed stacks from file: {ex.Message}" );
			return null;
		}
	}

	static string? ExtractFromDataTarget( DataTarget dataTarget, string sourceName )
	{
		if ( dataTarget.ClrVersions.Length == 0 )
		{
			Console.WriteLine( "No CLR found in minidump" );
			return null;
		}

		var sb = new StringBuilder();
		sb.AppendLine( "=== Managed Stack Traces ===" );
		sb.AppendLine( $"Source: {sourceName}" );
		sb.AppendLine( $"Extracted: {DateTime.UtcNow:O}" );
		sb.AppendLine();

		foreach ( var clrVersion in dataTarget.ClrVersions )
		{
			sb.AppendLine( $"CLR Version: {clrVersion.Version}" );
			sb.AppendLine();

			ClrRuntime? runtime = null;
			try
			{
				runtime = clrVersion.CreateRuntime();
			}
			catch ( Exception ex )
			{
				sb.AppendLine( $"ERROR: Failed to create runtime: {ex.Message}" );
				sb.AppendLine();
				continue;
			}

			using ( runtime )
			{
				// Get all threads with managed frames
				var threadsWithManagedCode = runtime.Threads
					.Where( t => t.EnumerateStackTrace().Any( f => f.Method != null ) )
					.ToList();

				if ( threadsWithManagedCode.Count == 0 )
				{
					sb.AppendLine( "No threads with managed frames found." );
					continue;
				}

				sb.AppendLine( $"Threads with managed code: {threadsWithManagedCode.Count}" );
				sb.AppendLine();

				foreach ( var thread in threadsWithManagedCode )
				{
					sb.AppendLine( $"--- Thread {thread.OSThreadId} (Managed ID: {thread.ManagedThreadId}) ---" );

					if ( thread.CurrentException != null )
					{
						var ex = thread.CurrentException;
						sb.AppendLine( $"  ** EXCEPTION: {ex.Type?.Name}: {ex.Message}" );

						// Print the exception's stack trace if available
						foreach ( var frame in ex.StackTrace )
						{
							var method = frame.Method;
							if ( method != null )
							{
								var typeName = method.Type?.Name ?? "<unknown type>";
								var methodName = method.Name ?? "<unknown method>";
								var signature = GetMethodSignature( method );
								sb.AppendLine( $"       at {typeName}.{methodName}{signature}" );
							}
							else
							{
								sb.AppendLine( $"       at 0x{frame.InstructionPointer:X16} <native>" );
							}
						}
					}

					sb.AppendLine( "" );

					var frames = thread.EnumerateStackTrace().ToList();
					var frameIndex = 0;

					foreach ( var frame in frames )
					{
						var method = frame.Method;
						if ( method == null )
						{
							// Native frame - show instruction pointer for correlation
							sb.AppendLine( $"  [{frameIndex,2}] 0x{frame.InstructionPointer:X16} <native>" );
						}
						else
						{
							var typeName = method.Type?.Name ?? "<unknown type>";
							var methodName = method.Name ?? "<unknown method>";
							var signature = GetMethodSignature( method );

							sb.AppendLine( $"  [{frameIndex,2}] 0x{frame.InstructionPointer:X16} {typeName}.{methodName}{signature}" );
						}

						frameIndex++;
					}

					sb.AppendLine();
				}

				// Also dump any unhandled exceptions
				var threadsWithExceptions = runtime.Threads
					.Where( t => t.CurrentException != null )
					.ToList();

				if ( threadsWithExceptions.Count > 0 )
				{
					sb.AppendLine( "=== Threads with Exceptions ===" );
					foreach ( var thread in threadsWithExceptions )
					{
						var ex = thread.CurrentException!;
						sb.AppendLine( $"Thread {thread.OSThreadId:X}: {ex.Type?.Name}" );
						sb.AppendLine( $"  Message: {ex.Message}" );
						sb.AppendLine( $"  HResult: 0x{ex.HResult:X8}" );

						// Walk the exception chain
						var inner = ex.Inner;
						var depth = 1;
						while ( inner != null && depth < 10 )
						{
							sb.AppendLine( $"  Inner[{depth}]: {inner.Type?.Name}: {inner.Message}" );
							inner = inner.Inner;
							depth++;
						}

						sb.AppendLine();
					}
				}
			}
		}

		return sb.ToString();
	}

	/// <summary>
	/// Finds the most recent minidump file in the game directory that was written within the last 2 minutes.
	/// </summary>
	public static string? FindRecentMinidump( string gameDir )
	{
		try
		{
			var cutoffTime = DateTime.UtcNow.AddSeconds( -120 );

			var dumpFiles = Directory.GetFiles( gameDir, "*.mdmp" )
				.Select( f => new FileInfo( f ) )
				.Where( f => f.LastWriteTimeUtc >= cutoffTime )
				.OrderByDescending( f => f.LastWriteTimeUtc )
				.ToList();

			if ( dumpFiles.Count == 0 )
			{
				return null;
			}

			var latest = dumpFiles[0];
			Console.WriteLine( $"Found minidump: {latest.Name} ({latest.Length:N0} bytes)" );
			return latest.FullName;
		}
		catch ( Exception ex )
		{
			Console.WriteLine( $"Error finding minidump: {ex.Message}" );
			return null;
		}
	}

	static string GetMethodSignature( ClrMethod method )
	{
		try
		{
			var sig = method.Signature;
			if ( string.IsNullOrEmpty( sig ) )
				return "()";

			// The signature includes the full method, extract just the parameters
			var parenIndex = sig.IndexOf( '(' );
			if ( parenIndex >= 0 )
			{
				return sig[parenIndex..];
			}

			return "()";
		}
		catch
		{
			return "()";
		}
	}
}
