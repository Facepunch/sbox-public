using Sandbox;

namespace FilesystemTests;

[TestClass]
public class LegalDownloadTests
{
	[DataTestMethod]
	[DataRow( ".bin/package.facepunch.sandbox.dll" )]
	[DataRow( ".bin/package.facepunch.sandbox.dll " )]
	[DataRow( ".bin/package.facepunch.sandbox.dll." )]
	[DataRow( ".bin/package.facepunch.sandbox.dll::$DATA" )]
	[DataRow( ".bin/package.facepunch.sandbox.DLL" )]
	[DataRow( "models/thing.exe" )]
	[DataRow( "models/../thing.vmdl_c" )]
	public void Executable_Or_Aliasing_Name_Is_Rejected( string path )
	{
		Assert.IsFalse( AssetDownloadCache.IsLegalDownload( path ) );
	}

	[DataTestMethod]
	[DataRow( "models/citizen/citizen.vmdl_c" )]
	[DataRow( "sounds/footstep.sound_c" )]
	[DataRow( "textures/thing.png" )]
	[DataRow( ".bin/package.facepunch.sandbox.cll" )]
	public void Asset_Is_Allowed( string path )
	{
		Assert.IsTrue( AssetDownloadCache.IsLegalDownload( path ) );
	}
}
