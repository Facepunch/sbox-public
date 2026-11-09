namespace Sandbox.SceneLab;

/// <summary>
/// A firefight on Flatgrass: two teams of 14 dressed citizens on the grass south of the building, driven by their animation
/// graphs, running between cover spots and stopping to shoot at each other in bursts. Every shot is traced against the map:
/// a muzzle flash (a sprite and a point light that lives two frames), dust and debris where it lands, and a decal that stays,
/// up to 256 of them. Each citizen but the followed one has a nameplate over its head (a world panel turned to the camera), and a HUD covers the
/// screen (screen panels: crosshair, health, ammo, score, kill feed). The camera follows one of the blue team over its
/// shoulder. All of it by frame count from a fixed seed, in a 600 frame loop.
/// </summary>
internal static class FirefightScene
{
	const int TeamSize = 14;
	const int LoopFrames = 600;
	const int MaxDecals = 256;
	const float BlueLine = -2350;
	const float RedLine = -1550;

	// The blue fighter the camera follows, from the middle of the line
	const int Lead = TeamSize / 2;

	// Their weapon is a stand-in: the repository has no gun models, so it's a dark box in the hand
	const string GunModel = "models/dev/box.vmdl";

	static readonly string[] DecalTextures =
	[
		"decals/impact/concrete/decal_impact_concrete_01_color.png", "decals/impact/concrete/decal_impact_concrete_02_color.png",
		"decals/impact/dirt/decal_impact_dirt_01_color.png", "decals/impact/dirt/decal_impact_dirt_02_color.png",
	];

	sealed class Fighter
	{
		public SkinnedModelRenderer Body;
		public ModelRenderer Gun;
		public GameObject Muzzle;
		public PointLight Flash;
		public WorldPanel Plate;
		public Vector3 A, B;
		public int Phase;
		public int Team;
		public int Target;
		public int FlashFrames;
	}

	static Fighter[] fighters = [];
	static ParticleEffect dust, debris, flashes;
	static readonly List<Decal> decals = [];
	static DecalDefinition[] decalDefinitions = [];
	static Sandbox.UI.Label ammo, score, health;
	static Sandbox.UI.Label[] feed = [];
	static Random random;
	static bool grounded;
	static int frame, decalIndex, rounds;

	public static Scene Build()
	{
		var scene = new Scene();
		using var _ = scene.Push();

		var cameraObject = new GameObject( true, "Camera" );
		var camera = cameraObject.Components.Create<CameraComponent>();
		camera.FieldOfView = 85;
		camera.FovAxis = CameraComponent.Axis.Horizontal;
		camera.ZNear = 5;
		camera.ZFar = 30000;
		camera.IsMainCamera = true;
		var tonemapping = cameraObject.Components.Create<Tonemapping>();
		tonemapping.Mode = Tonemapping.TonemappingMode.ACES;
		tonemapping.AutoExposureEnabled = false;
		cameraObject.Components.Create<Bloom>();

		new GameObject( true, "facepunch.flatgrass" ).Components.Create<MapInstance>();

		var seed = new Random( 3131 );
		fighters = new Fighter[TeamSize * 2];
		for ( int i = 0; i < fighters.Length; i++ )
		{
			var team = i / TeamSize;
			var slot = i % TeamSize;
			var x = (slot - (TeamSize - 1) / 2.0f) * 140 + (seed.NextSingle() - 0.5f) * 60;
			var line = team == 0 ? BlueLine : RedLine;
			var a = new Vector3( x, line + (seed.NextSingle() - 0.5f) * 200, 0 );
			var b = a + new Vector3( (seed.NextSingle() - 0.5f) * 600, (seed.NextSingle() - 0.5f) * 300, 0 );

			var body = Scenarios.AnimatedCitizen( $"{(team == 0 ? "Blue" : "Red")} {slot}", a, team == 0 ? 90 : -90, seed );
			Scenarios.Set( body, "holdtype", "rifle" );

			var gun = GameScenes.Prop( $"{body.GameObject.Name} Gun", GunModel, a, new Color( 0.08f, 0.08f, 0.09f ) );
			gun.GameObject.WorldScale = new Vector3( 26, 2, 4.5f ) / Model.Load( GunModel ).Bounds.Size.x;

			var muzzle = new GameObject( true, $"{body.GameObject.Name} Muzzle" );
			var flash = muzzle.Components.Create<PointLight>();
			flash.LightColor = new Color( 1, 0.75f, 0.4f ) * 6;
			flash.Radius = 260;
			flash.Shadows = false;
			flash.Enabled = false;

			var color = team == 0 ? new Color( 0.3f, 0.6f, 1 ) : new Color( 1, 0.35f, 0.3f );
			fighters[i] = new Fighter
			{
				Body = body,
				Gun = gun,
				Muzzle = muzzle,
				Flash = flash,
				A = a,
				B = b,
				Team = team,
				// The followed fighter's own is hidden, as a game hides the local player's
				Plate = i == Lead ? null : Scenarios.Nameplate( body.GameObject, $"{(team == 0 ? "Blue" : "Red")} {slot + 1}", 88, color ),
				Phase = seed.Next( LoopFrames ),
				Target = (team == 0 ? TeamSize : 0) + (slot * 5 + 3) % TeamSize,
			};
		}

		dust = Scenarios.Particles( "Impact Dust", "textures/particles/smoke/smoke_static_a.vtex", 1500, lit: true, alpha: 0.55f, grow: 3.5f, gravity: -20, damping: 2 );
		debris = Scenarios.Particles( "Impact Debris", "textures/particles/particle_debris_02.vtex", 1500, lit: true, gravity: 700 );
		flashes = Scenarios.Particles( "Muzzle Flashes", "textures/particles/muzzleflash/muzzleflash.vtex", 128, additive: true, grow: 1.6f );

		decalDefinitions = DecalTextures.Select( x => new DecalDefinition { ColorTexture = Texture.Load( x ), Width = 8, Height = 8, ColorMix = 0.8f } ).ToArray();
		decals.Clear();

		Hud();

		grounded = false;
		Rewind( scene );
		return scene;
	}

