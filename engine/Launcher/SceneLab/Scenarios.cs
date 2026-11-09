using System.Threading.Tasks;

namespace Sandbox.SceneLab;

/// <summary>
/// What the Scenario presets share: game-like scenes measured as a whole frame, scripted by frame count so every run sees the
/// same frames. Each keeps a frame counter that its <c>Frame</c> hook advances and its <c>Rewind</c> resets, which the
/// benchmark calls as it starts measuring, so both renderers measure the same stretch of the script.
/// </summary>
internal static class Scenarios
{
	/// <summary>
	/// The Scene menu's submenu they're in.
	/// </summary>
	public const string Group = "Scenarios";

	/// <summary>
	/// An outfit for a citizen from a fixed seed: a body, hair, a shirt, often a jacket or jumper over it, trousers or shorts,
	/// shoes, and sometimes a hat, glasses or gloves - layered clothing, bone merged onto the body.
	/// </summary>
	public static ClothingContainer RandomOutfit( Random random )
	{
		var paths = new List<string>
		{
			Pick( random, Bodies ),
			Pick( random, Hair ),
			Pick( random, Shirts ),
			Pick( random, random.Next( 3 ) == 0 ? Shorts : Trousers ),
			Pick( random, Shoes ),
		};

		if ( random.Next( 3 ) != 0 ) paths.Add( Pick( random, Jackets ) );
		if ( random.Next( 3 ) == 0 ) paths.Add( Pick( random, Hats ) );
		if ( random.Next( 5 ) == 0 ) paths.Add( Pick( random, Extras ) );

		return BridgeScene.Outfit( paths );
	}

	static string Pick( Random random, string[] list ) => list[random.Next( list.Length )];

	const string Clothes = "models/citizen_clothes";

	static readonly string[] Bodies = Enumerable.Range( 1, 11 ).Select( x => $"{Clothes}/body/human{x:00}.clothing" ).ToArray();

	static readonly string[] Hair =
	[
		$"{Clothes}/hair/hair_bobcut/hair_bobcut.clothing", $"{Clothes}/hair/hair_longbob/hair_longbob.clothing",
		$"{Clothes}/hair/hair_messy_bun/hair_messy_bun.clothing", $"{Clothes}/hair/hair_shortscruffy/hair_shortscruffy_brown.clothing",
		$"{Clothes}/hair/hair_slickedback/hair_slickedback.clothing", $"{Clothes}/hair/hair_tight_ponytail/hair_tight_ponytail_brown.clothing",
		$"{Clothes}/hair/hair_short_braids/hair_short_braids.clothing", $"{Clothes}/hair/hair_longcurly/hair_longcurly_brown.clothing",
		$"{Clothes}/hair/medium_beard/medium_beard.clothing", $"{Clothes}/hair/hair_thinning_short/thinning_short.clothing",
	];

	static readonly string[] Shirts =
	[
		$"{Clothes}/shirt/Tshirt/tshirt.clothing", $"{Clothes}/shirt/Tshirt/football_tshirt.clothing", $"{Clothes}/shirt/Polo_Shirt/polo_shirt.clothing",
		$"{Clothes}/shirt/Flannel_Shirt/flannel_shirt.clothing", $"{Clothes}/shirt/Army_Shirt/army_shirt.clothing", $"{Clothes}/shirt/Loose_Shirt/loose_shirt.clothing",
		$"{Clothes}/shirt/Hawaiian_Shirt/Hawaiian Shirt.clothing", $"{Clothes}/shirt/Tanktop/tanktop.clothing",
	];

	static readonly string[] Jackets =
	[
		$"{Clothes}/jacket/Hoodie/hoodie.clothing", $"{Clothes}/jacket/Hoodie/hoodie_black.clothing", $"{Clothes}/jacket/Bomber_Jacket/bomber_jacket.clothing",
		$"{Clothes}/jacket/Puffer_Jacket/puffer_jacket_blue.clothing", $"{Clothes}/jacket/Raincoat/raincoat.clothing", $"{Clothes}/jacket/Wind_Breaker/wind_breaker.clothing",
		$"{Clothes}/jacket/Brown_Leather_Jacket/brown_leather_jacket.clothing", $"{Clothes}/jumper/cardigan/cardigan.clothing",
		$"{Clothes}/jumper/loose_jumper/loose_jumper_wooly.clothing", $"{Clothes}/vest/Tactical_Vest/Models/tactical_vest.clothing",
	];

