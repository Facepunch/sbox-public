using Sandbox.Rendering;

namespace Sandbox.PanelGallery;

/// <summary>Visual checks for sprites interacting with scene lighting and geometry.</summary>
public sealed class SpriteLightingPage : GalleryPage
{
	readonly ScenePanel view;
	readonly CameraComponent camera;
	readonly PointLight light;
	readonly List<Texture> textures = [];
	readonly List<SpriteRenderer> sprites = [];
	readonly List<(Sandbox.UI.Label Label, Vector3 Position)> labels = [];
	bool animateLight = true;
	float time;

	public SpriteLightingPage() : base( "Sprite Lighting & Depth",
		"Compare lit and unlit sprites, an animated shadow on the wall, and a sprite intersecting solid geometry." )
	{
		view = AddChild<ScenePanel>();
		var scene = view.RenderScene;
		var cameraObject = scene.CreateObject();
		cameraObject.WorldPosition = new Vector3( -650, 0, 15 );
		cameraObject.WorldRotation = Rotation.LookAt( Vector3.Forward );
		camera = cameraObject.Components.Create<CameraComponent>();
		camera.FieldOfView = 46;
		camera.BackgroundColor = "#101820";
		camera.EnablePostProcessing = false;
		camera.OrthographicHeight = 350;

		ModelRenderer Box( Vector3 position, Vector3 size, Color tint )
		{
			var obj = scene.CreateObject();
			obj.WorldPosition = position;
			obj.WorldScale = size / 50;
			var renderer = obj.Components.Create<ModelRenderer>();
			renderer.Model = Model.Cube;
			renderer.Tint = tint;
			return renderer;
		}

		Box( new Vector3( 65, 0, 0 ), new Vector3( 8, 540, 240 ), (Color)"#7c8898" );
		Box( new Vector3( 5, 0, -122 ), new Vector3( 150, 540, 8 ), (Color)"#647080" );
		scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White * 0.12f;
		light = scene.CreateObject().Components.Create<PointLight>();
		light.WorldPosition = new Vector3( -160, 80, 100 );
		light.LightColor = (Color)"#ffd4a0" * 4;
		light.Radius = 650;
		light.Shadows = true;

		for ( int i = 0; i < 4; i++ ) textures.Add( SpritePage.CreateFrame( i ) );
		var resource = Sprite.FromTextures( textures, 5 );
		SpriteRenderer SpriteAt( Vector3 position, float size )
		{
			var sprite = scene.CreateObject().Components.Create<SpriteRenderer>();
			sprite.WorldPosition = position;
			sprite.Sprite = resource;
			sprite.Size = size;
			sprite.TextureFilter = FilterMode.Point;
			sprite.IsSorted = true;
			sprite.FogStrength = 0;
			sprite.Billboard = SpriteRenderer.BillboardMode.None;
			sprites.Add( sprite );
			return sprite;
		}

		var lit = SpriteAt( new Vector3( 0, 180, 30 ), 68 );
		lit.Lighting = true;
		SpriteAt( new Vector3( 0, 105, 30 ), 68 );
		var caster = SpriteAt( new Vector3( -15, 0, 30 ), 80 );
		caster.Shadows = true;
		var depth = SpriteAt( new Vector3( 0, -170, 30 ), 95 );
		depth.Color = GalleryPalette.Cyan;
		var occluder = Box( new Vector3( -24, -170, 10 ), new Vector3( 12, 100, 28 ), (Color)"#ed9a47" );

		Label( "Lit", new Vector3( -30, 180, 88 ) );
		Label( "Unlit", new Vector3( -30, 105, 88 ) );
		Label( "Shadow caster", new Vector3( -30, 0, 88 ) );
		Label( "Depth", new Vector3( -30, -170, 88 ) );

		var lighting = Case( "Lighting", column: true );
		Toggle( lighting, "Enable sprite lighting", true, value => lit.Lighting = value );
		Toggle( lighting, "Move light", true, value => animateLight = value );
		Toggle( lighting, "Light on", true, value => light.Enabled = value );
		lighting.Add.Label( "The lit sprite responds to the warm moving light. Its unlit neighbour stays white.", "reference-note" );

		var shadows = Case( "Shadows", column: true );
		Toggle( shadows, "Sprite casts shadows", true, value => caster.Shadows = value );
		Toggle( shadows, "Light casts shadows", true, value => light.Shadows = value );
		shadows.Add.Label( "The middle sprite should cast its animated silhouette onto the wall, including the transparent gaps around its feet.", "reference-note" );

		var geometry = Case( "Depth", column: true );
		Toggle( geometry, "Block in front", true, value => occluder.WorldPosition = occluder.WorldPosition.WithX( value ? -24 : 24 ) );
		Toggle( geometry, "Depth feathering", false, value => depth.DepthFeather = value ? 40 : 0 );
		geometry.Add.Label( "The orange block hides the cyan sprite when in front. Move it behind and enable feathering to compare a hard edge with a soft intersection.", "reference-note" );

		var playback = Case( "View", column: true );
		Toggle( playback, "Animate sprites", true, value => sprites.ForEach( x => x.PlaybackSpeed = value ? 1 : 0 ) );
		Toggle( playback, "Orthographic camera", false, value => camera.Orthographic = value );
		UseSceneLayout( view, camera );
	}

	void Label( string text, Vector3 position )
	{
		var label = view.Add.Label( text );
		label.SetProperty( "style", "position: absolute; transform: translateX(-50%); padding: 5px 8px; background-color: #101820dd; color: white; border-radius: 4px; pointer-events: none;" );
		labels.Add( (label, position) );
	}

	static void Toggle( Panel parent, string text, bool initial, Action<bool> changed ) =>
		parent.AddChild( new Sandbox.UI.Checkbox { LabelText = text, Checked = initial, ValueChanged = changed } );

	public override void Tick()
	{
		base.Tick();
		if ( animateLight ) time += RealTime.Delta;
		light.WorldPosition = new Vector3( -160, 80 + MathF.Sin( time * 0.7f ) * 130, 100 );
		foreach ( var (label, position) in labels )
		{
			var screen = camera.PointToScreenNormal( position );
			label.Style.Left = screen.x * view.Box.RectInner.Width / ScaleToScreen;
			label.Style.Top = screen.y * view.Box.RectInner.Height / ScaleToScreen;
		}
	}

	public override void OnDeleted()
	{
		view.RenderScene.Destroy();
		foreach ( var texture in textures ) texture.Dispose();
		base.OnDeleted();
	}
}
