using Sandbox.Rendering;

namespace Sandbox.PanelGallery;

/// <summary>Exercises SpriteRenderer components through ScenePanel's normal scene update.</summary>
public sealed class SpriteRendererPage : GalleryPage
{
	readonly ScenePanel view;
	readonly List<Texture> textures = [];
	readonly List<SpriteRenderer> renderers = [];
	readonly Sandbox.UI.Label status;
	readonly SpriteRenderer subject;
	float time;
	float direction = 1;
	bool paused;
	bool move = true;
	int loops;

	public SpriteRendererPage() : base( "SpriteRenderer",
		"Real SpriteRenderer components in a scene. Check animation, tint, rotation, scale and batch changes. The frame display and loop counter should keep advancing without any manual frame updates." )
	{
		var controls = Case( "Playback" );
		controls.AddChild( new Sandbox.UI.Button( "Pause / resume", "pause", "primarybutton", () =>
		{
			paused = !paused;
			ApplySpeed();
		} ) );
		controls.AddChild( new Sandbox.UI.Button( "Restart", "replay", "primarybutton", () =>
		{
			for ( int i = 0; i < renderers.Count; i++ )
				renderers[i].CurrentFrameIndex = i == 1 ? 2 : 0;
			loops = 0;
		} ) );
		controls.AddChild( new Sandbox.UI.Button( "Reverse", "swap_horiz", "primarybutton", () =>
		{
			direction *= -1;
			ApplySpeed();
		} ) );
		status = Add.Label( "", "reference-note" );

		view = AddChild<ScenePanel>();
		var scene = view.RenderScene;
		var cameraObject = scene.CreateObject();
		cameraObject.WorldPosition = new Vector3( -600, 0, 0 );
		cameraObject.WorldRotation = Rotation.LookAt( Vector3.Forward );
		var camera = cameraObject.Components.Create<CameraComponent>();
		camera.BackgroundColor = "#101b2b";
		camera.FieldOfView = 40;
		camera.OrthographicHeight = 340;
		camera.Orthographic = Environment.GetCommandLineArgs().Contains( "-orthographic" );
		camera.EnablePostProcessing = false;
		Toggle( controls, "Orthographic camera", camera.Orthographic, value => camera.Orthographic = value );

		for ( int i = 0; i < 4; i++ ) textures.Add( SpritePage.CreateFrame( i ) );
		var animated = Sprite.FromTextures( textures, 6 );
		animated.Animations[0].LoopMode = Sprite.LoopMode.Loop;
		var still = Sprite.FromTextures( new[] { textures[0] }, 6 );
		SpriteRenderer AddSprite( Sprite resource, Vector3 position, Color color, float size )
		{
			var go = scene.CreateObject();
			go.WorldPosition = position;
			var renderer = go.Components.Create<SpriteRenderer>();
			renderer.Sprite = resource;
			renderer.Color = color;
			renderer.Size = size;
			renderer.TextureFilter = FilterMode.Point;
			renderer.FogStrength = 0;
			renderer.IsSorted = true;
			renderers.Add( renderer );
			return renderer;
		}

		subject = AddSprite( animated, new Vector3( 0, 130, 70 ), Color.White, 72 );
		subject.OnAnimationEnd = _ => loops++;
		AddSprite( animated, new Vector3( 0, 0, 70 ), GalleryPalette.Pink, 72 ).CurrentFrameIndex = 2;
		AddSprite( animated, new Vector3( 0, -130, 70 ), GalleryPalette.Lime, 72 );
		AddSprite( still, new Vector3( 0, 130, -70 ), GalleryPalette.Cyan, 72 );
		var rotated = AddSprite( animated, new Vector3( 0, 0, -70 ), GalleryPalette.Pink, 72 );
		rotated.Billboard = SpriteRenderer.BillboardMode.None;
		rotated.WorldRotation = Rotation.From( 0, 0, 30 );
		AddSprite( animated, new Vector3( 0, -130, -70 ), GalleryPalette.Lime, 100 ).WorldScale = new Vector3( 1, 1, 0.6f );
		ApplySpeed();

		Add.Label( "Top: normal playback · half speed, offset frame · reverse playback. Bottom: static frame · rotated sprite · scaled sprite.", "reference-note" );
		var appearance = Case( "White sprite" );
		Toggle( appearance, "Visible", true, value => subject.Enabled = value );
		Toggle( appearance, "Additive", false, value => subject.Additive = value );
		Toggle( appearance, "Opaque", false, value => subject.Opaque = value );
		Toggle( appearance, "Flip horizontal", false, value => subject.FlipHorizontal = value );
		Toggle( appearance, "Move", true, value => move = value );
		Add.Label( "The white sprite should disappear and return when toggled, with no ghost copies when changing blend mode. Pausing freezes animated frames; the static cyan sprite never animates.", "reference-note" );
		UseSceneLayout( view, camera );
	}

	static void Toggle( Panel parent, string text, bool initial, Action<bool> changed ) =>
		parent.AddChild( new Sandbox.UI.Checkbox { LabelText = text, Checked = initial, ValueChanged = changed } );

	void ApplySpeed()
	{
		for ( int i = 0; i < renderers.Count; i++ )
			renderers[i].PlaybackSpeed = paused ? 0 : direction * (i == 1 ? 0.5f : i == 2 ? -1 : 1);
	}

	public override void Tick()
	{
		base.Tick();
		if ( move && !paused ) time += RealTime.Delta;
		subject.WorldPosition = new Vector3( 0, 130, 70 + MathF.Sin( time * 2 ) * 12 );
		status.Text = $"White sprite: frame {subject.CurrentFrameIndex + 1} / 4 · loops {loops} · {(paused ? "Paused" : direction < 0 ? "Reverse" : "Playing")} · {(subject.Enabled ? "Visible" : "Hidden")}";
	}

	public override void OnDeleted()
	{
		// Release scene references before disposing the textures shared by its renderers.
		view.RenderScene.Destroy();
		foreach ( var texture in textures ) texture.Dispose();
		base.OnDeleted();
	}
}
