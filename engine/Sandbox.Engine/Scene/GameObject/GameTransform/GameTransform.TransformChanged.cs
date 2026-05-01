namespace Sandbox;

public partial class GameTransform
{
	/// <summary>
	/// Called when the transform is changed
	/// </summary>
	public Action OnTransformChanged;

	/// <summary>
	/// I need to know the root transform that actually changed
	/// </summary>
	internal Action<GameTransform> OnTransformChangedInternal;

	/// <summary>
	/// Our transform has changed, which means our children transforms changed too
	/// tell them all.
	/// </summary>
	internal unsafe void TransformChanged( bool useTargetLocal = false, GameTransform root = null )
	{
		root ??= this;

		_worldCached = default;
		_worldInterpCached = default;

		InsideChangeCallback = useTargetLocal;

		try
		{
			OnTransformChanged?.Invoke();
			OnTransformChangedInternal?.Invoke( root );
		}
		finally
		{
			InsideChangeCallback = false;
		}

		var data = new TransformChangedData { Root = root };
		GameObject.ForEachChildFast( "TransformChanged", true, &TransformChangedCallback, ref data );
	}

	// empty, no data to pass
	struct TransformChangedData
	{
		public GameTransform Root;
	}

	static void TransformChangedCallback( GameObject c, ref TransformChangedData data )
	{
		if ( !c.Transform.IsFollowingParent() )
			return;

		c.Transform.TransformChanged( false, data.Root );
	}
}
