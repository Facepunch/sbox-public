namespace Sandbox;

/// <summary>
/// Emits particles in a model
/// </summary>
[Expose]
[Title( "Model Emitter" )]
[Category( "Effects" )]
[Icon( "soap" )]
public sealed class ParticleModelEmitter : ParticleEmitter
{
	[Property] public GameObject Target { get; set; }
	[Property] public bool OnEdge { get; set; }

	(Vector3 position, int attachBoneIndex) GetRandomPositionOnModel( ModelRenderer target )
	{
		if ( !target.IsValid() || !target.Model.IsValid() )
			return (WorldPosition, -1);

		// If we have hitboxes, use those
		if ( target.Model.HitboxSet?.All?.Count > 0 )
		{
			var boxIndex = Random.Shared.Int( 0, target.Model.HitboxSet.All.Count - 1 );
			var box = target.Model.HitboxSet.All[boxIndex];

			var tx = target.WorldTransform;
			var attachBoneIndex = -1;

			if ( target is SkinnedModelRenderer skinned )
			{
				skinned.TryGetBoneTransform( box.Bone, out tx );
				attachBoneIndex = box.Bone.Index;
			}

			return (tx.PointToWorld( OnEdge ? box.RandomPointOnEdge : box.RandomPointInside ), attachBoneIndex);
		}

		// If we have physics, use those
		if ( target.Model?.Physics is PhysicsGroupDescription physicsGroup )
		{
			return (target.WorldTransform.PointToWorld( OnEdge ? target.Model.PhysicsBounds.RandomPointOnEdge : target.Model.PhysicsBounds.RandomPointInside ), -1);
		}

		// todo: Fallback to along bones? Make bones fat? Might be unfairly biased to dense bone areas like fingers


		return (target.WorldTransform.PointToWorld( OnEdge ? target.Model.Bounds.RandomPointOnEdge : target.Model.Bounds.RandomPointInside ), -1);
	}

	public override bool Emit( ParticleEffect target )
	{
		var model = Target.IsValid() ? Target.Components.GetInChildrenOrSelf<ModelRenderer>() : Components.GetInParentOrSelf<ModelRenderer>();
		if ( !model.IsValid() ) return false;

		var (targetPosition, attachBoneIndex) = GetRandomPositionOnModel( model );

		target.Emit( targetPosition, Delta, attachBoneIndex );

		return true;
	}
}
