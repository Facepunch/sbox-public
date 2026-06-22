using Sandbox;

public partial class TestRpc
{
	[ClientRpc]
	public void ToClient( string text, Vector3 cocks, float somethignElse, bool blah )
	{
		
	}

	[OwnerRpc]
	public void ToOwner( string text, Vector3 cocks, float somethignElse, bool blah )
	{

	}

	[ServerRpc]
	public void ToServer( string text, Entity ent, Material material )
	{

	}

	[ServerRpc]
	public static void ServerStatic( string text )
	{

	}
}