	static readonly string[] Trousers =
	[
		$"{Clothes}/trousers/Jeans/jeans.clothing", $"{Clothes}/trousers/Jeans/jeans_black.clothing", $"{Clothes}/trousers/CargoPants/cargo_pants_army.clothing",
		$"{Clothes}/trousers/LooseJeans/loose_jeans.clothing", $"{Clothes}/trousers/TrackieBottoms/trackie_bottoms.clothing",
		$"{Clothes}/trousers/SmartTrousers/trousers.smart.clothing",
	];

	static readonly string[] Shorts = [$"{Clothes}/shorts/shorts/shorts.clothing", $"{Clothes}/shorts/summer_shorts/Summer Shorts.clothing"];

	static readonly string[] Shoes =
	[
		$"{Clothes}/shoes/Trainers/trainers.clothing", $"{Clothes}/shoes/Sneakers/sneakers.clothing", $"{Clothes}/shoes/Boots/army_boots.clothing",
		$"{Clothes}/shoes/Boots/black_boots.clothing", $"{Clothes}/shoes/Football_Shoes/football_shoes.clothing",
	];

	static readonly string[] Hats =
	[
		$"{Clothes}/hat/Baseball_Cap/baseball_cap.clothing", $"{Clothes}/hat/Beanie/beanie_red.clothing", $"{Clothes}/hat/Bucket_Hat/bucket_hat.clothing",
		$"{Clothes}/hat/Fedora/fedora.clothing", $"{Clothes}/hat/Flatcap/flatcap.clothing",
	];

	static readonly string[] Extras =
	[
		$"{Clothes}/glasses/Nerdy_Glasses/nerdy_glasses.clothing", $"{Clothes}/gloves/fingerless_gloves/fingerless_gloves.clothing",
		$"{Clothes}/necklace/vintage_necklace/vintage_necklace.clothing",
	];

	/// <summary>
	/// A citizen driven by its animation graph, as a game's players and NPCs are, dressed from <paramref name="random"/>.
	/// </summary>
	public static SkinnedModelRenderer AnimatedCitizen( string name, Vector3 position, float yaw, Random random )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = position;
		go.WorldRotation = Rotation.FromYaw( yaw );

