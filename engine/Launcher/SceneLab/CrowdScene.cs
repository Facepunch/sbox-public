namespace Sandbox.SceneLab;

/// <summary>
/// A crowd in a plaza: 240 citizens, each dressed in layers from a fixed seed, driven by their animation graphs - most standing
/// about, a third walking slow circles - with their faces moving through a few morphs (blinks, brows, mouths). Seen from three
/// distances, one preset each: among them, across the plaza, and from far off, so their LODs differ. The camera drifts a little,
/// by frame count, so what's culled and which LOD changes.
/// </summary>
internal static class CrowdScene
{
	const int CitizenCount = 240;
	const float Extent = 700;
	const int LoopFrames = 600;

	static SkinnedModelRenderer[] citizens = [];
	static Vector3[] homes = [];
	static float[] walkRadii = [];
	static string[][] morphs = [];
	static Vector3 eye, focus;
	static int frame;

	public static Scene Near() => Build( new Vector3( -760, -120, 64 ), new Vector3( 0, 0, 55 ) );
	public static Scene Mid() => Build( new Vector3( -1500, -900, 520 ), new Vector3( 0, 0, 40 ) );
	public static Scene Far() => Build( new Vector3( -3600, -2200, 900 ), new Vector3( 0, 0, 40 ) );

	static Scene Build( Vector3 cameraPosition, Vector3 lookAt )
	{
		var scene = GameScenes.Stage( out var camera, cameraPosition, lookAt );
		using var _ = scene.Push();

		camera.ZFar = 12000;
		var floor = scene.Directory.FindByName( "Floor" ).First();
		floor.WorldScale = new Vector3( 60, 60, 1 );
		floor.Components.Get<ModelRenderer>().MaterialOverride = Material.Load( "materials/dev/gray_50.vmat" );
		eye = cameraPosition;
		focus = lookAt;

		var random = new Random( 4242 );
		citizens = new SkinnedModelRenderer[CitizenCount];
		homes = new Vector3[CitizenCount];
		walkRadii = new float[CitizenCount];
		morphs = new string[CitizenCount][];

		for ( int i = 0; i < CitizenCount; i++ )
		{
			// Loose groups, denser in the middle
			var angle = random.NextSingle() * MathF.Tau;
			var distance = MathF.Sqrt( random.NextSingle() ) * Extent;
			homes[i] = new Vector3( MathF.Cos( angle ) * distance, MathF.Sin( angle ) * distance, 0 );
			walkRadii[i] = i % 3 == 0 ? 40 + random.NextSingle() * 80 : 0;

			var citizen = Scenarios.AnimatedCitizen( $"Citizen {i}", homes[i], random.NextSingle() * 360, random );
			citizens[i] = citizen;

			var names = citizen.Model.Morphs.Names;
			morphs[i] = names.Length == 0 ? [] : Enumerable.Range( 0, 4 ).Select( x => names[(i * 7 + x * 13) % names.Length] ).ToArray();
		}

		var tonemapping = camera.GameObject.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;

		Rewind( scene );
		return scene;
	}

	public static void Rewind( Scene scene )
	{
		frame = 0;
		Animate( scene );
	}

	/// <summary>
	/// A frame of the crowd: walkers on their circles, faces moving, the camera drifting.
	/// </summary>
	public static void Animate( Scene scene )
	{
		var t = frame++ % LoopFrames / 60.0f;

		for ( int i = 0; i < citizens.Length; i++ )
		{
			var citizen = citizens[i];
			if ( !citizen.IsValid() ) continue;

			if ( walkRadii[i] > 0 )
			{
				// One lap a loop, each from its own start, half of them the other way
				var direction = i % 2 == 0 ? 1 : -1;
				var angle = direction * t * MathF.Tau / (LoopFrames / 60.0f) + i;
				var offset = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * walkRadii[i];
				var velocity = new Vector3( -MathF.Sin( angle ), MathF.Cos( angle ), 0 ) * direction * walkRadii[i] * MathF.Tau / (LoopFrames / 60.0f);
				citizen.WorldPosition = homes[i] + offset;
				citizen.WorldRotation = Rotation.LookAt( velocity );
				Scenarios.Move( citizen, velocity );
			}

			for ( int m = 0; m < morphs[i].Length; m++ )
			{
				var weight = MathF.Max( 0, MathF.Sin( t * (1.5f + m * 0.7f) + i * 0.37f + m ) );
				citizen.Morphs.Set( morphs[i][m], weight * 0.8f, 0 );
			}
		}

		// A slow sideways sway around where the camera started
		if ( scene.Camera is { } camera )
		{
			var sway = MathF.Sin( t * MathF.Tau / (LoopFrames / 60.0f) ) * (eye - focus).Length * 0.08f;
			var side = Rotation.LookAt( focus - eye ).Right;
			camera.WorldPosition = eye + side * sway;
			camera.WorldRotation = Rotation.LookAt( focus - camera.WorldPosition );
		}
	}
}
