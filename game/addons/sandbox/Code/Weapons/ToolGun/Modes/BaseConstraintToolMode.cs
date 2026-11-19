public abstract class BaseConstraintToolMode : ToolMode
{
	protected SelectionPoint Point1;
	protected SelectionPoint Point2;
	protected int Stage = 0;

	public virtual bool CanConstraintToSelf => false;

	/// <summary>
	/// What <see cref="WriteConstraintConfiguration"/> sent with the constraint, on the host while <see cref="CreateConstraint"/> runs.
	/// </summary>
	protected string Configuration { get; private set; }

	public override bool UseSnapGrid => true;

	protected override void OnDisabled()
	{
		base.OnDisabled();
		Stage = 0;
		Point1 = default;
		Point2 = default;
	}

	public override void OnControl()
	{
		base.OnControl();

		var select = TraceSelect();

		if ( Input.Pressed( "attack2" ) )
		{
			// Some constraint tools can one-shot with secondary, like sliders.
			if ( Stage == 0 && select.IsValid() && GetSecondaryPoint( select ) is SelectionPoint point2 )
			{
				if ( !UpdateValidity( select, point2 ) )
					return;

				Point1 = select;
				Point2 = point2;

				Create( Point1, Point2, ToolInput.Secondary, WriteConstraintConfiguration() );
				ShootEffects( select );

				OnConstraintSubmitted( Point1, Point2 );

				return;
			}

			Stage = 0;
			IsValidState = false;
			return;
		}

		if ( !select.IsValid() )
			return;

		if ( Input.Pressed( "reload" ) )
		{
			var go = select.GameObject.Network.RootGameObject ?? select.GameObject;
			RemoveConstraints( go );
			ShootEffects( select );
		}

		IsValidState = true;

		if ( Stage == 0 )
		{
			IsValidState = UpdateValidity( select );
		}

		if ( Stage == 1 )
		{
			IsValidState = UpdateValidity( Point1, select );
		}

		if ( !IsValidState ) return;

		if ( Input.Pressed( "attack1" ) )
		{
			if ( Stage == 0 )
			{
				Point1 = select;
				Stage++;
				ShootEffects( select );
				return;
			}

			if ( Stage == 1 )
			{
				Point2 = select;

				Create( Point1, Point2, ToolInput.Primary, WriteConstraintConfiguration() );
				ShootEffects( select );
				OnConstraintSubmitted( Point1, Point2 );
			}

			Stage = 0;
		}


	}

	bool UpdateValidity( SelectionPoint point1, SelectionPoint point2 )
	{
		if ( !point1.GameObject.IsValid() ) return false;
		if ( !point2.GameObject.IsValid() ) return false;

		if ( !CanConstraintToSelf )
		{
			if ( point1.GameObject == point2.GameObject )
			{
				return false;
			}
		}

		return true;
	}

	bool UpdateValidity( SelectionPoint point1 )
	{
		if ( !point1.GameObject.IsValid() ) return false;

		return true;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void Create( SelectionPoint point1, SelectionPoint point2, ToolInput input, string configuration )
	{
		if ( !FireToolAction( input ) ) return;

		if ( !UpdateValidity( point1, point2 ) )
		{
			Log.Warning( "Tried to create invalid constraint" );
			return;
		}

		Configuration = configuration;
		CreateConstraint( point1, point2 );
		Configuration = null;
		CheckContraptionStats( point1.GameObject );

		FirePostToolAction( input );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RemoveConstraints( GameObject go )
	{
		if ( !FireToolAction( ToolInput.Reload ) ) return;

		if ( !go.IsValid() ) return;

		var builder = new LinkedGameObjectBuilder();
		builder.AddConnected( go );

		var toRemove = new List<GameObject>();
		foreach ( var linked in builder.Objects )
			toRemove.AddRange( FindConstraints( linked, go ) );

		foreach ( var host in toRemove )
			host.Destroy();

		FirePostToolAction( ToolInput.Reload );
	}

	/// <summary>
	/// Lets tools define what constraints should be removed when removing constraints from a game object.
	/// </summary>
	/// <param name="linked"></param>
	/// <param name="target"></param>
	/// <returns></returns>
	protected virtual IEnumerable<GameObject> FindConstraints( GameObject linked, GameObject target ) => [];

	protected abstract void CreateConstraint( SelectionPoint point1, SelectionPoint point2 );

	/// <summary>
	/// Settings for the entity the constraint carries, sent to the host with it, see <see cref="ToolMode.WriteConfiguration{T}"/>.
	/// </summary>
	protected virtual string WriteConstraintConfiguration() => null;

	/// <summary>
	/// Called on the owning client after a valid constraint creation was submitted to the host.
	/// Follow-up RPCs are ordered after the constraint creation RPC.
	/// </summary>
	protected virtual void OnConstraintSubmitted( SelectionPoint point1, SelectionPoint point2 ) { }

	protected virtual SelectionPoint? GetSecondaryPoint( SelectionPoint select ) => default;
}
