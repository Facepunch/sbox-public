using System;
using System.Collections.Generic;
using Sandbox;

namespace Editor;

partial class SceneCompiler
{
	readonly record struct Source( Component Component, string Label, SceneCompileSkipReason SkipReason, bool NeedsConversion = false )
	{
		public bool NeedsCompilation => SkipReason == SceneCompileSkipReason.None || NeedsConversion;
	}

	static IEnumerable<Source> DiscoverSources( Scene scene )
	{
		foreach ( var mesh in scene.Components.GetAll<MeshComponent>( FindMode.EverythingInSelfAndDescendants ) )
			yield return new Source( mesh, "meshes", SkipReason( mesh ), NeedsConversion: Runtime( mesh.GameObject ) );

		foreach ( var renderer in scene.GetAllComponents<ModelRenderer>() )
			yield return new Source( renderer, "props", SkipReason( renderer ) );
	}

	/// <summary>
	/// Why a mesh can't be welded into the world. Anything that triggers or
	/// collides with its own physics settings has to keep doing its own thing, and so does anything
	/// drawing with shadows turned off - an aggregate draws what it holds, with shadows.
	/// </summary>
	static SceneCompileSkipReason SkipReason( MeshComponent mesh )
	{
		if ( !mesh.Active )
			return SceneCompileSkipReason.NotActive;

		if ( mesh.Mesh is null )
			return SceneCompileSkipReason.NoMesh;

		if ( !Runtime( mesh.GameObject ) )
			return SceneCompileSkipReason.NotInGame;

		if ( !mesh.GameObject.IsStatic )
			return SceneCompileSkipReason.NotStatic;

		if ( mesh.Rigidbody.IsValid() )
			return SceneCompileSkipReason.Rigidbody;

		if ( !mesh.HideInGame && mesh.RenderType != ModelRenderer.ShadowRenderType.On )
			return SceneCompileSkipReason.ShadowsDisabled;

		if ( mesh.Collision == MeshComponent.CollisionType.None )
			return SceneCompileSkipReason.None;

		return PhysicsSkipReason( mesh );
	}

	/// <summary>
	/// Whether an object reaches the compiled scene at all. Compilation lifts geometry out of the
	/// hierarchy it sat in and parents it under the world root, so an object the scene loader would
	/// have dropped - or one sat under any object it would have dropped - has to be left alone.
	/// </summary>
	static bool Runtime( GameObject go )
	{
		const GameObjectFlags excluded = GameObjectFlags.NotSaved | GameObjectFlags.EditorOnly;

		for ( var current = go; current.IsValid(); current = current.Parent )
		{
			if ( (current.Flags & excluded) != 0 )
				return false;
		}

		return true;
	}

	static bool PropOrigin( Material material ) => material.IsValid() && material.Flags.GetBool( "VertexNeedsPropOrigin" );

	/// <summary>
	/// Why a model renderer's geometry can't be welded into the world. This
	/// mirrors the map compiler's prop_static test - anything that picks its meshes or materials at
	/// runtime, or draws somewhere other than the world, has to keep drawing itself.
	/// </summary>
	static SceneCompileSkipReason SkipReason( ModelRenderer renderer )
	{
		if ( renderer is SkinnedModelRenderer )
			return SceneCompileSkipReason.Animated;

		if ( !renderer.Active )
			return SceneCompileSkipReason.NotActive;

		if ( !Runtime( renderer.GameObject ) )
			return SceneCompileSkipReason.NotInGame;

		if ( !renderer.GameObject.IsStatic )
			return SceneCompileSkipReason.NotStatic;

		if ( renderer.Components.GetAll<ModelDeformer>( FindMode.EverythingInSelfAndDescendants )
			.Any( x => x.Target == renderer && Runtime( x.GameObject ) ) )
			return SceneCompileSkipReason.ModelDeformers;

		if ( renderer.RenderType != ModelRenderer.ShadowRenderType.On )
			return SceneCompileSkipReason.ShadowsDisabled;

		var options = renderer.RenderOptions;

		if ( !options.Game || options.Overlay || options.Bloom || options.AfterUI )
			return SceneCompileSkipReason.NotInWorld;

		if ( !renderer.Model.IsValid() )
			return SceneCompileSkipReason.NoModel;

		if ( renderer.Model.IsProcedural )
			return SceneCompileSkipReason.ProceduralModel;

		if ( !renderer.Model.HasRenderMeshes() )
			return SceneCompileSkipReason.NoRenderMeshes;

		var materials = renderer.Materials;

		for ( int i = 0; i < materials.Count; i++ )
		{
			if ( materials.HasOverride( i ) )
				return SceneCompileSkipReason.MaterialOverrides;
		}

		return renderer.GameObject.Components.Get<ModelCollider>( FindMode.EverythingInSelf ) is { } collider
			? SkipReason( collider )
			: SceneCompileSkipReason.None;
	}

	/// <summary>
	/// Why a collider's shapes can't be welded into the world. A renderer we
	/// compile takes its collider with it, so anything the collider does for itself keeps them both.
	/// </summary>
	static SceneCompileSkipReason SkipReason( ModelCollider collider )
	{
		if ( !collider.Active )
			return SceneCompileSkipReason.CollisionNotActive;

		if ( !collider.Model.IsValid() || collider.Model.Physics is null )
			return SceneCompileSkipReason.CollisionNoShapes;

		if ( collider.Rigidbody.IsValid() )
			return SceneCompileSkipReason.CollisionRigidbody;

		if ( !collider.Static )
			return SceneCompileSkipReason.CollisionNotStatic;

		return PhysicsSkipReason( collider );
	}

	static SceneCompileSkipReason PhysicsSkipReason( Collider collider )
	{
		var modelCollision = collider is ModelCollider;

		if ( collider.IsTrigger )
			return modelCollision ? SceneCompileSkipReason.CollisionTrigger : SceneCompileSkipReason.Trigger;

		if ( collider.ColliderFlags != default )
			return modelCollision ? SceneCompileSkipReason.CollisionFlags : SceneCompileSkipReason.ColliderFlags;

		if ( !collider.SurfaceVelocity.IsNearZeroLength )
			return modelCollision ? SceneCompileSkipReason.CollisionSurfaceVelocity : SceneCompileSkipReason.SurfaceVelocity;

		if ( collider.Friction.HasValue || collider.Elasticity.HasValue || collider.RollingResistance.HasValue )
			return modelCollision ? SceneCompileSkipReason.CollisionPhysicsOverrides : SceneCompileSkipReason.PhysicsOverrides;

		return SceneCompileSkipReason.None;
	}

	/// <summary>
	/// The collider whose shapes get welded in alongside a compiled renderer. Null means there's
	/// nothing to weld - <see cref="SkipReason(ModelRenderer)"/> has already turned away anything
	/// with collision we can't take.
	/// </summary>
	static ModelCollider Collider( GameObject go )
	{
		var collider = go.Components.Get<ModelCollider>( FindMode.EverythingInSelf );

		return collider is not null && SkipReason( collider ) == SceneCompileSkipReason.None ? collider : null;
	}

	/// <summary>
	/// An object's effective tags, ancestors included, as one comparable key. Compiled geometry moves
	/// out of the hierarchy it inherited these from, so anything sharing an aggregate or a collision
	/// shape has to share its tags.
	/// </summary>
	static string TagKey( GameObject go ) => string.Join( ',', go.Tags.TryGetAll().Order( StringComparer.Ordinal ) );
}
