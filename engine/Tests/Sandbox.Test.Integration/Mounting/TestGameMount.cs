using Sandbox;
using Sandbox.Engine;
using Sandbox.Mounting;
using System;

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
		context.Add( ResourceType.GameResource, "/surfaces/test.surface", new TestGameSurfaceResource() );
		context.Add( ResourceType.GameResource, "/resources/test.custom", new TestGameCustomResourceLoader() );
		context.Add( ResourceType.GameResource, "/resources/pending.custom", new TestGamePendingResource() );
		context.Add( ResourceType.GameResource, "/sounds/surface.sound", new TestGameSoundResource() );
		context.Add( ResourceType.GameResource, "/surfaces/collision_a/shared.surface", new TestGameSurfaceResource() );
		context.Add( ResourceType.GameResource, "/surfaces/collision_b/shared.surface", new TestGameSurfaceResource() );

		IsMounted = true;
		return Task.CompletedTask;
	}
}

public class TestGameSurfaceResource : ResourceLoader<TestGameMount>
{
	protected override async Task<object> LoadAsync() => new Surface
	{
		Friction = 1.25f,
		Elasticity = 0.4f,
		Density = 700.0f,
		SoundCollection = new()
		{
			FootLeft = await ResourceLibrary.LoadAsync<SoundEvent>( "mount://testgame/sounds/surface.sound" ),
			FootRight = await ResourceLibrary.LoadAsync<SoundEvent>( "mount://testgame/sounds/surface.sound" ),
			ImpactHard = await ResourceLibrary.LoadAsync<SoundEvent>( "mount://testgame/sounds/surface.sound" ),
			ScrapeRough = await ResourceLibrary.LoadAsync<SoundEvent>( "mount://testgame/sounds/surface.sound" )
		}
	};
}

public class TestGameSoundResource : ResourceLoader<TestGameMount>
{
	protected override object Load() => new SoundEvent { Sounds = [], Volume = 0.5f, Pitch = 1.25f };
}

public class TestGameCustomResource : GameResource
{
	public int Value { get; set; }
	public bool IsInitialized { get; private set; }

	protected override void PostLoad()
	{
		IsInitialized = true;
	}
}

public class TestGameCustomResourceLoader : ResourceLoader<TestGameMount>
{
	protected override object Load() => new TestGameCustomResource { Value = 42 };
}

public class TestGamePendingResource : ResourceLoader<TestGameMount>
{
	public TaskCompletionSource<object> Completion { get; } = new();

	protected override Task<object> LoadAsync() => Completion.Task;
}

internal sealed class MountedResourceTestContext : IDisposable
{
	readonly GlobalContext _context;
	GlobalContext.GlobalContextScope _scope;

	public MountedResourceTestContext()
	{
		var previous = GlobalContext.Current;
		_context = new GlobalContext
		{
			TypeLibrary = previous.TypeLibrary,
			NodeLibrary = previous.NodeLibrary,
			FileMount = previous.FileMount,
			JsonSerializerOptions = previous.JsonSerializerOptions
		};
		_scope = new GlobalContext.GlobalContextScope( _context );
	}

	public void Dispose()
	{
		try
		{
			_context.Shutdown();
		}
		finally
		{
			_scope.Dispose();
		}
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
