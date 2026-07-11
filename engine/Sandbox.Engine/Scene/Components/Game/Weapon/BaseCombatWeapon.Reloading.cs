namespace Sandbox;

public partial class BaseCombatWeapon
{
	//
	// Reloading. Host authoritative: the host runs the reload over time, topping the magazine up from
	// reserve, and the result replicates via the FromHost magazine and IsReloading. Reload tolerates the
	// round-trip (it isn't twitch-sensitive like firing), so it's not predicted - presentation follows
	// the synced state through OnReloadStarted/OnReloadInserted/OnReloadFinished, which a game overrides
	// to drive its sound and animation. Supports whole-magazine and one-at-a-time (incremental) reloads.
	//

	/// <summary>How long a (full) reload takes, in seconds. For incremental reloads, the time per round.</summary>
	[Property, Feature( "Reloading" )] public float ReloadTime { get; set; } = 2f;

	/// <summary>Load one round at a time instead of the whole magazine (shotgun-style).</summary>
	[Property, Feature( "Reloading" )] public bool IncrementalReloading { get; set; } = false;

	/// <summary>Delay before the first round goes in. Zero uses <see cref="ReloadTime"/>.</summary>
	[Property, Feature( "Reloading" ), ShowIf( nameof( IncrementalReloading ), true )] public float ReloadStartTime { get; set; } = 0f;

	/// <summary>Extra delay after the first round before the rest (e.g. a longer first insert). Incremental only.</summary>
	[Property, Feature( "Reloading" ), ShowIf( nameof( IncrementalReloading ), true )] public float FirstShellReloadTime { get; set; } = 0f;

	/// <summary>Can the reload be cancelled (e.g. by firing) part-way through?</summary>
	[Property, Feature( "Reloading" )] public bool CanCancelReload { get; set; } = true;

	/// <summary>Start a reload automatically when the trigger is pulled on an empty magazine.</summary>
	[Property, Feature( "Reloading" )] public bool AutoReload { get; set; } = true;

	/// <summary>True while a reload is in progress. Host authoritative, synced so every peer can animate.</summary>
	[Sync( SyncFlags.FromHost ), Change( nameof( OnReloadingChanged ) )] public bool IsReloading { get; private set; }

	// Bumped by the host when a reload is cancelled part-way, so every peer can tell a cancel from a
	// finish (IsReloading going false can't).
	[Sync( SyncFlags.FromHost ), Change( nameof( OnReloadCancelsChanged ) ), Expose] private int ReloadCancels { get; set; }

	private System.Threading.CancellationTokenSource _reloadCancel;

	/// <summary>
	/// How full a reload fills the primary magazine. Defaults to <see cref="PrimaryClipSize"/> -
	/// override for +1-in-the-chamber style reloads (e.g. <c>ClipMaxSize + (Clip1 > 0 ? 1 : 0)</c>).
	/// Read once when the reload starts.
	/// </summary>
	protected virtual int ReloadFillTarget => PrimaryClipSize;

	/// <summary>
	/// Can we start a reload? Yes when not already reloading, the magazine isn't full, and there's reserve
	/// to load. Override to add conditions.
	/// </summary>
	public virtual bool CanReload()
	{
		if ( IsReloading )
			return false;

		// The primary magazine wants rounds and there's reserve to load...
		if ( UsesPrimaryClip && Clip1 < ReloadFillTarget && Ammo1 > 0 )
			return true;

		// ...or the secondary magazine does.
		if ( UsesSecondaryClip && Clip2 < SecondaryClipSize && Ammo2 > 0 )
			return true;

		return false;
	}

