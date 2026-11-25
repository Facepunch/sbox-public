/// <summary>
/// A categorized sound resource for use on spawned entities (thrusters, hoverballs, emitters, etc.).
/// The <see cref="Category"/> field allows the editor to filter definitions by type.
/// </summary>
[AssetType( Name = "Sound Definition", Extension = "sndef", Category = "Sandbox" )]
public class SoundDefinition : GameResource, IDefinitionResource, IResourcePreview
{
	public const string Thruster = "thruster";
	public const string Hoverball = "hoverball";
	public const string Emitter = "emitter";

	/// <summary>
	/// Display name for this sound definition.
	/// </summary>
	[Property]
	public string Title { get; set; }

	/// <summary>
	/// Optional description of the sound.
	/// </summary>
	[Property]
	public string Description { get; set; }

	/// <summary>
	/// Category tag used for filtering in the resource picker (e.g. "thruster", "hoverball").
	/// </summary>
	[Property]
	public string Category { get; set; }

	/// <summary>
	/// The underlying sound event to play.
	/// </summary>
	[Property]
	public SoundEvent Sound { get; set; }

	/// <summary>
	/// Plays the sound at the given world position. Returns default if no sound is set.
	/// </summary>
	public SoundHandle Play( Vector3 position )
	{
		return Sound is not null ? Sandbox.Sound.Play( Sound, position ) : default;
	}

	/// <summary>
	/// Plays the sound at the given world position, parented to a GameObject. Returns default if no sound is set.
	/// </summary>
	public SoundHandle Play( Vector3 position, GameObject parent )
	{
		if ( Sound is null ) return default;

		var handle = Sandbox.Sound.Play( Sound, position );
		handle.Parent = parent;
		handle.FollowParent = true;
		return handle;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "🔊", width, height, "#4a90d9" );
	}

	private SoundHandle _previewHandle;

	public void OnPreview()
	{
		OnPreviewStop();

		if ( Sound is null ) return;
		_previewHandle = Sandbox.Sound.Play( Sound );
	}

	public void OnPreviewStop()
	{
		if ( _previewHandle.IsValid() )
		{
			_previewHandle.Stop();
			_previewHandle = default;
		}
	}
}
