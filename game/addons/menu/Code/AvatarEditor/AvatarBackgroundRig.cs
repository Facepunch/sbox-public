using Sandbox;

public sealed class AvatarBackgroundRig : Component
{
	[Property]
	public string Title { get; set; }

	[Property]
	public Texture Icon { get; set; }


	protected override void OnEnabled()
	{
		base.OnEnabled();
	}

	public void SetActiveRig()
	{
		foreach ( var r in Scene.GetAll<AvatarBackgroundRig>() )
			r.GameObject.Enabled = false;

		GameObject.Enabled = true;
		Game.Cookies.Set( "avatar.rig", GameObject.Id );
	}

	public static void RestoreSaved()
	{
		var objectId = Game.Cookies.Get<Guid>( "avatar.rig", Guid.Empty );
		if ( objectId == Guid.Empty )
			return;

		var found = Game.ActiveScene.GetAllObjects( false ).Where( x => x.Id == objectId ).FirstOrDefault();
		if ( found == null ) return;


		found.GetComponent<AvatarBackgroundRig>( true )?.SetActiveRig();
	}
}
