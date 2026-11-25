using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Mcp.Transport;

/// <summary>
/// Newline-delimited UTF-8 messages shared by the pipe server and stdio bridge.
/// </summary>
internal sealed class McpWire( Stream input, int maxBytes = 8 * 1024 * 1024 )
{
	readonly byte[] buffer = new byte[8192];
	int position;
	int length;
	static readonly UTF8Encoding Encoding = new( false, true );

	/// <summary>
	/// The pipe a process serves MCP on: its id and when it started, so a reused id names another pipe. On Linux the start
	/// is the kernel's (<c>/proc/[pid]/stat</c>, clock ticks since boot): <see cref="System.Diagnostics.Process.StartTime"/> there is worked
	/// out from the boot time each time it's read, and comes out a little different in the server and the client.
	/// </summary>
	internal static string PipeName( System.Diagnostics.Process process ) => $"sbox-mcp-{process.Id}-{StartOf( process ):x}";

	static long StartOf( System.Diagnostics.Process process )
	{
		if ( OperatingSystem.IsLinux() )
		{
			var stat = File.ReadAllText( $"/proc/{process.Id}/stat" );

			// After the command name, which may hold spaces and brackets: the state is field 3, the start field 22
			var fields = stat[(stat.LastIndexOf( ')' ) + 2)..].Split( ' ' );
			return long.Parse( fields[22 - 3], System.Globalization.CultureInfo.InvariantCulture );
		}

		return process.StartTime.ToUniversalTime().Ticks;
	}

	internal async Task<string> ReadAsync( CancellationToken cancellation )
	{
		using var message = new MemoryStream();

		while ( true )
		{
			if ( position == length )
			{
				length = await input.ReadAsync( buffer, cancellation ).ConfigureAwait( false );
				position = 0;

				if ( length == 0 )
				{
					if ( message.Length != 0 )
						throw new EndOfStreamException( "Incomplete MCP message: expected a newline." );

					return null;
				}
			}

			var newline = Array.IndexOf( buffer, (byte)'\n', position, length - position );
			var end = newline < 0 ? length : newline;
			var count = end - position;

			if ( message.Length + count > maxBytes )
				throw new InvalidDataException( $"MCP message exceeds {maxBytes} bytes." );

			message.Write( buffer, position, count );
			position = newline < 0 ? end : end + 1;

			// Some writers (Windows PowerShell's redirected stdin among them) open the stream with a byte order mark.
			if ( newline >= 0 )
				return Encoding.GetString( message.GetBuffer(), 0, (int)message.Length ).TrimStart( '﻿' ).TrimEnd( '\r' );
		}
	}

	internal static async Task WriteAsync( Stream output, string message, CancellationToken cancellation )
	{
		var bytes = Encoding.GetBytes( message + "\n" );
		await output.WriteAsync( bytes, cancellation ).ConfigureAwait( false );
		await output.FlushAsync( cancellation ).ConfigureAwait( false );
	}
}
