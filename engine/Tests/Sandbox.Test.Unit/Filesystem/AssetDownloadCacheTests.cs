using System;
using System.IO;

namespace FilesystemTests;

[TestClass]
public class AssetDownloadCacheTests
{
	/// <summary>
	/// A crc can be forged to match, so what a game server sends never lands where package downloads look.
	/// </summary>
	[TestMethod]
	public void HostFilesAreKeptApartFromPackageFiles()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"assetcache_{Guid.NewGuid():N}" );
		AssetDownloadCache.Initialize( folder );

		try
		{
			byte[] data = [1, 2, 3, 4];
			var crc = Sandbox.Utility.Crc64.FromBytes( data );

			var stored = AssetDownloadCache.StoreFile( "maps/popular.vpk", crc, data, fromHost: true );

			Assert.IsTrue( File.Exists( stored ) );
			Assert.IsFalse( File.Exists( AssetDownloadCache.GetAbsolutePath( "maps/popular.vpk", crc ) ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}
}