	/// <summary>
	/// Begin reloading. Routed to the host, which runs it authoritatively. Override <see cref="OnReloadStarted"/>
	/// and friends for presentation rather than this.
	/// </summary>
	public virtual void Reload()
	{
		if ( !Networking.IsHost )
		{
			ReloadHost();
			return;
		}

		if ( !CanReload() )
			return;

		_ = RunReload();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void ReloadHost() => Reload();

	/// <summary>Abort a reload before it finishes, loading nothing further.</summary>
	public virtual void CancelReload()
	{
		if ( !IsReloading )
			return;

		if ( !Networking.IsHost )
		{
			// Only the owner can ask the host to cancel - proxies run this too (holster fires on every
			// peer) and just follow the synced state.
			if ( !IsProxy )
				CancelReloadHost();

			return;
		}

		_reloadCancel?.Cancel();
		ReloadCancels++;
		IsReloading = false;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void CancelReloadHost() => CancelReload();

	/// <summary>
	/// The host-side reload loop. Tops the magazine up from reserve - all at once, or one round at a time
	/// for incremental. Updates the FromHost magazine as it goes, so presentation can follow.
	/// </summary>
	private async System.Threading.Tasks.Task RunReload()
	{
		_reloadCancel?.Cancel();
		var cts = new System.Threading.CancellationTokenSource();
		_reloadCancel = cts;
		var ct = cts.Token;

		IsReloading = true;

		try
		{
			var firstRound = true;
			var waited = false;

			// Captured once - a +1-in-the-chamber target must not shift as rounds go in.
			var fillTarget = ReloadFillTarget;

			while ( Clip1 < fillTarget && Ammo1 > 0 && !ct.IsCancellationRequested )
			{
				var delay = (IncrementalReloading && firstRound && ReloadStartTime > 0f) ? ReloadStartTime : ReloadTime;
				await Task.DelaySeconds( delay, ct );
				waited = true;

				// The weapon can be destroyed while we slept - don't touch it after.
				if ( !IsValid || ct.IsCancellationRequested )
					break;

				var want = IncrementalReloading ? 1 : (fillTarget - Clip1);
				Clip1 += TakeReserveAmmo( PrimaryAmmoType, want );

				if ( IncrementalReloading && firstRound && FirstShellReloadTime > 0f )
					await Task.DelaySeconds( FirstShellReloadTime, ct );

				if ( !IsValid )
					break;

				firstRound = false;

				if ( !IncrementalReloading )
					break;
			}

			// A reload tops the secondary magazine up too (GMod reloads both clips) - taking the
			// reload time when the primary loop didn't already.
			if ( IsValid && !ct.IsCancellationRequested && UsesSecondaryClip && Clip2 < SecondaryClipSize && Ammo2 > 0 )
			{
				if ( !waited )
					await Task.DelaySeconds( ReloadTime, ct );

				if ( IsValid && !ct.IsCancellationRequested )
					Clip2 += TakeReserveAmmo( SecondaryAmmoType, SecondaryClipSize - Clip2 );
			}
		}
		catch ( System.OperationCanceledException ) { }
		finally
		{
			if ( _reloadCancel == cts )
			{
				_reloadCancel = null;

				if ( IsValid )
					IsReloading = false;
			}

			cts.Dispose();
		}
	}

	[Expose]
	private void OnReloadingChanged( bool oldValue, bool newValue )
	{
		if ( newValue )
			OnReloadStarted();
		else
			OnReloadFinished();
	}

	[Expose]
	private void OnReloadCancelsChanged( int oldValue, int newValue ) => OnReloadCancelled();

	// These drive the active weapon model's reload presentation (view model for the owner, world model for
	// everyone else). They fire on every peer off the synced IsReloading/Clip1, so the game just overrides
	// the hooks on its BaseWeaponModel. Override these to add weapon-level reactions on top.

	/// <summary>
	/// Reload started. Plays the reload animation on the weapon model, and fires the "b_reload" gesture on
	/// the holder's body. Fires once, on every peer, off the synced IsReloading.
	/// </summary>
	protected virtual void OnReloadStarted()
	{
		WeaponModel?.OnReloadStart();

		HolderRenderer?.Set( "b_reload", true );
	}

	/// <summary>Reload ended (finished or cancelled). Drives the active weapon model.</summary>
	protected virtual void OnReloadFinished() => WeaponModel?.OnReloadFinish();

	/// <summary>
	/// The reload was cancelled part-way (fired, holstered). Fires on every peer, alongside
	/// <see cref="OnReloadFinished"/> - use it to stop timed reload sounds and the like.
	/// </summary>
	protected virtual void OnReloadCancelled() => WeaponModel?.OnReloadCancel();

	/// <summary>A round was loaded (incremental). Drives the active weapon model's per-shell insert.</summary>
	protected virtual void OnReloadInserted() => WeaponModel?.OnIncrementalReload();
}
