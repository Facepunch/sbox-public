public abstract partial class ToolMode
{
	/// <summary>
	/// A point in the world selected by the current player's toolgun. We try to keep this as minimal as possible
	/// while catering for as much as possible.
	/// </summary>
	public struct SelectionPoint
	{
		public GameObject GameObject { get; set; }
		public Transform LocalTransform { get; set; }


		/// <summary>
		/// Returns true if GameObject is valid - which means we hit something
		/// </summary>
		public bool IsValid()
		{
			return GameObject.IsValid();
		}

		/// <summary>
		/// Returns the position of the hit - in the world space. This is the transformed position of the LocalTransform relative to the GameObject.
		/// </summary>
		/// <returns></returns>
		public Vector3 WorldPosition()
		{
			return GameObject.WorldTransform.PointToWorld( LocalTransform.Position );
		}

		/// <summary>
		/// Returns the transform of the hit
		/// </summary>
		/// <returns></returns>
		public Transform WorldTransform()
		{
			return GameObject.WorldTransform.ToWorld( LocalTransform );
		}

		/// <summary>
		/// Returns true if this object is a part of the static map
		/// </summary>
		public bool IsWorld => GameObject.Tags.Has( "world" );


		/// <summary>
		/// Returns true if this object is a player
		/// </summary>
		public bool IsPlayer => GameObject.Tags.Has( "player" );
	}

	/// <summary>
	/// Get a SelectionPoint along a ray.
	/// </summary>
	public SelectionPoint TraceFromRay( Ray ray, float distance, GameObject source )
	{
		var trace = Scene.Trace.Ray( ray, distance )
			.IgnoreGameObjectHierarchy( source );

		if ( TraceIgnoreTags.Any() )
			trace = trace.WithoutTags( TraceIgnoreTags.ToArray() );

		if ( TraceHitboxes )
			trace = trace.UseHitboxes();

		var tr = trace.Run();

		var sp = new SelectionPoint
		{
			GameObject = tr.GameObject,
			LocalTransform = tr.GameObject?.WorldTransform.ToLocal( new Transform( tr.EndPosition, Rotation.LookAt( tr.Normal ) ) ) ?? global::Transform.Zero
		};

		if ( sp.IsValid() )
		{
			// Ask the object if it allows toolgun interaction (anything can reject via IToolgunEvent)
			var selectEvent = new IToolgunEvent.SelectEvent { User = Player.Network.Owner };
			sp.GameObject.Root.RunEvent<IToolgunEvent>( x => x.OnToolgunSelect( selectEvent ) );
			if ( selectEvent.Cancelled ) return default;
		}

		return sp;
	}

	/// <summary>
	/// Get a SelectionPoint from the tool gun owner's eyes.
	/// </summary>
	public SelectionPoint TraceSelect()
	{
		// Use the exact selected surface point; tracing at an edge can hit a different face or miss.
		if ( UseSnapGrid && !AbsorbMouseInput && Input.Down( "snap" ) && _hasSnapTarget && _snapSelection.IsValid() )
			return _snapSelection;

		var player = Toolgun?.Owner;
		if ( !player.IsValid() ) return default;

		return TraceFromRay( player.EyeTransform.ForwardRay, 4096, player.GameObject );
	}

	/// <summary>
	/// Given a clicked point on a, and a clicked point on b, return a transform that places the objects so the points are touching
	/// </summary>
	public Transform GetEasyModePlacement( SelectionPoint a, SelectionPoint b )
	{
		var go = a.GameObject.Network.RootGameObject ?? a.GameObject;

		var tx = b.WorldTransform();
		tx.Rotation = tx.Rotation * a.LocalTransform.Rotation.Inverse * new Angles( 180, 0, 0 );
		tx.Position += tx.Rotation * -a.LocalTransform.Position * go.WorldScale;
		tx.Scale = go.WorldScale;
		return tx;
	}

	/// <summary>
	/// Helper to apply physics properties from a model to a GameObject's <see cref="Rigidbody"/>
	/// </summary>
	/// <param name="go"></param>
	protected void ApplyPhysicsProperties( GameObject go )
	{
        var model = go.GetComponentInChildren<ModelRenderer>();

        if ( !model.IsValid() ) return;
        if ( model.Model.Physics is null ) return;

        if ( model.Model.Physics.Parts.Count == 1 )
        {
            var part = model.Model.Physics.Parts[0];
            var rb = go.GetComponent<Rigidbody>();

            rb.MassOverride = part.Mass;
            rb.LinearDamping = part.LinearDamping;
            rb.AngularDamping = part.AngularDamping;
            rb.OverrideMassCenter = part.OverrideMassCenter;
            rb.MassCenterOverride = part.MassCenterOverride;
            rb.GravityScale = part.GravityScale;

            var props = go.GetOrAddComponent<PhysicalProperties>();
            props.Mass = part.Mass;
            props.GravityScale = part.GravityScale;
        }
    }
}