	/// <summary>
	/// A shooter's HUD: crosshair, health, ammo, the score and a kill feed.
	/// </summary>
	static void Hud()
	{
		var go = new GameObject( true, "HUD" );
		var root = go.Components.Create<ScreenPanel>().GetPanel();
		root.StyleSheet.Add( Sandbox.UI.StyleSheet.FromString( HudStyles, "/scenelab/firefight.scss" ) );
		var hud = root.Add.Panel( "hud" );

		var cross = hud.Add.Panel( "cross" );
		cross.Add.Panel( "h" );
		cross.Add.Panel( "v" );

		var vitals = hud.Add.Panel( "vitals" );
		vitals.Add.Label( "HEALTH", "caption" );
		health = vitals.Add.Label( "100", "big" );
		vitals.Add.Panel( "bar" ).Add.Panel( "fill" );

		var weapon = hud.Add.Panel( "weapon" );
		weapon.Add.Label( "RIFLE", "caption" );
		ammo = weapon.Add.Label( "30 / 120", "big" );

		score = hud.Add.Label( "BLUE 0 - 0 RED", "score" );

		var killfeed = hud.Add.Panel( "feed" );
		feed = Enumerable.Range( 0, 5 ).Select( _ => killfeed.Add.Label( "", "kill" ) ).ToArray();
	}

	const string HudStyles = """
		.hud { position: absolute; left: 0; top: 0; right: 0; bottom: 0; font-family: Poppins; color: white; }
		.cross { position: absolute; left: 50%; top: 50%; width: 30px; height: 30px; margin-left: -15px; margin-top: -15px; }
		.h { position: absolute; left: 0; top: 14px; width: 30px; height: 2px; background-color: rgba( 255, 255, 255, 0.8 ); }
		.v { position: absolute; left: 14px; top: 0; width: 2px; height: 30px; background-color: rgba( 255, 255, 255, 0.8 ); }
		.vitals, .weapon { position: absolute; bottom: 40px; padding: 14px 22px; flex-direction: column;
			background-color: rgba( 0, 0, 0, 0.45 ); border-radius: 10px; }
		.vitals { left: 40px; width: 260px; }
		.weapon { right: 40px; align-items: flex-end; }
		.caption { font-size: 16px; color: rgba( 255, 255, 255, 0.6 ); }
		.big { font-size: 48px; font-weight: 700; }
		.bar { height: 10px; border-radius: 5px; background-color: rgba( 255, 255, 255, 0.2 ); }
		.fill { width: 82%; height: 100%; border-radius: 5px; background-color: #5ad16a; }
		.score { position: absolute; top: 24px; left: 0; right: 0; text-align: center; font-size: 30px; font-weight: 700;
			text-shadow: 0 2px 4px black; }
		.feed { position: absolute; top: 24px; right: 30px; flex-direction: column; align-items: flex-end; gap: 6px; }
		.kill { font-size: 18px; padding: 4px 10px; background-color: rgba( 0, 0, 0, 0.5 ); border-radius: 4px; }
		""";

