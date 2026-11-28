using System.Text.Json.Serialization;
using static Sandbox.Component;
using static Sandbox.ModelRenderer;

namespace Sandbox;

/// <summary>
/// An editable polygon mesh with collision
/// </summary>
[Hide, Expose]
public sealed partial class MeshComponent : Collider, ExecuteInEditor, ITintable, IMaterialSetter
{
	[Expose]
	public enum CollisionType
	{
		None,
		Mesh,
		Hull,
		Sphere = 4,
		Capsule
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

			UpdateMesh();
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

	/// <summary>
	/// Scene object tag for Hide In Game meshes in the editor, excluded from envmap and indirect light volume bakes.
	/// </summary>
	internal const string HideInGameTag = "hide_in_game";

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
			{
				UpdateSceneObjectTags();
				return;
			}

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
				_sceneObject.Flags.ExcludeGameLayer = RenderType == ShadowRenderType.ShadowsOnly;
			}
		}
	} = ShadowRenderType.On;

	/// <summary>Draw order for overlapping overlay surfaces.</summary>
	[Property, Category( "Rendering" )]
	public int RenderOrder
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( _sceneObject.IsValid() )
				_sceneObject.Attributes.Set( "RenderOrder", value );
		}
	}

	/// <summary>Hide this surface when rendering in low quality mode.</summary>
	[Property, Category( "Rendering" )]
	public bool DisabledInLowQuality
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( _sceneObject.IsValid() )
				_sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.DisabledInLowQuality, value );
		}
	}

	[JsonIgnore, Hide]
	public Model Model { get; private set; }

	public override bool IsConcave => Collision == CollisionType.Mesh;

	bool Hidden => !Scene.IsEditor && HideInGame;

	SceneObject _sceneObject;
	PolygonMesh _builtMesh;
	CollisionType _builtCollision;

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
		GameObject.Tags.Add( "world" );

		// Mesh needs to build before collider.
		RebuildRenderMesh();

		base.OnEnabledInternal();

		_ = RecomputeTextureParametersWhenTexturesLoad();
	}

	// Face texture parameters are derived from the coordinates using each material's texture size.
	// A streamed texture isn't resident yet when we enable, so its faces derive against a fallback
	// size and the scale comes out wrong. Wait for the textures then derive from the coordinates again.
	async Task RecomputeTextureParametersWhenTexturesLoad()
	{
		if ( !Scene.IsEditor || Mesh is null || !Mesh.HasLoadingMaterialTextures() )
			return;

		while ( this.IsValid() && Mesh is not null && Mesh.HasLoadingMaterialTextures() )
			await Task.Delay( 10 );

		if ( this.IsValid() && Mesh is not null )
			Mesh.ComputeFaceTextureParametersFromCoordinates();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		DeleteSceneObject();
	}

	void DeleteSceneObject()
	{
		DeleteEditorRenderData();

		if ( !_sceneObject.IsValid() ) return;

		_sceneObject.RenderingEnabled = false;
		_sceneObject.Delete();
		_sceneObject = null;
	}

	// Off the update list in play: a mesh edits only in the editor, and the collision setter rebuilds itself. A map has thousands
	internal override bool UpdatesIn( Scene scene ) => scene.IsEditor;

	protected override void OnUpdate()
	{
		base.OnUpdate();

		UpdateMesh();
	}

	public void RebuildMesh()
	{
		if ( Scene?.IsEditor != true ) return;

		UpdateMesh();
	}

	void UpdateMesh()
	{
		// In game only a collision type change does anything, and this runs every frame for every mesh
		if ( Scene?.IsEditor != true && (_builtMesh is null || _builtCollision == Collision) ) return;
		if ( !Active || !PhysicsBody.IsValid() ) return;

		if ( (Scene.IsEditor && (_builtMesh != Mesh || Mesh?.IsDirty == true)) || Mesh?.IsLightmapDirty == true )
		{
			RebuildRenderMesh();
		}
		else if ( _builtMesh is not null && _builtCollision != Collision )
		{
			SetModel( _builtMesh.CreateModel( Collision ) );
		}
		else if ( Scene.IsEditor && Mesh?.IsVertexDataDirty == true )
		{
			Mesh.UpdateVertexData();
		}
	}

	protected override void OnTagsChanged()
	{
		base.OnTagsChanged();

		UpdateSceneObjectTags();
	}

	internal override void TransformChanged( GameTransform root )
	{
		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Transform = WorldTransform;
		}
		if ( Scene.IsEditor )
		{
			UpdateEditorRenderState();
			EditorRenderDataChanged?.Invoke();
		}

		if ( Mesh is not null && Scene.IsEditor )
		{
			// Compute face texture parameters on transform change but don't rebuild mesh now.
			var wasDirty = Mesh.IsDirty;
			Mesh.Transform = WorldTransform;
			if ( !wasDirty )
				Mesh.IsDirty = false;
		}

		base.TransformChanged( root );
	}

	internal override void ApplyShapeSurfaces()
	{
		if ( Shapes.Count == 0 ) return;

		// PolygonMesh emits one shape per physics part, in model order.
		var index = 0;
		foreach ( var body in Model.Physics.Parts )
		{
			foreach ( var part in body.Parts )
			{
				var shape = Shapes[index++];
				shape.Surface = Surface ?? part.Surface;
				if ( part is PhysicsGroupDescription.BodyPart.MeshPart mesh && shape._shape is PhysicsShape3d physics )
					physics.SetSurfaces( mesh.Surfaces, Surface );
			}
		}
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
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( mesh.Surface, mesh.Surfaces ) )
					{
						shape = targetBody.AddShape( mesh, bx, false, true );
					}
					Assert.NotNull( shape, "Mesh shape was null" );

					yield return shape;
				}
			}
			else if ( Collision == CollisionType.Sphere )
			{
				foreach ( var sphere in part.Spheres )
				{
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( sphere.Surface ) )
						shape = targetBody.AddSphereShape( bx.PointToWorld( sphere.Sphere.Center ), sphere.Sphere.Radius * MathF.Abs( bx.UniformScale ) );
					yield return shape;
				}
			}
			else if ( Collision == CollisionType.Capsule )
			{
				foreach ( var capsule in part.Capsules )
				{
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( capsule.Surface ) )
						shape = targetBody.AddCapsuleShape( bx.PointToWorld( capsule.Capsule.CenterA ), bx.PointToWorld( capsule.Capsule.CenterB ), capsule.Capsule.Radius * MathF.Abs( bx.UniformScale ) );
					yield return shape;
				}
			}
			else if ( Collision == CollisionType.Hull )
			{
				foreach ( var hull in part.Hulls )
				{
					PhysicsShape shape;
					using ( targetBody._body.UseShapeSurface( hull.Surface ) )
					{
						shape = targetBody.AddShape( hull, bx );
					}
					Assert.NotNull( shape, "Hull shape was null" );
					yield return shape;
				}
			}
		}
	}

	void RebuildRenderMesh()
	{
		if ( !Active ) return;

		if ( Mesh is not null )
		{
			Mesh.Transform = WorldTransform;
			Mesh.SetSmoothingAngle( SmoothingAngle );

			// An edit moves the lightmap's charts
			if ( _builtMesh == Mesh && Mesh.IsDirty )
				LightmapOffset = null;

			UpdateLightmap();
		}

		var model = Model;
		if ( _builtMesh != Mesh || Mesh?.IsDirty == true || Mesh?.IsLightmapDirty == true || model is null )
			model = Mesh?.Rebuild( Collision );
		else
		{
			if ( Mesh?.IsVertexDataDirty == true )
				Mesh.UpdateVertexData();
			if ( _builtCollision != Collision )
				model = Mesh?.CreateModel( Collision );
		}

		_builtMesh = Mesh;
		SetModel( model );
	}

	void SetModel( Model model )
	{
		_builtCollision = Collision;

		if ( Model != model )
		{
			Model = model;
			RebuildImmediately();
		}

		if ( Model is null || Model.MeshCount == 0 )
		{
			DeleteSceneObject();
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

		if ( Scene.IsEditor )
		{
			UpdateEditorRenderData();
		}

		_sceneObject.Component = this;
		UpdateSceneObjectTags();
		_sceneObject.ColorTint = Color;
		if ( RenderOrder != 0 ) _sceneObject.Attributes.Set( "RenderOrder", RenderOrder );
		if ( DisabledInLowQuality ) _sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.DisabledInLowQuality, true );
		_sceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
		_sceneObject.Flags.ExcludeGameLayer = RenderType == ShadowRenderType.ShadowsOnly;
		UpdateLightmap();
	}

	/// <summary>
	/// Hide In Game meshes still render in the editor, tag them so editor bakes (envmaps, indirect light volumes) can leave them out.
	/// </summary>
	void UpdateSceneObjectTags()
	{
		if ( !_sceneObject.IsValid() ) return;

		_sceneObject.Tags.SetFrom( GameObject.Tags );

		if ( HideInGame )
			_sceneObject.Tags.Add( HideInGameTag );

		if ( Scene.IsEditor )
		{
			_sceneObject.Tags.Add( "editor_mesh" );
			UpdateEditorRenderState();
		}
	}
}