		var body = go.Components.Create<SkinnedModelRenderer>();
		body.Model = Model.Load( "models/citizen_human/human.vmdl" );
		body.UseAnimGraph = true;
		BridgeScene.Dress( RandomOutfit( random ), body );
		Set( body, "b_grounded", true );
		Set( body, "variation_seed", random.Next( 100000 ) );
		return body;
	}

	/// <summary>
	/// Set a citizen's animation graph moving at <paramref name="velocity"/> (world space), as a game's animation helper does
	/// from its character controller.
	/// </summary>
	public static void Move( SkinnedModelRenderer body, Vector3 velocity )
	{
		var local = body.WorldRotation.Inverse * velocity;
		var speed = velocity.WithZ( 0 ).Length;
		Set( body, "move_x", local.x );
		Set( body, "move_y", local.y );
		Set( body, "move_z", 0f );
		Set( body, "move_groundspeed", speed );
		Set( body, "wish_x", local.x );
		Set( body, "wish_y", local.y );
		Set( body, "wish_groundspeed", speed );
	}

	/// <summary>
	/// An animation graph parameter, through the graph's parameters (the citizen's <c>human.ag</c>), or the old graph's.
	/// </summary>
	public static void Set( SkinnedModelRenderer body, string name, float value )
	{
		if ( body.Animation.Graph is not null ) body.Animation.Parameters.TrySet( name, value );
#pragma warning disable CS0618 // The old animation graph, for a model without the new one
		else body.Set( name, value );
#pragma warning restore CS0618
	}

	/// <inheritdoc cref="Set(SkinnedModelRenderer, string, float)"/>
	public static void Set( SkinnedModelRenderer body, string name, bool value )
	{
		if ( body.Animation.Graph is not null ) body.Animation.Parameters.TrySet( name, value );
#pragma warning disable CS0618 // The old animation graph, for a model without the new one
		else body.Set( name, value );
#pragma warning restore CS0618
	}

	/// <inheritdoc cref="Set(SkinnedModelRenderer, string, float)"/>
	public static void Set( SkinnedModelRenderer body, string name, string value )
	{
		if ( body.Animation.Graph is not null ) body.Animation.Parameters.TrySet( name, value );
#pragma warning disable CS0618 // The old animation graph, for a model without the new one
		else body.Set( name, value );
#pragma warning restore CS0618
	}

	/// <summary>
	/// A package's files mounted - a map's models and materials, with no map loaded - once the backend is up, through the same
	/// cloud path a map preset's <see cref="MapInstance"/> fetches its map. Fetched once a run.
	/// </summary>
	public static bool Mounted( string ident )
	{
		if ( !PanelAppSystem.ApiReady.IsCompleted ) return false;

		if ( !mounts.TryGetValue( ident, out var task ) )
		{
			MapScenes.EnableDownloads();
			mounts[ident] = task = Mount( ident );
		}

		return task.IsCompleted;
	}

	static readonly Dictionary<string, Task<bool>> mounts = new();

	static async Task<bool> Mount( string ident )
	{
		var package = await Package.FetchAsync( ident, false );
		if ( package is null )
		{
			Log.Warning( $"Scene Lab: no package {ident}" );
			return false;
		}

		if ( await package.MountAsync() is null )
		{
			Log.Warning( $"Scene Lab: couldn't mount {ident}" );
			return false;
		}

		return true;
	}

	/// <summary>
	/// A particle effect with no emitter and nothing random: <see cref="Emit"/> places every particle, so a run's particles
	/// are the same each time (an emitter's spread, and a particle's own seed, come from <c>Random.Shared</c>). Particles fade
	/// from <paramref name="alpha"/> to nothing and grow by <paramref name="grow"/> over their lives, and fall at
	/// <paramref name="gravity"/>.
	/// </summary>
	public static ParticleEffect Particles( string name, string texture, int max, bool additive = false, bool lit = false, float alpha = 1, float grow = 1, float gravity = 0, float damping = 0 )
	{
		var go = new GameObject( true, name );
		var effect = go.Components.Create<ParticleEffect>();
		effect.MaxParticles = max;
		effect.Lifetime = 1000;
		effect.ApplyAlpha = true;
		effect.Alpha = new ParticleFloat { Type = ParticleFloat.ValueType.Range, Evaluation = ParticleFloat.EvaluationType.Life, ConstantA = alpha, ConstantB = 0 };
		effect.ApplyShape = true;
		effect.Scale = new ParticleFloat { Type = ParticleFloat.ValueType.Range, Evaluation = ParticleFloat.EvaluationType.Life, ConstantA = 1, ConstantB = grow };
		effect.Damping = damping;

		if ( gravity != 0 )
		{
			effect.Force = true;
			effect.ForceDirection = Vector3.Down * gravity;
		}

		var renderer = go.Components.Create<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( Texture.Load( texture ) );
		renderer.Additive = additive;
		renderer.Lighting = lit;
		renderer.SortMode = ParticleSpriteRenderer.ParticleSortMode.ByDistance;
		return effect;
	}

	/// <summary>
	/// A particle placed exactly: where, how fast, how big, which way round, its colour and how long it lives.
	/// </summary>
	public static void Emit( ParticleEffect effect, Vector3 position, Vector3 velocity, float size, float roll, Color color, float life )
	{
		if ( effect.IsFull ) return;

		var p = effect.Emit( position, 0 );
		p.Velocity = velocity;
		p.StartScale = size;
		p.Size = size;
		p.Angles = new Angles( 0, 0, roll );
		p.Color = color;
		p.DeathTime = p.BornTime + life;
	}

	/// <summary>
	/// Every particle gone, for a rewind.
	/// </summary>
	public static void Clear( ParticleEffect effect )
	{
		if ( effect.IsValid() ) effect.Clear();
	}

	/// <summary>
	/// A label above something: a world panel turned to face the camera every frame by <see cref="FaceCamera"/>.
	/// </summary>
	public static WorldPanel Nameplate( GameObject parent, string text, float height, Color color )
	{
		var go = new GameObject( parent, true, $"{parent.Name} Nameplate" );
		go.LocalPosition = Vector3.Up * height;
		go.WorldScale = 2;

		var panel = go.Components.Create<WorldPanel>();
		panel.PanelSize = new Vector2( 600, 140 );
		var root = panel.GetPanel();
		root.StyleSheet.Add( Sandbox.UI.StyleSheet.FromString( NameplateStyles, "/scenelab/nameplate.scss" ) );
		var plate = root.Add.Panel( "plate" );
		plate.Add.Label( text, "name" );
		var bar = plate.Add.Panel( "health" );
		bar.Add.Panel( "fill" ).Style.BackgroundColor = color;
		return panel;
	}

	const string NameplateStyles = """
		.plate { position: absolute; left: 0; top: 0; right: 0; bottom: 0; flex-direction: column; align-items: center;
			justify-content: center; gap: 8px; font-family: Poppins; color: white; }
		.name { font-size: 56px; font-weight: 700; white-space: nowrap; text-shadow: 0 2px 6px black; }
		.health { width: 360px; height: 18px; border-radius: 9px; background-color: rgba( 0, 0, 0, 0.6 ); }
		.fill { width: 70%; height: 100%; border-radius: 9px; }
		""";

	/// <summary>
	/// Turn world panels to face the camera, as a game's nameplates do.
	/// </summary>
	public static void FaceCamera( IEnumerable<WorldPanel> panels, Vector3 camera )
	{
		foreach ( var panel in panels )
		{
			if ( !panel.IsValid() ) continue;
			var go = panel.GameObject;
			go.WorldRotation = Rotation.LookAt( (camera - go.WorldPosition).WithZ( 0 ) );
		}
	}

	/// <summary>
	/// A shadowed light's flicker for a frame, from its index: mostly steady, with short deterministic dips and the odd
	/// blackout, like a failing tube.
	/// </summary>
	public static float Flicker( int light, int frame )
	{
		var hash = (uint)(light * 73856093) ^ (uint)(frame / 3 * 19349663);
		hash ^= hash >> 13;
		hash *= 0x5bd1e995;
		hash ^= hash >> 15;
		var noise = (hash & 0xffff) / 65535.0f;
		return noise < 0.08f ? 0.05f : noise < 0.3f ? 0.55f + noise : 1;
	}
}

