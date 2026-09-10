using Sandbox;

public partial class TestRpc
{
	[ClientRpc]
	public void ToClient( Entity ent, IEntity ientity, IClient iclient )
	{
		
	}

	[OwnerRpc]
	public void ToOwner( Entity ent, IEntity ientity, IClient iclient )
	{

	}

	[ServerRpc]
	public void ToServer( Entity ent, IEntity ientity, IClient iclient )
	{

	}

	[ServerRpc]
	public static void ServerStatic( Entity ent, IEntity ientity, IClient iclient )
	{

	}
}
