using Sandbox.Rendering;

namespace Sandbox.PanelGallery;

/// <summary>Sprite instances updated once per tick and drawn into destination rectangles.</summary>
public sealed class SpritePage : GalleryPage
{
	readonly List<Texture> textures = new();
	readonly SpriteInstance sprite;
	readonly SpriteInstance slowSprite;
	readonly Sandbox.UI.Label status;
	readonly CrowdPreview crowd;

	public SpritePage() : base( "Sprites", "SpriteInstance owns playback. Painter draws its current frame into a rectangle, with optional tint and filtering." )
	{
		for ( int frame = 0; frame < 4; frame++ ) textures.Add( CreateFrame( frame ) );
		var resource = Sprite.FromTextures( textures, 6 );
		sprite = new SpriteInstance( resource );
		slowSprite = new SpriteInstance( resource ) { PlaybackSpeed = 0.5f, CurrentFrameIndex = 2 };
		crowd = new CrowdPreview( resource );

		var controls = Case( "Playback" );
		controls.AddChild( new Sandbox.UI.Button( "Pause / resume", "pause", "primarybutton", () =>
		{
			sprite.Paused = !sprite.Paused;
			slowSprite.Paused = sprite.Paused;
		} ) );
		controls.AddChild( new Sandbox.UI.Button( "Restart", "replay", "primarybutton", () =>
		{
			sprite.Play( "Default", restart: true );
			slowSprite.Play( "Default", restart: true );
			crowd.Restart();
		} ) );
		controls.AddChild( new Sandbox.UI.Button( "Reverse", "swap_horiz", "primarybutton", () =>
		{
			sprite.PlaybackSpeed *= -1;
			slowSprite.PlaybackSpeed *= -1;
		} ) );
		status = controls.Parent.Add.Label( "", "reference-note" );

		var stress = Case( "5,000 sprites", column: true );
		stress.Add.Label( "5,000 independent instances · four shared textures · varied colors, rotation, scale and playback speed. Playback controls apply to the whole demo.", "reference-note" );
		stress.AddChild( crowd );

		var examples = Examples( "Drawing" );
		AddExample( examples, "One instance, two draws", "Both copies show the same frame. The second is tinted pink.", 0 );
		AddExample( examples, "Independent playback", "One resource, two instances. The green sprite runs at half speed.", 1 );
		AddExample( examples, "Destination rectangle", "A wide rectangle stretches the frame. Compare point sampling with bilinear filtering.", 2 );
	}

	void AddExample( Panel parent, string title, string description, int mode )
	{
		var example = new GalleryExample( parent, title, description, 180 );
		example.Result.AddChild( new Preview( this, mode ) );
	}

	public override void Tick()
	{
		base.Tick();
		sprite.Update( RealTime.Delta );
		slowSprite.Update( RealTime.Delta );
		if ( !sprite.Paused ) crowd.Update( RealTime.Delta, sprite.PlaybackSpeed );
		status.Text = $"Frame {sprite.CurrentFrameIndex + 1} / 4 · {(sprite.Paused ? "Paused" : sprite.PlaybackSpeed < 0 ? "Reverse" : "Playing")}";
	}

	sealed class CrowdPreview : Panel
	{
		const int Count = 5000;
		readonly Particle[] particles = new Particle[Count];
		float time;

		readonly record struct Particle( SpriteInstance Sprite, Vector2 Position, Vector2 Velocity,
			Color Tint, float Scale, float Angle, float Spin, float Speed );

		public CrowdPreview( Sprite resource )
		{
			Style.Width = Length.Percent( 100 );
			Style.Height = 420;
			Style.FlexShrink = 0;
			var random = new Random( 12345 );
			for ( int i = 0; i < Count; i++ )
			{
				particles[i] = new Particle(
					new SpriteInstance( resource ) { CurrentFrameIndex = i % 4 },
					new Vector2( random.NextSingle(), random.NextSingle() ),
					new Vector2( random.NextSingle() - 0.5f, random.NextSingle() - 0.5f ) * 0.08f,
					new ColorHsv( random.NextSingle() * 360, 0.35f + random.NextSingle() * 0.6f, 1 ),
					0.25f + random.NextSingle() * 1.1f,
					random.NextSingle() * 360,
					(random.NextSingle() - 0.5f) * 150,
					0.4f + random.NextSingle() * 1.6f );
			}
		}

