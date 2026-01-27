namespace Sandbox;

/// <summary>
/// Renders a trail behind the object, when it moves.
/// </summary>
[Expose]
[Title( "Trail Renderer" )]
[Category( "Rendering" )]
[Icon( "show_chart" )]
public sealed class TrailRenderer : Renderer, Component.ExecuteInEditor
{
	SceneTrailObject _so;

	[Group( "Trail" )]
	[Property] public int MaxPoints { get; set; } = 64;

	[Group( "Trail" )]
	[Property] public float PointDistance { get; set; } = 8;

	[Group( "Trail" )]
	[Property] public float LifeTime { get; set; } = 2;

	/// <summary>
	/// When enabled, new points are added to the trail.
	/// </summary>
	[Group( "Trail" )]
	[Property] public bool Emitting { get; set; } = true;

	[Group( "Appearance" )]
	[Property, InlineEditor( Label = false )] public TrailTextureConfig Texturing { get; set; } = TrailTextureConfig.Default;

	[Group( "Appearance" )]
	[Property] public Gradient Color { get; set; } = global::Color.Cyan;

	[Group( "Appearance" )]
	[Property] public Curve Width { get; set; } = 5;

	[Group( "Appearance" )]
	[Property] public SceneLineObject.FaceMode Face { get; set; }

	[Group( "Rendering" )]
	[Property] public bool Wireframe { get; set; }

	[Group( "Rendering" )]
	[Property] public bool Opaque { get; set; } = true;

	[ShowIf( "Opaque", true )]
	[Group( "Rendering" )]
	[Property] public bool CastShadows { get; set; } = false;

	[ShowIf( "Opaque", false )]
	[Group( "Rendering" )]
	[Property] public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	private Material _defaultMaterial;

	protected override void OnEnabled()
	{
		_defaultMaterial = Material.Load( "materials/default/default_line.vmat" ).CreateCopy();
		_so = new SceneTrailObject( Scene.SceneWorld );
		_so.Transform = WorldTransform;
		OnSceneObjectCreated( _so );
	}

	protected override void OnDisabled()
	{
		BackupRenderAttributes( _so?.Attributes );
		_so?.Delete();
		_so = null;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !_so.IsValid() )
			return;

		_so.Transform = new Transform( WorldPosition );
		_so.LifeTime = LifeTime;
		_so.Texturing = Texturing;
		if ( !Texturing.Material.IsValid() )
		{
#pragma warning disable CS0618
			if ( Texturing.Texture.IsValid() )
			{
				_defaultMaterial.Set( "g_tColor", Texturing.Texture );
#pragma warning restore CS0618
			}
			else
			{
				_defaultMaterial.Set( "g_tColor", Texture.White );
			}
			_so.Texturing = _so.Texturing with { Material = _defaultMaterial };
		}
		_so.MaxPoints = MaxPoints;
		_so.PointDistance = PointDistance;
		_so.TrailColor = Color;
		_so.Width = Width;
		_so.Flags.CastShadows = Opaque && CastShadows;
		_so.Wireframe = Wireframe;
		_so.Opaque = Opaque;
		_so.BlendMode = BlendMode;
		_so.Face = Face;
		_so.SamplerState = new() { Filter = Texturing.FilterMode, AddressModeU = Texturing.TextureAddressMode, AddressModeV = Texturing.TextureAddressMode };

		if ( Emitting )
		{
			_so.TryAddPosition( WorldPosition, WorldRotation.Up );
		}

		_so.AdvanceTime( Time.Delta );
		_so.Build();

		RenderOptions.Apply( _so );
	}
}
