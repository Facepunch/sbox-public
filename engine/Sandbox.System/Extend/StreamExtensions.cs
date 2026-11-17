using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	/// <summary>
	/// Read a null terminated string from the stream, at given offset.
	/// </summary>
	/// <param name="stream">The stream to read from.</param>
	/// <param name="offset">Offset where to start reading, from the beginning of the stream.</param>
	/// <exception cref="EndOfStreamException">The stream ends before the null terminator.</exception>
	public static string ReadNullTerminatedString( this Stream stream, long offset )
	{
		stream.Seek( offset, SeekOrigin.Begin );

		int b;
		List<byte> bytes = new List<byte>();
		while ( (b = stream.ReadByte()) != 0x00 )
		{
			if ( b == -1 )
				throw new EndOfStreamException();

			bytes.Add( (byte)b );
		}

		return Encoding.UTF8.GetString( bytes.ToArray() );
	}
}