	/// <summary>
	/// The map has loaded and everyone stands on its ground.
	/// </summary>
	public static bool Ready( Scene scene ) => MapScenes.Loaded( scene ) && grounded;

	public static void Rewind( Scene scene )
	{
		frame = 0;
		rounds = 0;
		random = new Random( 77 );
		decalIndex = 0;
		foreach ( var decal in decals ) decal.GameObject.Destroy();
		decals.Clear();
		Scenarios.Clear( dust );
		Scenarios.Clear( debris );
		Scenarios.Clear( flashes );

		foreach ( var fighter in fighters )
		{
			fighter.FlashFrames = 0;
			if ( fighter.Flash.IsValid() ) fighter.Flash.Enabled = false;
		}
	}

	/// <summary>
	/// A frame of the fight, once the map is in: everyone runs or shoots, the camera follows Blue 8, the HUD updates.
	/// </summary>
	public static void Fight( Scene scene )
	{
		MapScenes.StartWhenOnline( scene );
		if ( !MapScenes.Loaded( scene ) ) return;

		if ( !grounded )
		{
			foreach ( var fighter in fighters )
			{
				fighter.A = Ground( scene, fighter.A );
				fighter.B = Ground( scene, fighter.B );
			}

			grounded = true;
			Rewind( scene );
		}

		var f = frame++;
		for ( int i = 0; i < fighters.Length; i++ ) Act( scene, fighters[i], (f + fighters[i].Phase) % LoopFrames, f );

		if ( scene.Camera is { } camera )
		{
			var lead = fighters[Lead].Body.WorldPosition;
			camera.WorldPosition = lead + new Vector3( 45, -150, 95 );
			camera.WorldRotation = Rotation.LookAt( new Vector3( lead.x * 0.6f, RedLine, lead.z + 20 ) - camera.WorldPosition );
			Scenarios.FaceCamera( fighters.Select( x => x.Plate ), camera.WorldPosition );
		}

		UpdateHud( f );
	}

	static Vector3 Ground( Scene scene, Vector3 at )
	{
		var trace = scene.Trace.Ray( at.WithZ( 3000 ), at.WithZ( -3000 ) ).Run();
		return trace.Hit ? trace.HitPosition : at;
	}

	/// <summary>
	/// One fighter's frame, <paramref name="t"/> into its own loop: run A to B, shoot, run back, shoot.
	/// </summary>
	static void Act( Scene scene, Fighter fighter, int t, int f )
	{
		var body = fighter.Body;
		if ( !body.IsValid() ) return;

		const int Run = 110;
		const int Half = LoopFrames / 2;
		var leg = t % Half;
		var outward = t < Half;
		var from = outward ? fighter.A : fighter.B;
		var to = outward ? fighter.B : fighter.A;

		var target = fighters[fighter.Target].Body;
		var shooting = leg >= Run;

		if ( !shooting )
		{
			var velocity = (to - from) / (Run / 60.0f);
			body.WorldPosition = Vector3.Lerp( from, to, leg / (float)Run );
			body.WorldRotation = Rotation.LookAt( velocity.WithZ( 0 ) );
			Scenarios.Move( body, velocity );
			Scenarios.Set( body, "b_attack", false );
			Hold( fighter );
		}
		else
		{
			body.WorldPosition = to;
			Scenarios.Move( body, Vector3.Zero );
			var aim = (target.WorldPosition - body.WorldPosition).WithZ( 0 );
			body.WorldRotation = Rotation.LookAt( aim );
			Hold( fighter );

			// Bursts of three, a shot every five frames, then a pause
			var firing = (leg - Run) % 40 < 15 && (leg - Run) % 5 == 0;
			Scenarios.Set( body, "b_attack", firing );
			if ( firing ) Shoot( scene, fighter, target, fighter == fighters[Lead] );
		}

		if ( fighter.FlashFrames > 0 && --fighter.FlashFrames == 0 ) fighter.Flash.Enabled = false;
	}