		public void Restart()
		{
			time = 0;
			for ( int i = 0; i < Count; i++ ) particles[i].Sprite.CurrentFrameIndex = i % 4;
		}

		public void Update( float deltaTime, float direction )
		{
			time += deltaTime * direction;
			foreach ( var particle in particles )
			{
				particle.Sprite.PlaybackSpeed = particle.Speed * direction;
				particle.Sprite.Update( deltaTime );
			}
		}

		public override void OnDraw( Painter painter )
		{
			painter.Fill = (Color)"#111a28";
			painter.Rect( painter.Bounds, 8 );
			using var clip = painter.Scope();
			painter.Clip( painter.Bounds, 8 );
			var area = painter.Bounds.Shrink( 24 );
			foreach ( var particle in particles )
			{
				var position = particle.Position + particle.Velocity * time;
				position.x -= MathF.Floor( position.x );
				position.y -= MathF.Floor( position.y );
				using var scope = painter.Scope();
				painter.Translate( area.Position + position * area.Size );
				painter.Rotate( particle.Angle + particle.Spin * time );
				painter.Scale( particle.Scale * (1 + 0.15f * MathF.Sin( time * 2 + particle.Angle )) );
				painter.Sprite( particle.Sprite, new Rect( -12, -12, 24, 24 ), particle.Tint, FilterMode.Point );
			}
		}
	}

	sealed class Preview : Panel
	{
		readonly SpritePage page;
		readonly int mode;

		public Preview( SpritePage page, int mode )
		{
			this.page = page;
			this.mode = mode;
			Style.Width = Length.Percent( 100 );
			Style.Height = Length.Percent( 100 );
		}

		public override void OnDraw( Painter painter )
		{
			painter.Fill = Color.Parse( "#263044" ).Value;
			painter.Rect( painter.Bounds, 8 );
			var left = mode == 2 ? new Rect( 20, 44, 180, 90 ) : new Rect( 44, 26, 128, 128 );
			var right = mode == 2 ? new Rect( 220, 44, 180, 90 ) : new Rect( 248, 26, 128, 128 );
			painter.Sprite( page.sprite, left, filter: FilterMode.Point );
			painter.Sprite( mode == 1 ? page.slowSprite : page.sprite, right,
				tint: mode == 0 ? GalleryPalette.Pink : mode == 1 ? GalleryPalette.Lime : Color.White,
				filter: mode == 2 ? FilterMode.Bilinear : FilterMode.Point );
		}
	}

	// A tiny four-frame character, generated locally so the example needs no external assets.
	internal static Texture CreateFrame( int frame )
	{
		const int size = 24;
		var pixels = new byte[size * size * 4];
		int bounce = frame == 1 ? -1 : frame == 3 ? 1 : 0;
		for ( int y = 0; y < size; y++ )
			for ( int x = 0; x < size; x++ )
			{
				int py = y - bounce;
				bool body = x >= 4 && x <= 19 && py >= 7 && py <= 18
					|| x >= 7 && x <= 16 && py >= 4 && py <= 6;
				bool foot = py >= 19 && py <= 20 && ((x + frame * 2) % 6 < 3) && x >= 4 && x <= 19;
				if ( !body && !foot ) continue;
				bool eye = py >= 9 && py <= 11 && (x == 9 || x == 15);
				int i = (y * size + x) * 4;
				pixels[i] = pixels[i + 1] = pixels[i + 2] = eye ? (byte)24 : (byte)255;
				pixels[i + 3] = 255;
			}
		return Texture.Create( size, size ).WithData( pixels ).Finish();
	}

	public override void OnDeleted()
	{
		foreach ( var texture in textures ) texture.Dispose();
		base.OnDeleted();
	}
}
