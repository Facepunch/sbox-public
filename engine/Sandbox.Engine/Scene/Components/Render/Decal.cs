using Sandbox.Rendering;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// The Decal component projects textures onto model's opaque or transparent surfaces.
/// They inherit and modify the PBR properties of the surface they're projected on.
/// </summary>
[Expose]
[Title( "Decal" )]
[Category( "Rendering" )]
[Icon( "lens_blur" )]
[EditorHandle( "materials/gizmo/decal.png" )]
[HelpUrl( "https://sbox.game/dev/doc/scene/components/reference/decals/" )]
public sealed partial class Decal : Component, Component.ExecuteInEditor, Component.ITemporaryEffect
{
	[Property, WideMode]
	public List<DecalDefinition> Decals { get; set; } = [];

	[Obsolete] public Texture ColorTexture { get; set; }
	[Obsolete] public Texture NormalTexture { get; set; }
	[Obsolete] public Texture RMOTexture { get; set; }

	float _startTime;
	DecalSceneObject _sceneObject;
	private uint _sequenceId = 0;
	int _seed = 0;
	DecalDefinition _def;
	bool _isActive;

	/// <summary>
	/// How long should this decal live for?
	/// </summary>
	[Property, Header( "Life" )]
	public ParticleFloat LifeTime { get; set; } = 0;

	/// <summary>
	/// If true then the decal will repeat itself forever
	/// </summary>
	[Property]
	public bool Looped { get; set; }

	/// <summary>
	/// If true then this decal will automatically get removed when maxdecals are exceeded. This is good for
	/// things like bullect impacts, where you want to keep them around for as long as possible but also
	/// don't want to have an unlimited amount of them hanging around.
	/// 
	/// Note that while the component will be destroyed, you probably want a TemporaryEffect component on the 
	/// GameObject to make sure it all gets fully deleted.
	/// </summary>
	[Property]
	public bool Transient { get; set; }

	/// <summary>
	/// A 2D size of the decal in world units.
	/// </summary>
	[Property, Header( "Dimensions" )]
	public Vector2 Size { get; set; } = 1;

	/// <summary>
	/// Scale the width and height by this value
	/// </summary>
	[Property]
	public ParticleFloat Scale { get; set; } = 1;

	/// <summary>
	/// Rotation angle of the decal in degrees
	/// </summary>
	[Property]
	public ParticleFloat Rotation { get; set; } = new ParticleFloat( 0, 360 );

	/// <summary>
	/// The depth of the decal in world units. This is how far the decal extends into the surface it is projected onto.
	/// </summary>
	[Property]
	public float Depth { get; set; } = 8;

	/// <summary>
	/// Parallax depth strength of the decal
	/// </summary>
	[Property, Header( "Properties" )]
	public ParticleFloat Parallax { get; set; } = 1;

	/// <summary>
	/// Tints the color of the decal's albedo and can be used to adjust the overall opacity of the decal.
	/// </summary>
	[Property]
	public ParticleGradient ColorTint { get; set; } = Color.White;

	/// <summary>
	/// Controls the opacity of the decal's color texture without reducing the impact of the normal or rmo texture.
	/// Set to 0 to create a normal/rmo only decal masked by the color textures alpha.
	/// </summary>
	[Property, Range( 0, 1 )]
	public ParticleFloat ColorMix { get; set; } = 1.0f;

	/// <summary>
	/// Attenuation angle controls how much the decal fades at an angle.
	/// At 0 it does not fade at all. Up to 1 it fades the most.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float AttenuationAngle { get; set; } = 1.0f;

	private uint _sortLayer;

	/// <summary>
	/// Determines the order the decal gets rendered in, the higher the layer the more priority it has.
	/// Decals on the same layer get automatically sorted by their GameObject ID.
	/// </summary>
	[Property, Header( "Sorting" )]
	public uint SortLayer
	{
		get => _sortLayer;
		set
		{
			if ( _sortLayer == value ) return;
			_sortLayer = value;
			UpdateSortLayer();
		}
	}

	[Title( "Sheet" )]
	[Property, FeatureEnabled( "SheetSequence", Icon = "apps" )]
	public bool SheetSequence { get; set; }

	/// <summary>
	/// Which sequence to use
	/// </summary>
	[Property, Feature( "SheetSequence" ), Range( 0, 255 )]
	public uint SequenceId
	{
		get => _sequenceId;
		set
		{
			if ( _sequenceId == value )
				return;

			_sequenceId = value;

			UpdateSequence();
		}
	}

	protected override void OnEnabled()
	{
		Assert.IsNull( _sceneObject );

		_sceneObject = new DecalSceneObject( Scene.SceneWorld );

		_seed = Random.Shared.Int( 10000 );
		_startTime = Time.Now;
		_def = Random.Shared.FromList( Decals );
		_isActive = true;

		UpdateSceneObject();
		UpdateToDelta( 0 );

		if ( Transient )
		{
			Scene.Get<DecalGameSystem>()?.AddTransient( this );
		}
	}

	protected override void OnDisabled()
	{
		_sceneObject?.Delete();
		_sceneObject = null;

		if ( Transient )
		{
			Scene.Get<DecalGameSystem>()?.RemoveTransient( this );
		}
	}

