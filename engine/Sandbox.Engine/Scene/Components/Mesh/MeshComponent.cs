using System.Text.Json.Serialization;
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

	[Property, Hide]
	public PolygonMesh Mesh
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			RebuildMesh();
		}
	}

	[Property, Order( 1 )]
	public CollisionType Collision
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			RebuildImmediately();
		}
	} = CollisionType.Mesh;

	[Property, Title( "Tint" ), Order( 2 )]
	public Color Color
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			_sceneObject?.ColorTint = Color;
		}
	} = Color.White;

	[Property, Order( 3 )]
	public float SmoothingAngle
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;
			Mesh?.SetSmoothingAngle( field );
		}
	}

	[Property, Order( 4 )]
	public bool HideInGame
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			if ( Scene.IsEditor )
				return;

			if ( HideInGame )
			{
				DeleteSceneObject();
			}
			else if ( !_sceneObject.IsValid() && Model is not null )
			{
				_sceneObject = new SceneObject( Scene.SceneWorld, Model, WorldTransform );
				UpdateSceneObject();
			}
		}
	}

	[Title( "Cast Shadows" ), Property, Category( "Lighting" )]
	public ShadowRenderType RenderType
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;
			if ( _sceneObject.IsValid() )
			{
				_sceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
			}
		}
	} = ShadowRenderType.On;

	[JsonIgnore, Hide]
	public Model Model { get; private set; }

	public override bool IsConcave => Collision == CollisionType.Mesh;

	bool Hidden => !Scene.IsEditor && HideInGame;

	SceneObject _sceneObject;

	public void SetMaterial( Material material, int triangle )
	{
		if ( Mesh is null ) return;

		var face = Mesh.TriangleToFace( triangle );
		if ( !face.IsValid ) return;

		Mesh.SetFaceMaterial( face, material );
	}

	public Material GetMaterial( int triangle )
	{
		if ( Mesh is null ) return default;

		var face = Mesh.TriangleToFace( triangle );
		return Mesh.GetFaceMaterial( face );
	}

	internal override void OnEnabledInternal()
	{
		// Mesh needs to build before collider.
		RebuildRenderMesh();

		base.OnEnabledInternal();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		DeleteSceneObject();
	}

	void DeleteSceneObject()
	{
		if ( !_sceneObject.IsValid() ) return;

		_sceneObject.RenderingEnabled = false;
		_sceneObject.Delete();
		_sceneObject = null;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		RebuildMesh();
	}

	public void RebuildMesh()
	{
		// Only rebuild dirty meshes in editor.
		if ( !Active ) return;
		if ( !Scene.IsEditor ) return;
		if ( Mesh is null ) return;

		if ( Mesh.IsDirty )
		{
			RebuildRenderMesh();
			RebuildImmediately();
		}
		else if ( Mesh.IsVertexDataDirty )
		{
			Mesh.UpdateVertexData();
		}
	}

	protected override void OnTagsChanged()
	{
		base.OnTagsChanged();

		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Tags.SetFrom( Tags );
		}
	}

	internal override void TransformChanged( GameTransform root )
	{
		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Transform = WorldTransform;
		}

		if ( Mesh is not null && Scene.IsEditor )
		{
			// Compute face texture parameters on transform change but don't rebuild mesh now.
			var wasDirty = Mesh.IsDirty;
			Mesh.Transform = WorldTransform;
			Mesh.IsDirty = wasDirty;
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

	void RebuildRenderMesh()
	{
		if ( !Active ) return;
		if ( Mesh is null ) return;

		Mesh.Transform = WorldTransform;
		Mesh.SetSmoothingAngle( SmoothingAngle );
		Model = Mesh.Rebuild();

		if ( Model is null || Model.MeshCount == 0 )
		{
			if ( _sceneObject.IsValid() )
			{
				_sceneObject.RenderingEnabled = false;
				_sceneObject.Delete();
				_sceneObject = null;
			}

			return;
		}

		if ( Hidden ) return;

		if ( !_sceneObject.IsValid() )
		{
			_sceneObject = new SceneObject( Scene.SceneWorld, Model, WorldTransform );
		}
		else
		{
			_sceneObject.Model = Model;
			_sceneObject.Transform = WorldTransform;

			// We manually set the model, sceneobject needs to update based on any new materials in it
			_sceneObject.UpdateFlagsBasedOnMaterial();
		}

		UpdateSceneObject();
	}

	void UpdateSceneObject()
	{
		if ( !_sceneObject.IsValid() ) return;

		_sceneObject.Component = this;
		_sceneObject.Tags.SetFrom( GameObject.Tags );
		_sceneObject.ColorTint = Color;
		_sceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
	}
}
