namespace Sandbox;

/// <summary>
/// Static API for convenient access to layer management in the active scene.
/// Provides runtime layer control for gameplay mechanics.
/// </summary>
[Expose]
public static class Layers
{
	/// <summary>
	/// Gets the LayerManager for the active scene.
	/// </summary>
	private static LayerManager Manager => LayerManager.Current;

	#region Query

	/// <summary>
	/// Gets a layer by name from the active scene.
	/// </summary>
	public static Layer Get( string name ) => Manager?.GetLayer( name );

	/// <summary>
	/// Gets a layer by ID from the active scene.
	/// </summary>
	public static Layer Get( Guid id ) => Manager?.GetLayer( id );

	/// <summary>
	/// Gets all layers in the active scene.
	/// </summary>
	public static IReadOnlyList<Layer> All => Manager?.All ?? Array.Empty<Layer>();

	/// <summary>
	/// Gets all visible layers in the active scene.
	/// </summary>
	public static IReadOnlyList<Layer> GetVisible() => Manager?.Visible ?? Array.Empty<Layer>();

	/// <summary>
	/// Gets all layers with the specified tag.
	/// </summary>
	public static IReadOnlyList<Layer> GetByTag( string tag ) => Manager?.GetByTag( tag ) ?? Array.Empty<Layer>();

	/// <summary>
	/// Gets the default layer.
	/// </summary>
	public static Layer Default => Manager?.DefaultLayer;

	#endregion

	#region Visibility Control

	/// <summary>
	/// Shows a layer by name.
	/// </summary>
	public static void Show( string layerName ) => Manager?.Show( layerName );

	/// <summary>
	/// Hides a layer by name.
	/// </summary>
	public static void Hide( string layerName ) => Manager?.Hide( layerName );

	/// <summary>
	/// Toggles a layer's visibility by name.
	/// </summary>
	public static void Toggle( string layerName ) => Manager?.Toggle( layerName );

	/// <summary>
	/// Sets a layer's visibility by name.
	/// </summary>
	public static void SetVisible( string layerName, bool visible ) => Manager?.SetVisible( layerName, visible );

	/// <summary>
	/// Shows only the specified layer, hiding all others.
	/// </summary>
	public static void Solo( string layerName ) => Manager?.Solo( layerName );

	/// <summary>
	/// Shows all layers.
	/// </summary>
	public static void ShowAll() => Manager?.ShowAll();

	/// <summary>
	/// Hides all layers.
	/// </summary>
	public static void HideAll() => Manager?.HideAll();

	/// <summary>
	/// Shows only the specified layers, hiding all others.
	/// </summary>
	public static void ShowOnly( params string[] layerNames ) => Manager?.ShowOnly( layerNames );

	/// <summary>
	/// Sets visibility for layers matching a predicate.
	/// </summary>
	public static void SetVisibleWhere( Func<Layer, bool> predicate, bool visible ) =>
		Manager?.SetVisibleWhere( predicate, visible );

	#endregion

	#region Presets/Snapshots

	/// <summary>
	/// Saves the current layer visibility state as a named preset.
	/// </summary>
	public static void SavePreset( string presetName ) => Manager?.SavePreset( presetName );

	/// <summary>
	/// Loads a previously saved preset.
	/// </summary>
	public static bool LoadPreset( string presetName ) => Manager?.LoadPreset( presetName ) ?? false;

	/// <summary>
	/// Captures the current layer visibility/opacity state as a snapshot.
	/// </summary>
	public static LayerSnapshot CaptureSnapshot() => Manager?.CaptureSnapshot();

	/// <summary>
	/// Restores layer visibility/opacity state from a snapshot.
	/// </summary>
	public static void RestoreSnapshot( LayerSnapshot snapshot ) => Manager?.RestoreSnapshot( snapshot );

	/// <summary>
	/// Gets all available preset names.
	/// </summary>
	public static IEnumerable<string> GetPresetNames() => Manager?.GetPresetNames() ?? Enumerable.Empty<string>();

	/// <summary>
	/// Deletes a preset by name.
	/// </summary>
	public static bool DeletePreset( string presetName ) => Manager?.DeletePreset( presetName ) ?? false;

	#endregion

	#region Events

	/// <summary>
	/// Event fired when any layer's visibility changes.
	/// </summary>
	public static event Action<Layer> OnLayerVisibilityChanged
	{
		add
		{
			var manager = Manager;
			if ( manager is not null )
				manager.OnLayerVisibilityChanged += value;
		}
		remove
		{
			var manager = Manager;
			if ( manager is not null )
				manager.OnLayerVisibilityChanged -= value;
		}
	}

	/// <summary>
	/// Event fired when a layer is created.
	/// </summary>
	public static event Action<Layer> OnLayerCreated
	{
		add
		{
			var manager = Manager;
			if ( manager is not null )
				manager.OnLayerCreated += value;
		}
		remove
		{
			var manager = Manager;
			if ( manager is not null )
				manager.OnLayerCreated -= value;
		}
	}