/// <summary>
/// A camera's scripted path by frame: keys of position, yaw and pitch, eased in a straight line between - moves at a speed,
/// turns on the spot and holds. It loops.
/// </summary>
internal sealed class CameraPath
{
	readonly record struct Key( float Frame, Vector3 Position, float Yaw, float Pitch );

	readonly List<Key> keys = [];

	public CameraPath( Vector3 start, float yaw, float pitch = 0 )
	{
		keys.Add( new Key( 0, start, yaw, pitch ) );
	}

	Key Last => keys[^1];

	/// <summary>
	/// The frames a loop takes.
	/// </summary>
	public int Frames => Math.Max( 1, (int)MathF.Ceiling( Last.Frame ) );

	/// <summary>
	/// Go to <paramref name="to"/> at <paramref name="speed"/> units a second, facing the way it goes - after turning to face
	/// it over <paramref name="turnSeconds"/> if it doesn't already.
	/// </summary>
	public CameraPath MoveTo( Vector3 to, float speed, float turnSeconds = 0.3f, float? pitch = null )
	{
		var travel = to - Last.Position;
		var yaw = Last.Yaw + Angles.NormalizeAngle( travel.EulerAngles.yaw - Last.Yaw );
		if ( MathF.Abs( yaw - Last.Yaw ) > 1 ) keys.Add( Last with { Frame = Last.Frame + turnSeconds * 60, Yaw = yaw } );
		// Timed by the ground covered, so climbing a slope doesn't slow it
		keys.Add( new Key( Last.Frame + travel.WithZ( 0 ).Length / speed * 60, to, Last.Yaw, pitch ?? Last.Pitch ) );
		return this;
	}

	/// <summary>
	/// Turn on the spot by <paramref name="degrees"/> over <paramref name="seconds"/>.
	/// </summary>
	public CameraPath Turn( float degrees, float seconds, float? pitch = null )
	{
		keys.Add( Last with { Frame = Last.Frame + seconds * 60, Yaw = Last.Yaw + degrees, Pitch = pitch ?? Last.Pitch } );
		return this;
	}

	/// <summary>
	/// Cut to somewhere else, facing <paramref name="yaw"/>: a respawn, or a jump to another part of the map.
	/// </summary>
	public CameraPath Cut( Vector3 to, float yaw )
	{
		keys.Add( Last with { Frame = Last.Frame + 1, Position = to, Yaw = yaw } );
		return this;
	}

	/// <summary>
	/// Stay put for <paramref name="seconds"/>.
	/// </summary>
	public CameraPath Hold( float seconds )
	{
		keys.Add( Last with { Frame = Last.Frame + seconds * 60 } );
		return this;
	}

	/// <summary>
	/// Where the camera is on a frame, and which way it looks.
	/// </summary>
	public (Vector3 Position, Rotation Rotation) At( int frame )
	{
		var f = frame % Frames;
		var i = 1;
		while ( i < keys.Count - 1 && keys[i].Frame < f ) i++;

		var a = keys[i - 1];
		var b = keys[Math.Min( i, keys.Count - 1 )];
		var t = b.Frame > a.Frame ? Math.Clamp( (f - a.Frame) / (b.Frame - a.Frame), 0, 1 ) : 1;
		var position = Vector3.Lerp( a.Position, b.Position, t );
		return (position, Rotation.From( MathX.Lerp( a.Pitch, b.Pitch, t ), MathX.Lerp( a.Yaw, b.Yaw, t ), 0 ));
	}

	public void Apply( CameraComponent camera, int frame )
	{
		var (position, rotation) = At( frame );
		camera.WorldPosition = position;
		camera.WorldRotation = rotation;
	}
}
