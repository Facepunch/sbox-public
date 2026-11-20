public partial class BaseSandboxWeapon
{
	/// <summary>
	/// How long into a reload pulling the trigger can still cancel it, in seconds. Past this the reload
	/// plays out. Zero keeps the whole reload cancellable.
	/// </summary>
	[Property, Feature( "Reloading" ), ShowIf( nameof( CanCancelReload ), true )] public float ReloadCancelWindow { get; set; } = 0f;

	TimeSince _timeSinceReloadStart;

	// The engine cancels a reload from inside OnControl when a trigger is pulled. Anything else that
	// cancels (holstering) goes through untouched.
	bool _inControl;

	/// <summary>
	/// Is it too late in the current reload for a trigger pull to cancel it?
	/// </summary>
	public bool ReloadCommitted => IsReloading && ReloadCancelWindow > 0f && _timeSinceReloadStart > ReloadCancelWindow;

	protected override void OnReloadStarted()
	{
		_timeSinceReloadStart = 0;
		base.OnReloadStarted();
	}

	protected override void OnControl()
	{
		_inControl = true;

		try
		{
			base.OnControl();
		}
		finally
		{
			_inControl = false;
		}
	}

	public override void CancelReload()
	{
		if ( _inControl && ReloadCommitted ) return;

		base.CancelReload();
	}
}