	/// <summary>
	/// Event fired when a layer is deleted.
	/// </summary>
	public static event Action<Layer> OnLayerDeleted
	{
		add
		{
			var manager = Manager;
			if ( manager is not null )
				manager.OnLayerDeleted += value;
		}
		remove
		{
			var manager = Manager;
			if ( manager is not null )
				manager.OnLayerDeleted -= value;
		}
	}

	/// <summary>
	/// Event fired when a member changes layers.
	/// </summary>
	public static event Action<LayerMember, Layer, Layer> OnMemberLayerChanged
	{
		add
		{
			var manager = Manager;
			if ( manager is not null )
				manager.OnMemberLayerChanged += value;
		}
		remove
		{
			var manager = Manager;
			if ( manager is not null )
				manager.OnMemberLayerChanged -= value;
		}
	}

	#endregion

	#region Transitions

	/// <summary>
	/// Fades a layer in over the specified duration.
	/// </summary>
	public static async Task FadeIn( string layerName, float duration )
	{
		var layer = Get( layerName );
		if ( layer is null ) return;

		layer.Visible = true;
		await FadeOpacity( layer, layer.Opacity, 1f, duration );
	}

	/// <summary>
	/// Fades a layer out over the specified duration.
	/// </summary>
	public static async Task FadeOut( string layerName, float duration )
	{
		var layer = Get( layerName );
		if ( layer is null ) return;

		await FadeOpacity( layer, layer.Opacity, 0f, duration );
		layer.Visible = false;
	}

	/// <summary>
	/// Cross-fades between two layers over the specified duration.
	/// </summary>
	public static async Task CrossFade( string fromLayer, string toLayer, float duration )
	{
		var from = Get( fromLayer );
		var to = Get( toLayer );

		if ( from is null && to is null ) return;

		// Run both fades in parallel
		var tasks = new List<Task>();

		if ( from is not null )
		{
			tasks.Add( FadeOpacity( from, from.Opacity, 0f, duration ).ContinueWith( _ =>
			{
				from.Visible = false;
			} ) );
		}

		if ( to is not null )
		{
			to.Visible = true;
			to.Opacity = 0f;
			tasks.Add( FadeOpacity( to, 0f, 1f, duration ) );
		}

		await Task.WhenAll( tasks );
	}

	/// <summary>
	/// Fades a layer's opacity from one value to another.
	/// </summary>
	private static async Task FadeOpacity( Layer layer, float from, float to, float duration )
	{
		if ( layer is null || duration <= 0 )
		{
			if ( layer is not null )
				layer.Opacity = to;
			return;
		}

		var elapsed = 0f;
		var startOpacity = from;

		while ( elapsed < duration )
		{
			if ( layer is null || !layer.IsValid )
				break;

			elapsed += Time.Delta;
			var t = Math.Min( elapsed / duration, 1f );

			// Use smooth step for nicer transitions
			t = t * t * ( 3f - 2f * t );

			layer.Opacity = MathX.Lerp( startOpacity, to, t );

			await Task.Frame();
		}

		if ( layer is not null && layer.IsValid )
		{
			layer.Opacity = to;
		}
	}

	/// <summary>
	/// Sets opacity for a layer with optional animation.
	/// </summary>
	public static async Task SetOpacity( string layerName, float opacity, float duration = 0f )
	{
		var layer = Get( layerName );
		if ( layer is null ) return;

		if ( duration <= 0 )
		{
			layer.Opacity = opacity;
			return;
		}

		await FadeOpacity( layer, layer.Opacity, opacity, duration );
	}

	#endregion

	#region Batch Operations

	/// <summary>
	/// Begins a batch update. Layer state changes are deferred until EndBatchUpdate.
	/// </summary>
	public static void BeginBatchUpdate() => Manager?.BeginBatchUpdate();

	/// <summary>
	/// Ends a batch update and applies all pending layer state changes.
	/// </summary>
	public static void EndBatchUpdate() => Manager?.EndBatchUpdate();

	/// <summary>
	/// Executes an action within a batch update scope.
	/// </summary>
	public static void Batch( Action action )
	{
		if ( action is null ) return;

		BeginBatchUpdate();
		try
		{
			action();
		}
		finally
		{
			EndBatchUpdate();
		}
	}

	#endregion

	#region Layer Creation

	/// <summary>
	/// Creates a new layer with the specified name.
	/// </summary>
	public static Layer Create( string name = null ) => Manager?.CreateLayer( name );

	/// <summary>
	/// Creates a new layer group with the specified name.
	/// </summary>
	public static Layer CreateGroup( string name = null ) => Manager?.CreateGroup( name );

	/// <summary>
	/// Deletes a layer by name.
	/// </summary>
	public static bool Delete( string layerName )
	{
		var layer = Get( layerName );
		if ( layer is null ) return false;
		return Manager?.DeleteLayer( layer ) ?? false;
	}

	/// <summary>
	/// Deletes a layer.
	/// </summary>
	public static bool Delete( Layer layer ) => Manager?.DeleteLayer( layer ) ?? false;

	#endregion
}