	/// <summary>
	/// The gun into the hand, as a weapon's world model follows its holder's hold bone, and the muzzle at its end.
	/// </summary>
	static void Hold( Fighter fighter )
	{
		if ( !fighter.Body.TryGetBoneTransform( "hold_R", out var hand ) ) return;

		fighter.Gun.WorldPosition = hand.PointToWorld( new Vector3( 8, 0, 2 ) );
		fighter.Gun.WorldRotation = hand.Rotation;
		fighter.Muzzle.WorldPosition = hand.PointToWorld( new Vector3( 22, 0, 3 ) );
	}

	/// <summary>
	/// A shot: the flash at the muzzle, and where the trace lands, dust, debris and a decal.
	/// </summary>
	static void Shoot( Scene scene, Fighter fighter, SkinnedModelRenderer target, bool lead )
	{
		if ( lead ) rounds++;

		var muzzle = fighter.Muzzle.WorldPosition;
		fighter.Flash.Enabled = true;
		fighter.FlashFrames = 2;
		Scenarios.Emit( flashes, muzzle, Vector3.Zero, 14 + random.NextSingle() * 8, random.NextSingle() * 360, Color.White, 0.05f );

		// At the target's middle, spread so most go past it into the ground or the building behind
		var aim = target.WorldPosition + new Vector3( (random.NextSingle() - 0.5f) * 90, (random.NextSingle() - 0.5f) * 90, 10 + random.NextSingle() * 50 );
		var direction = (aim - muzzle).Normal;
		var trace = scene.Trace.Ray( muzzle, muzzle + direction * 5000 ).Run();
		if ( !trace.Hit ) return;

		var hit = trace.HitPosition + trace.Normal * 0.5f;
		for ( int i = 0; i < 4; i++ )
		{
			var spray = (trace.Normal + new Vector3( random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f )).Normal;
			Scenarios.Emit( dust, hit, spray * (30 + random.NextSingle() * 60), 10 + random.NextSingle() * 10, random.NextSingle() * 360, new Color( 0.55f, 0.5f, 0.42f ), 1.2f + random.NextSingle() );
			Scenarios.Emit( debris, hit, spray * (150 + random.NextSingle() * 200), 2 + random.NextSingle() * 2, random.NextSingle() * 360, new Color( 0.4f, 0.37f, 0.3f ), 0.8f );
		}

		AddDecal( scene, trace.HitPosition, trace.Normal );
	}

	/// <summary>
	/// A bullet hole where a shot landed: a new decal until there are <see cref="MaxDecals"/>, then the oldest moved.
	/// </summary>
	static void AddDecal( Scene scene, Vector3 position, Vector3 normal )
	{
		Decal decal;
		if ( decals.Count < MaxDecals )
		{
			decal = new GameObject( true, $"Bullet Hole {decals.Count}" ).Components.Create<Decal>();
			decal.Depth = 6;
			decals.Add( decal );
		}
		else
		{
			decal = decals[decalIndex++ % MaxDecals];
		}

		decal.Decals = [decalDefinitions[random.Next( decalDefinitions.Length )]];
		decal.Rotation = random.NextSingle() * 360;
		decal.Size = new Vector2( 0.8f + random.NextSingle() * 0.5f );
		decal.WorldPosition = position;
		decal.WorldRotation = Rotation.LookAt( -normal );
	}

	static readonly string[] Weapons = ["rifle", "smg", "pistol", "shotgun"];

	static void UpdateHud( int f )
	{
		ammo.Text = $"{30 - rounds % 30} / {120 - rounds / 30 * 30 % 120}";
		health.Text = $"{100 - f / 60 * 3 % 70}";

		// A kill every 70 frames, scrolling the feed
		var kills = f / 70;
		score.Text = $"BLUE {(kills + 1) / 2} - {kills / 2} RED";
		for ( int i = 0; i < feed.Length; i++ )
		{
			var k = kills - i;
			feed[i].Text = k <= 0 ? "" : $"{(k % 2 == 0 ? "Red" : "Blue")} {k % TeamSize + 1}  [{Weapons[k % Weapons.Length]}]  {(k % 2 == 0 ? "Blue" : "Red")} {k * 5 % TeamSize + 1}";
		}
	}
}
