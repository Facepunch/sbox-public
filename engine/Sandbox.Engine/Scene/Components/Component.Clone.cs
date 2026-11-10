namespace Sandbox;

public abstract partial class Component
{
	// Set only during the cloning process
	// We store this on the component to avoid the need reverse lookup table during the clone process
	private Component _cloneOriginal = null;

	/// <summary>
	/// Runs after this clone has been created by a cloned GameObject.
	/// </summary>
	/// <param name="original">The original component that was cloned.</param>
	/// <param name="context">During the cloning process, we build a mapping from original objects to their clone, so we will need to add ourselves to it.</param>
	internal void InitClone( Component original, CloneContext context )
	{
		context.OriginalToClone[original] = this;
		_cloneOriginal = original;
	}

	/// <summary>
	/// Runs after all objects of the original hierarchy have been cloned/created.
	/// Here we initialize the clones properties and fields with the values from the original object.
	/// The caller has already pushed the ActionGraph target for our GameObject.
	/// </summary>
	internal void PostClone( CloneContext context )
	{
		if ( !_cloneOriginal.IsValid() )
		{
			// Nothing todo this component is not a proper clone. It was created through side effects while cloning properties.
			return;
		}

		var plan = ReflectionQueryCache.ClonePlan( GetType() );
		for ( int i = 0; i < plan.Length; i++ )
		{
			plan[i].Clone( this, _cloneOriginal, context );
		}

		CheckRequireComponent();

		_cloneOriginal = null;
	}
}
