using System;
using System.IO;

namespace FilesystemTests;

[TestClass]
public class SafePathTests
{
	[TestMethod]
	[DataRow( "con" )]
	[DataRow( "aux.2026.png" )]
	[DataRow( "..." )]
	public void ToFileNameFallsBackOnUnusableNames( string input )
	{
		Assert.AreEqual( "fallback", SafePath.ToFileName( input, "fallback" ) );
	}

	[TestMethod]
	public void IsWithinRejectsEscapes()
	{
		Assert.IsTrue( SafePath.IsWithin( "root", Path.Combine( "root", "a", "b.png" ) ) );
		Assert.IsFalse( SafePath.IsWithin( "root", Path.Combine( "root", "..", "b.png" ) ) );
		Assert.IsFalse( SafePath.IsWithin( "root", "rootsibling/b.png" ) );
	}

	[TestMethod]
	[DataRow( "com1.txt" )]
	[DataRow( "folder/LPT1.json" )]
	public void WritingToDeviceNamesThrows( string path )
	{
		Assert.ThrowsException<ArgumentException>( () => new MemoryFileSystem().WriteAllText( path, "x" ) );
	}

	[TestMethod]
	public void NamesThatStartLikeDevicesWork()
	{
		var fs = new MemoryFileSystem();
		fs.WriteAllText( "com10.txt", "x" );

		Assert.IsTrue( fs.FileExists( "com10.txt" ) );
		Assert.AreEqual( "console", SafePath.ToFileName( "console", "fallback" ) );
	}
}
