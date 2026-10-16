using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Source1;

internal sealed partial class MdlFile
{
	private IReadOnlyList<VmtNode> ReadKeyValues( Stream stream )
	{
		if ( _header.KeyValueSize == 0 )
			return Array.Empty<VmtNode>();

		ValidateRange<byte>( stream, _header.KeyValueOffset, _header.KeyValueSize );
		stream.Position = _header.KeyValueOffset;
		var bytes = new byte[_header.KeyValueSize];
		stream.ReadExactly( bytes );
		var end = Array.IndexOf( bytes, (byte)0 );
		var text = Encoding.UTF8.GetString( bytes, 0, end < 0 ? bytes.Length : end );
		return VmtFile.ParseKeyValues( text, "MDL keyvalues",
			diagnostic => throw new NotSupportedException( diagnostic.Message ) );
	}
}
