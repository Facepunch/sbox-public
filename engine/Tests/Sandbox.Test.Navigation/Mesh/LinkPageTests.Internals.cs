using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// What a commit allocates on the managed heap among 30,000 links, against the size of the link table.
/// </summary>
public partial class LinkPageTests
{
	static partial void CopiedLittle( long most )
	{
		long table = (long)Side * Side * PerTile * System.Runtime.CompilerServices.Unsafe.SizeOf<LinkEntry>();
		Console.WriteLine( $"{Side * Side * PerTile} links, table {table:N0} bytes, a one-tile commit allocates at most {most:N0} bytes" );
		Assert.IsTrue( most * 20 < table, $"{most:N0} bytes per commit" );
	}
}
