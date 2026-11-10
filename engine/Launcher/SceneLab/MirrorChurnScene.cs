namespace Sandbox.SceneLab;

/// <summary>
/// Mirror Churn (Stress group): what a long session does to the bridge's caches. For <see cref="ChurnFrames"/> frames it
/// spawns props and citizens that live a second each, cycling through models, body groups, material groups, material
/// overrides and materials made at runtime, and changes the overrides of those alive; then it stops, and the scene empties.
/// Halfway it reloads a model and a material the props draw with. The window logs the mirror's cache counts as it goes
/// (<see cref="SceneLabScene.ReportMirror"/>), so growth shows as a number.
/// </summary>
internal static class MirrorChurnScene
{
	const int ChurnFrames = 3000;
	const int Lifetime = 60;
	const int PerFrame = 8;
	const int ReloadFrame = 1500;
	const int ReportEvery = 250;

	/// <summary>
	/// The whole script: the churn, the last objects going, and time for idle caches to empty.
	/// </summary>
	public const int Frames = ChurnFrames + Lifetime + 900;

	public const string ReloadedModel = "models/dev/box.vmdl";
	public const string ReloadedMaterial = "materials/dev/primary_red.vmat";

	static readonly string[] Materials =
	[
		"materials/dev/dev_metal_rough10.vmat", "materials/dev/primary_white_emissive.vmat", "materials/dev/black_grid_8.vmat",
		"materials/dev/dev_grating.vmat", "materials/dev/primary_blue.vmat", ReloadedMaterial,
	];

	static readonly string[] SkinGroups = ["default", "skin_light", "skin_dark"];

	static readonly List<(GameObject Object, int Dies)> alive = new();
	static int frame;
	static int spawned;

	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var _, new Vector3( -900, -700, 600 ) );
		Rewind( scene );
		return scene;
	}

	/// <summary>
	/// Back to the first frame, with nothing spawned.
	/// </summary>
	public static void Rewind( Scene scene )
	{
		foreach ( var (go, _) in alive )
		{
			if ( go.IsValid() ) go.Destroy();
		}

		alive.Clear();
		frame = 0;
		spawned = 0;
	}

	/// <summary>
	/// The script's frame: spawn, change and destroy by frame count, so every run churns the same.
	/// </summary>
	public static void Step( Scene scene )
	{
		frame++;

		for ( int i = alive.Count - 1; i >= 0; i-- )
		{
			if ( alive[i].Dies > frame ) continue;
			alive[i].Object.Destroy();
			alive.RemoveAt( i );
		}

		if ( frame == ReloadFrame )
		{
			NativeEngine.g_pResourceSystem.ReloadResource( ReloadedModel );
			NativeEngine.g_pResourceSystem.ReloadResource( ReloadedMaterial );
		}

		// The first frame draws the stage alone, for the caches' starting size
		if ( frame == 1 || frame > ChurnFrames ) return;

		// Game code changing what's alive: a new override every frame for some
		for ( int i = frame % 4; i < alive.Count; i += 4 )
		{
			if ( alive[i].Object.Components.Get<ModelRenderer>() is { } renderer && renderer is not SkinnedModelRenderer )
				renderer.MaterialOverride = Material.Load( Materials[(frame + i) % Materials.Length] );
		}

		for ( int i = 0; i < PerFrame; i++ )
			Spawn( spawned++ );
	}

	static void Spawn( int n )
	{
		var position = new Vector3( (n * 37 % 30 - 15) * 40, (n * 11 % 30 - 15) * 40, 0 );
		GameObject go;

		if ( n % 10 == 0 )
		{
			// A citizen: body groups and material groups and its skin overridden
			go = new GameObject( true, $"Citizen {n}" );
			go.WorldPosition = position;
			var citizen = go.Components.Create<SkinnedModelRenderer>();
			citizen.Model = Model.Load( "models/citizen_human/human.vmdl" );
			citizen.UseAnimGraph = false;
			citizen.PlaybackRate = 0;
			citizen.MaterialGroup = SkinGroups[n / 10 % SkinGroups.Length];
			citizen.SetBodyGroup( "Head", n / 30 % 8 );
			citizen.SetBodyGroup( "Feet", n / 240 % 3 );
			if ( n % 20 == 0 ) citizen.SetMaterialOverride( Material.Load( "models/citizen/skin/citizen_skin_grey.vmat" ), "skin" );
		}
		else
		{
			var models = HeavyScene.Models;
			var model = n % 5 == 1 ? ReloadedModel : $"models/citizen_props/{models[n % models.Length]}.vmdl";
			var prop = GameScenes.Prop( $"Prop {n}", model, position, new ColorHsv( n * 23 % 360, 0.5f, 1 ).ToColor() );
			go = prop.GameObject;

			switch ( n % 4 )
			{
				case 1:
					prop.MaterialOverride = Material.Load( Materials[n / 4 % Materials.Length] );
					break;
				case 2:
					prop.Materials.SetOverride( 0, Material.Load( Materials[n / 4 % Materials.Length] ) );
					break;
				case 3:
					// A material made at runtime, as game code makes them: a new one every time
					prop.MaterialOverride = Material.Load( Materials[n % Materials.Length] ).CreateCopy();
					break;
			}
		}

		alive.Add( (go, frame + Lifetime) );
	}

	/// <summary>
	/// The script's frame, on the frames whose mirror caches are logged: the first, every few hundred, and those after the
	/// reload and after the last object goes.
	/// </summary>
	public static string Report( Scene scene )
	{
		if ( frame == 1 ) return "frame 1, nothing spawned";
		if ( frame == ReloadFrame ) return $"frame {frame}, {ReloadedModel} and {ReloadedMaterial} reloaded";
		if ( frame == ChurnFrames + Lifetime ) return $"frame {frame}, emptied";
		return frame % ReportEvery == 0 ? $"frame {frame}{(frame <= ChurnFrames ? $", {alive.Count} alive" : "")}" : null;
	}
}