	bool ITemporaryEffect.IsActive => _isActive;

	void ITemporaryEffect.DisableLooping()
	{
		Looped = false;
	}

	void UpdateCurrentDefinition()
	{
		_def = default;

		// We don't know when the contents of Decals changes, so we
		// re-select every time here, based on the seed.
		if ( Decals is not null && Decals.Count > 0 )
		{
			var id = (int)(Rand( 349 ) * 64);
			_def = Decals[id % Decals.Count];
		}
	}

	void UpdateSceneObject()
	{
		if ( !_sceneObject.IsValid() )
			return;

		UpdateCurrentDefinition();

		if ( _def is null )
		{
			_sceneObject.RenderingEnabled = false;
			return;
		}

		// _sceneObject.ExclusionBitMask = ExclusionLayer;

		_sceneObject.Tags.SetFrom( GameObject.Tags );

		UpdateSortLayer();
		UpdateSequence();
	}

	void UpdateSortLayer()
	{
		if ( !_sceneObject.IsValid() ) return;

		// 24 bits gameobject id / 8 bits user sort layer
		// this way you get automatic sorting with a user layer override
		var bytes = GameObject.Id.ToByteArray();
		_sceneObject.SortOrder = ((uint)(SortLayer & 0xFF) << 24) | (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16));
	}

	protected override void OnPreRender()
	{
		if ( !_sceneObject.IsValid() )
			return;

		var lt = LifeTime.Evaluate( 0.0f, Rand( 2531 ) );

		if ( lt <= 0 )
		{
			UpdateToDelta( 0 );
			return;
		}

		float d = (Time.Now - _startTime) / lt;

		if ( d >= 1 && !Looped && !Scene.IsEditor )
		{
			_isActive = false;
			d = 1;
		}

		UpdateToDelta( d % 1.0f );
	}

	Vector3 GetDecalVolume( float delta )
	{
		if ( _def is null )
			return 1;

		var scale = Scale.Evaluate( delta, Rand( 238 ) );

		Vector3 size = new Vector3( Depth, Size.x, Size.y );
		size.y *= _def.Width * scale;
		size.z *= _def.Height * scale;
		return size;
	}

	/// <summary>
	/// Get the world bounds of this decal
	/// </summary>
	public BBox WorldBounds => BBox.FromPositionAndSize( Transform.World.Position, GetDecalVolume( 0.5f ) * Transform.World.Scale );

	void UpdateToDelta( float delta )
	{
		UpdateCurrentDefinition();

		if ( _def is null )
		{
			_sceneObject.RenderingEnabled = false;
			return;
		}

		var rotation = new Angles( 0, 0, Rotation.Evaluate( delta, Rand( 512 ) ) );
		var size = GetDecalVolume( delta );
		var tx = Transform.World.WithScale( Transform.World.Scale * size );
		tx.Rotation = tx.Rotation * rotation;

		_sceneObject.RenderingEnabled = _isActive;
		_sceneObject.Color = _def.Tint * ColorTint.Evaluate( delta, Rand( 238 ) );
		_sceneObject.ColorMix = _def.ColorMix * ColorMix.Evaluate( delta, Rand( 324 ) );
		_sceneObject.AttenuationAngle = AttenuationAngle;
		_sceneObject.ParallaxStrength = Parallax.Evaluate( delta, Rand( 245 ) ) * _def.ParallaxStrength * 0.25f;
		_sceneObject.SamplerIndex = SamplerState.GetBindlessIndex( new SamplerState { AddressModeU = TextureAddressMode.Clamp, AddressModeV = TextureAddressMode.Clamp, Filter = _def.FilterMode } );
		_sceneObject.Transform = tx;

		_sceneObject.ColorTexture = _def.ColorTexture;
		_sceneObject.NormalTexture = _def.NormalTexture;
		_sceneObject.RMOTexture = _def.RoughMetalOcclusionTexture;
		_sceneObject.HeightTexture = _def.HeightTexture;
		_sceneObject.EmissionTexture = _def.EmissiveTexture;
		_sceneObject.EmissionEnergy = _def.EmissionEnergy;
	}

	private void UpdateSequence()
	{
		if ( !_sceneObject.IsValid() )
			return;

		_sceneObject.SequenceIndex = SheetSequence ? SequenceId % 255 : 0;
	}

	/// <summary>
	/// Tags have been updated - lets update our scene object tags
	/// </summary>
	protected override void OnTagsChanged()
	{
		if ( !_sceneObject.IsValid() )
			return;

		_sceneObject.Tags.SetFrom( GameObject.Tags );
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
		{
			using ( Gizmo.Scope() )
			{
				Gizmo.Transform = Gizmo.Transform.WithScale( 1 );
				Gizmo.Draw.Arrow( Vector3.Zero, Vector3.Forward * 8.0f, 2, 1 );
			}
		}
		else if ( _def is not null )
		{
			var size = GetDecalVolume( 0.5f );

			var box = BBox.FromPositionAndSize( Vector3.Zero, size );
			Gizmo.Draw.LineBBox( box );
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal float Rand( int seed = 0, [CallerLineNumber] int line = 0 )
	{
		int i = _seed + (line * 20) + seed;
		return Game.Random.FloatDeterministic( i );
	}
}
