using Sandbox;
using Sandbox.Mounting;

/// <summary>
/// A mounting implementation for Quake
/// </summary>
public partial class TestGameMount : Sandbox.Mounting.BaseGameMount
{
	public override string Ident => "testgame";
	public override string Title => "Test Game";


	protected override void Initialize( InitializeContext context )
	{
		IsInstalled = true;
		return;
	}

	protected override Task Mount( MountContext context )
	{
		context.Add( ResourceType.Texture, "/gfx/sprites/mario.png", new TestGameTextureResource() );

		IsMounted = true;
		return Task.CompletedTask;
	}
}


public class TestGameTextureResource : ResourceLoader<TestGameMount>
{
	public TestGameTextureResource()
	{

	}

	protected override object Load()
	{
		using var bitmap = new Bitmap( 128, 128 );
		bitmap.Clear( Color.Random );

		return bitmap.ToTexture();
	}

}
