using System.Text.Json.Serialization;
using Sandbox.Rendering;
using static Sandbox.Component;
using static Sandbox.ModelRenderer;

namespace Sandbox;

/// <summary>
/// An editable polygon mesh with collision
/// </summary>
[Hide, Expose]
public sealed class MeshComponent : Collider, ExecuteInEditor, ITintable, IMaterialSetter
{
	[Expose]
	public enum CollisionType
	{
		None,
		Mesh,
		Hull
	}

	private PolygonMesh _mesh;

	[Property, Order( 0 )]
	public PolygonMesh Mesh
	{
		get => _mesh;
		set
		{
			if ( _mesh == value )
				return;

			_mesh = value;

			Update();
		}
	}

	[Property, Order( 1 )]
	public CollisionType Collision
	{
		get => _collisionType;
		set
		{
			if ( _collisionType == value )
				return;

			_collisionType = value;

			RebuildImmediately();
		}
	}

	[Property, Title( "Tint" ), Order( 2 )]
	public Color Color
	{
		get => _color;
		set
		{
			if ( _color == value )
				return;

			_color = value;

			if ( SceneObject.IsValid() )
			{
				SceneObject.ColorTint = Color;
			}
		}
	}

	[Property, Order( 3 )]
	public float SmoothingAngle
	{
		get => _smoothingAngle;
		set
		{
			if ( _smoothingAngle == value )
				return;

			_smoothingAngle = value;
			Mesh?.SetSmoothingAngle( _smoothingAngle );
		}
	}

	[Property, Order( 4 )]
	public bool HideInGame
	{
		get => _hideInGame;
		set
		{
			if ( _hideInGame == value )
				return;

			_hideInGame = value;

			if ( Scene.IsEditor )
				return;

			if ( HideInGame )
			{
				DeleteSceneObject();
			}
			else if ( !SceneObject.IsValid() && Model is not null )
			{
				SceneObject = new SceneObject( Scene.SceneWorld, Model, WorldTransform );
				UpdateSceneObject();
			}
		}
	}

	private ShadowRenderType _renderType = ShadowRenderType.On;

	[Title( "Cast Shadows" ), Property, Category( "Lighting" )]
	public ShadowRenderType RenderType
	{
		get => _renderType;
		set
		{
			if ( _renderType == value )
				return;

			_renderType = value;
			if ( SceneObject.IsValid() )
			{
				SceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
			}
		}
	}

	RenderOptions _renderOptions;

	[Property, MakeDirty, Order( -100 ), InlineEditor( Label = false ), Group( "Advanced Rendering", StartFolded = true )]
	public RenderOptions RenderOptions
	{
		get
		{
			_renderOptions ??= new( OnRenderOptionsChanged );
			return _renderOptions;
		}
	}

	private void OnRenderOptionsChanged()
	{
		UpdateSceneObject();
	}

	[JsonIgnore, Hide]
	public Model Model { get; private set; }

	public override bool IsConcave => Collision == CollisionType.Mesh;

	private bool Hidden => !Scene.IsEditor && HideInGame;
	private SceneObject SceneObject;
	private Color _color = Color.White;
	private CollisionType _collisionType = CollisionType.Mesh;
	private float _smoothingAngle;
	private bool _hideInGame;

	public void SetMaterial( Material material, int triangle )
	{
		if ( Mesh is null )
			return;

		var face = Mesh.TriangleToFace( triangle );
		if ( !face.IsValid )
			return;

		Mesh.SetFaceMaterial( face, material );
	}

	public Material GetMaterial( int triangle )
	{
		if ( Mesh is null )
			return default;

		var face = Mesh.TriangleToFace( triangle );
		return Mesh.GetFaceMaterial( face );
	}

	protected override void OnValidate()
	{
		WorldScale = 1;

		Update();
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();

		RebuildMesh();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		DeleteSceneObject();
	}

	private void DeleteSceneObject()
	{
		if ( !SceneObject.IsValid() )
			return;

		SceneObject.RenderingEnabled = false;
		SceneObject.Delete();
		SceneObject = null;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		Update();
	}

	private void Update()
	{
		if ( !Active )
			return;

		if ( Mesh is null )
			return;

		if ( Scene.IsEditor )
		{
			Mesh.Transform = WorldTransform;
		}

		if ( !Mesh.IsDirty )
			return;

		RebuildMesh();
	}

	internal override void OnTagsUpdatedInternal()
	{
		if ( SceneObject.IsValid() )
		{
			SceneObject.Tags.SetFrom( Tags );
		}

		base.OnTagsUpdatedInternal();
	}

	internal override void TransformChanged( GameTransform root )
	{
		if ( WorldScale != 1 && Mesh is not null )
		{
			Mesh.Scale( WorldScale );
			WorldScale = 1;
		}

		if ( Mesh is not null && Scene.IsEditor )
		{
			Mesh.Transform = WorldTransform;
		}

		if ( SceneObject.IsValid() )
		{
			SceneObject.Transform = WorldTransform;
		}

		base.TransformChanged( root );
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		if ( Collision == CollisionType.None )
			yield break;

		if ( Model is null || Model.Physics is null )
			yield break;

		foreach ( var part in Model.Physics.Parts )
		{
			Assert.NotNull( part, "Physics part was null" );

			var bx = local.ToWorld( part.Transform );

			if ( Collision == CollisionType.Mesh )
			{
				foreach ( var mesh in part.Meshes )
				{
					var shape = targetBody.AddShape( mesh, bx, false, true );
					Assert.NotNull( shape, "Mesh shape was null" );

					shape.Surface = mesh.Surface;
					shape.Surfaces = mesh.Surfaces;

					yield return shape;
				}
			}
			else if ( Collision == CollisionType.Hull )
			{
				foreach ( var hull in part.Hulls )
				{
					var shape = targetBody.AddShape( hull, bx );
					Assert.NotNull( shape, "Hull shape was null" );
					shape.Surface = hull.Surface;
					yield return shape;
				}
			}
		}
	}

	public void RebuildMesh()
	{
		if ( !Active )
			return;

		if ( Mesh is null )
			return;

		Mesh.Transform = WorldTransform;
		Mesh.SetSmoothingAngle( SmoothingAngle );
		Model = Mesh.Rebuild();

		RebuildImmediately();

		if ( Model is null || Model.MeshCount == 0 )
		{
			if ( SceneObject.IsValid() )
			{
				SceneObject.RenderingEnabled = false;
				SceneObject.Delete();
				SceneObject = null;
			}

			return;
		}

		if ( Hidden )
			return;

		if ( !SceneObject.IsValid() )
		{
			SceneObject = new SceneObject( Scene.SceneWorld, Model, WorldTransform );
		}
		else
		{
			SceneObject.Model = Model;
			SceneObject.Transform = WorldTransform;

			// We manually set the model, sceneobject needs to update based on any new materials in it
			SceneObject.UpdateFlagsBasedOnMaterial();
		}

		UpdateSceneObject();
	}

	private void UpdateSceneObject()
	{
		if ( !SceneObject.IsValid() ) return;

		SceneObject.Component = this;
		SceneObject.Tags.SetFrom( GameObject.Tags );
		SceneObject.ColorTint = Color;
		SceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
		RenderOptions.Apply( SceneObject );
	}
}
