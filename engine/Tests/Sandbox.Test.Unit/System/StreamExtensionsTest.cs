using System;
using System.IO;
using System.Text;

namespace SystemTests;

/// <summary>
/// Null-terminated stream reads stop at a terminator or report EOF.
/// </summary>
[TestClass]
public class StreamExtensionsTest
{
	/// <summary>
	/// Valid strings respect the requested offset and decode UTF-8.
	/// </summary>
	[TestMethod]
	public void ReadsTerminatedString()
	{
		using var stream = new MemoryStream( Encoding.UTF8.GetBytes( "skip:héllo\0tail" ) );
		Assert.AreEqual( "héllo", stream.ReadNullTerminatedString( 5 ) );
	}

	/// <summary>
	/// Missing terminators fail on the first EOF instead of reading forever.
	/// </summary>
	[DataTestMethod]
	[DataRow( "" )]
	[DataRow( "unterminated" )]
	public void RejectsMissingTerminator( string value )
	{
		using var stream = new EofGuardStream( Encoding.UTF8.GetBytes( value ) );
		Assert.ThrowsException<EndOfStreamException>( () => stream.ReadNullTerminatedString( 0 ) );
	}

	// Make regressions fail promptly rather than hanging the test runner at EOF.
	private sealed class EofGuardStream( byte[] buffer ) : MemoryStream( buffer )
	{
		private bool reachedEnd;

		/// <summary>
		/// Allows one EOF result and rejects further reads.
		/// </summary>
		public override int ReadByte()
		{
			if ( reachedEnd )
				throw new InvalidOperationException( "Read beyond EOF." );

			var value = base.ReadByte();
			reachedEnd = value == -1;
			return value;
		}
	}
}
