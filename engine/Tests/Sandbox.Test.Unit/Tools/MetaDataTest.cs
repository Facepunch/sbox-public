using System;
using System.IO;
using Editor;

namespace ToolsTests;

[TestClass]
public class MetaDataTest
{
	/// <summary>
	/// A read scope reuses one parse, but never hides a write: a value set inside the scope, or
	/// inside a nested scope on the same file, is what the next lookup returns, and once the scope
	/// is gone reads see the file as it is on disk again.
	/// </summary>
	[TestMethod]
	public void ReadScopeSeesWrites()
	{
		var path = Path.Combine( Path.GetTempPath(), $"{Guid.NewGuid()}.meta" );
		var meta = new MetaData( path );

		try
		{
			meta.Set( "value", 1 );

			using ( MetaData.CacheReads( meta ) )
			{
				Assert.AreEqual( 1, meta.GetInt( "value" ) );

				meta.Set( "value", 2 );
				Assert.AreEqual( 2, meta.GetInt( "value" ), "A write inside the scope must not be hidden by the cached read" );

				using ( MetaData.CacheReads( meta ) )
				{
					meta.Set( "value", 3 );
				}

				Assert.AreEqual( 3, meta.GetInt( "value" ), "A write in a nested scope must not be hidden when it ends" );
			}

			File.WriteAllText( path, """{ "value": 4 }""" );
			Assert.AreEqual( 4, meta.GetInt( "value" ), "Outside a scope every read goes to disk" );
		}
		finally
		{
			File.Delete( path );
		}
	}
}
