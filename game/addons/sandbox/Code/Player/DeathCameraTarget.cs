public partial class DeathCameraTarget : Component
{
	[Sync( SyncFlags.FromHost )]
	public Connection Connection { get; set; }
	public DateTime Created { get; set; }

	protected override void OnEnabled()
	{
		if ( !ServerSettings.KeepPlayerCorpses )
			Invoke( 60.0f, GameObject.Destroy );
	}
}
