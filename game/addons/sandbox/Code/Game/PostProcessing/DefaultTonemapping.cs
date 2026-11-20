/// <summary>
/// Drives the game's default <see cref="Tonemapping"/> on the same GameObject (the system scene camera).
/// The default is disabled whenever the loaded scene provides its own global tonemapping, i.e. an enabled
/// <see cref="Tonemapping"/> that is on a camera or inside an infinite <see cref="PostProcessVolume"/>.
/// </summary>
[Title( "Default Tonemapping" ), Category( "Post Processing" ), Icon( "exposure" )]
public sealed class DefaultTonemapping : Component
{
	readonly List<Tonemapping> _tonemappings = new();

	protected override void OnUpdate()
	{
		var tonemapping = GetComponent<Tonemapping>( true );
		if ( !tonemapping.IsValid() ) return;

		_tonemappings.Clear();
		Scene.GetAll( _tonemappings );

		var enable = true;
		foreach ( var x in _tonemappings )
		{
			if ( x != tonemapping && IsGlobal( x ) )
			{
				enable = false;
				break;
			}
		}

		if ( tonemapping.Enabled != enable )
			tonemapping.Enabled = enable;
	}

	static bool IsGlobal( Tonemapping tonemapping )
	{
		var volume = tonemapping.GetComponentInParent<PostProcessVolume>( false, true );
		if ( volume is null ) return true; // On a camera, applies everywhere that camera renders

		return volume.SceneVolume.Type == Sandbox.Volumes.SceneVolume.VolumeTypes.Infinite;
	}
}
