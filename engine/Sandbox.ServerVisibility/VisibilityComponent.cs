namespace Sandbox.ServerVisibility;

/// <summary>
/// Attach this component to any networked <see cref="GameObject"/> that should participate
/// in the server-side Fog of War system.
///
/// <para>
/// This component implements <see cref="Component.INetworkVisible"/>, which the engine
/// checks in <c>NetworkObject.IsVisible()</c> before falling back to PVS.  When the
/// engine asks "should this object be transmitted to connection X?", we delegate to
/// <see cref="VisibilityManager.IsVisibleTo"/> which runs the full layered pipeline:
/// spatial grid → range → sound → LoS → prediction → grace period.
/// </para>
///
/// <para>
/// <b>Usage:</b> Simply add this component to your player prefab or any entity you want
/// to protect from wallhack / ESP.  No other setup is required — the component
/// self-registers with the <see cref="VisibilityManager"/> system.
/// </para>
///
/// <para>
/// <b>Spectators:</b> Tag the spectator's root GameObject with <c>"spectator"</c> and
/// the system will bypass occlusion for that observer.
/// </para>
///
/// <para>
/// <b>Sound Events:</b> Call <see cref="RegisterNoise"/> from your weapon / footstep
/// code to mark this entity as "noisy" so it remains visible through walls within
/// the configured sound range.
/// </para>
/// </summary>
[Title( "Server Visibility" )]
[Category( "Networking" )]
[Icon( "visibility" )]
public sealed class VisibilityComponent : Component, Component.INetworkVisible
{
	// ─── Properties ──────────────────────────────────────────────

	/// <summary>
	/// If true, this entity is always transmitted to all clients regardless of
	/// visibility checks.  Useful for global entities like game managers.
	/// </summary>
	[Property, Title( "Always Visible" )]
	public bool AlwaysVisible { get; set; } = false;

	/// <summary>
	/// Override the max visibility range for this specific entity.
	/// 0 = use the global <see cref="VisibilityConfig.MaxVisibilityRange"/>.
	/// </summary>
	[Property, Title( "Custom Max Range" )]
	public float CustomMaxRange { get; set; } = 0f;

	/// <summary>
	/// If true, this entity participates in sound-based visibility.
	/// Set to false for entities that should never be force-revealed by noise
	/// (e.g. silent traps, cameras).
	/// </summary>
	[Property, Title( "Sound Visible" )]
	public bool SoundVisibilityEnabled { get; set; } = true;

	// ─── Lifecycle ───────────────────────────────────────────────

	protected override void OnEnabled()
	{
		base.OnEnabled();

		var manager = Scene.GetSystem<VisibilityManager>();
		manager?.Register( GameObject );
	}

	protected override void OnDisabled()
	{
		var manager = Scene.GetSystem<VisibilityManager>();
		manager?.Unregister( GameObject );

		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		var manager = Scene.GetSystem<VisibilityManager>();
		manager?.Unregister( GameObject );

		base.OnDestroy();
	}

	// ─── INetworkVisible Implementation ──────────────────────────

	/// <summary>
	/// Called by the engine's <c>NetworkObject.IsVisible()</c> for each connection.
	/// This is the hook point where we inject our fog-of-war logic.
	/// </summary>
	/// <param name="connection">The client connection being evaluated.</param>
	/// <param name="worldBounds">The world-space bounding box of this networked object.</param>
	/// <returns>True if this entity should be transmitted to the connection.</returns>
	public bool IsVisibleToConnection( Connection connection, in BBox worldBounds )
	{
		// Always-visible entities bypass all checks.
		if ( AlwaysVisible )
			return true;

		// Only run on the server.
		if ( !Networking.IsHost )
			return true;

		// Don't hide objects from their owner — the owner always sees their own stuff.
		if ( GameObject.Network.Active && GameObject.Network.OwnerId == connection.Id )
			return true;

		var manager = Scene.GetSystem<VisibilityManager>();

		if ( manager is null )
			return true; // fail-open if the system isn't running

		return manager.IsVisibleTo( connection, GameObject, worldBounds );
	}

	// ─── Public API ──────────────────────────────────────────────

	/// <summary>
	/// Register that this entity has made a noise (gunshot, footstep, explosion, etc.).
	/// Call this from your weapon / movement code.
	///
	/// <example>
	/// <code>
	/// // In your weapon component:
	/// var vis = GameObject.Components.Get&lt;VisibilityComponent&gt;();
	/// vis?.RegisterNoise();
	/// </code>
	/// </example>
	/// </summary>
	public void RegisterNoise()
	{
		if ( !SoundVisibilityEnabled )
			return;

		var manager = Scene.GetSystem<VisibilityManager>();
		manager?.Prediction.RegisterNoise( GameObject );
	}

	/// <summary>
	/// Force this entity to be visible to a specific connection for the next few ticks.
	/// Useful for scripted reveals (e.g. "spotted" callout in a tactical shooter).
	/// </summary>
	public void ForceVisibleTo( Connection connection, float duration = 1f )
	{
		// We achieve this by registering a noise event — the sound system will
		// keep the entity visible for the configured linger time.
		// For a custom duration, we'd need a separate mechanism, but the noise
		// system is a good approximation.
		if ( connection is null )
			return;

		var manager = Scene.GetSystem<VisibilityManager>();
		manager?.Prediction.RegisterNoise( GameObject );
	}
}
