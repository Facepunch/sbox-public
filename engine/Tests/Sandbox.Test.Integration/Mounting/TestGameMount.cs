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
		context.Add( ResourceType.Scene, "/maps/testlevel", new TestGameSceneResource() );

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


/// <summary>
/// Builds a <see cref="SceneFile"/> entirely in code, as a mount would do for a level
/// that doesn't exist on disk as a scene.
/// </summary>
public class TestGameSceneResource : SceneLoader<TestGameMount>
{
	protected override void BuildScene()
	{
		var root = new GameObject( true, "MountedRoot" );
		root.AddComponent<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );

		var child = new GameObject( true, "MountedChild" );
		child.Parent = root;
	}
}
