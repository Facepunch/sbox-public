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

	internal static string PipeName( int pid, long startTime ) => $"sbox-mcp-{pid}-{startTime:x}";

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
