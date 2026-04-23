/// <summary>
/// A mounting implementation for Natural Selection 2
/// </summary>
public class GameMount : BaseGameMount
{
	public override string Ident => "ns2";
	public override string Title => "Natural Selection 2";

	const long appId = 4920;
	string appDir;

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( appId ) )
			return;

		appDir = context.GetAppDirectory( appId );
		IsInstalled = System.IO.Directory.Exists( appDir );
	}

	private static readonly Dictionary<string, ResourceType> FileTypes = new()
	{
		{ ".model", ResourceType.Model },
		{ ".dds", ResourceType.Texture },
		{ ".material", ResourceType.Material },
		{ ".fsb", ResourceType.Sound },
	};

	protected override Task Mount( MountContext context )
	{
		foreach ( var fullPath in System.IO.Directory.GetFiles( appDir, "*.*", SearchOption.AllDirectories ) )
		{
			var ext = Path.GetExtension( fullPath )?.ToLower();
			if ( string.IsNullOrWhiteSpace( ext ) )
				continue;

			if ( !FileTypes.TryGetValue( ext, out var resourceType ) )
				continue;

			var path = Path.GetRelativePath( appDir, fullPath ).Replace( '\\', '/' );

			if ( resourceType == ResourceType.Model )
			{
				context.Add( resourceType, path, new ModelLoader( fullPath ) );
			}
			else if ( resourceType == ResourceType.Texture )
			{
				context.Add( resourceType, path, new DdsTextureLoader( fullPath ) );
			}
			else if ( resourceType == ResourceType.Material )
			{
				context.Add( resourceType, path, new MaterialLoader( fullPath ) );
			}
			else if ( resourceType == ResourceType.Sound )
			{
				SoundBankLoader.AddSoundsFromBank( context, fullPath, path );
			}
		}

		IsMounted = true;

		return Task.CompletedTask;
	}
}
