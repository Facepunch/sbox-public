using Sandbox;

public partial class TestRpc
{
	[Net, Predicted, Change]
	public IEntity Weapon { get; set; }

	[Net, Predicted, Change]
	public Entity Bullet { get; set; }

	[Net, Predicted, Change]
	public IClient Shooter { get; set; }

}
