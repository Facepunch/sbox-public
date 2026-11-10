namespace Sandbox.Mounting;

internal class MountConfig
{
	internal static async Task Mount()
	{
		var list = EngineFileSystem.Config.ReadJsonOrDefault( "mounts.json", new string[0] );
		foreach ( var item in list )
		{
			await Directory.Mount( item );
		}
	}

	internal static void Save()
	{
		var list = Directory.GetAll().Where( x => x.Mounted ).Select( x => x.Ident );
		EngineFileSystem.Config.WriteJson( "mounts.json", list );
	}
}
