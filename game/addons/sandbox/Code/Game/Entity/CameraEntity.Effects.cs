public sealed partial class CameraEntity
{
	[Property, Sync] public NetList<string> Effects { get; set; } = new();
	[Property, Sync] public NetDictionary<string, string> EffectValues { get; set; } = new();

	public CameraEffectStack LensEffects { get; } = new();

	readonly Dictionary<string, string> _pendingValues = new();

	public IEnumerable<string> ActiveEffects => Effects ?? Enumerable.Empty<string>();

	public bool HasEffect( string path ) => Effects?.Contains( path ) == true;

	public static string ValueKey( string path, string setting ) => $"{path}|{setting}";

	string SyncedValue( string key ) => EffectValues is not null && EffectValues.TryGetValue( key, out var value ) ? value : null;

	IEnumerable<string> SyncedKeys => EffectValues?.Keys ?? Enumerable.Empty<string>();

	public string GetEffectValue( string path, string setting )
	{
		var key = ValueKey( path, setting );
		return _pendingValues.TryGetValue( key, out var pending ) ? pending : SyncedValue( key );
	}

	public IEnumerable<string> ChangedSettings( string path )
	{
		var prefix = ValueKey( path, "" );
		return SyncedKeys.Concat( _pendingValues.Keys )
			.Where( key => key.StartsWith( prefix, StringComparison.Ordinal ) )
			.Select( key => key[prefix.Length..] )
			.Distinct()
			.Where( setting => GetEffectValue( path, setting ) is not null )
			.ToList();
	}

	public void EditEffectValue( string path, string setting, string json )
	{
		_pendingValues[ValueKey( path, setting )] = json;
		SetEffectValue( path, setting, json );
	}

	[Rpc.Host]
	public void SetEffectEnabled( string path, bool enabled )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return;

		// the host owns these, so it's the one that makes them when a camera has none yet
		Effects ??= new();

		if ( enabled && !Effects.Contains( path ) ) Effects.Add( path );
		else if ( !enabled && Effects.Remove( path ) ) RemoveValues( path );
	}

	[Rpc.Host]
	void SetEffectValue( string path, string setting, string json )
	{
		if ( string.IsNullOrWhiteSpace( path ) || string.IsNullOrWhiteSpace( setting ) ) return;

		var key = ValueKey( path, setting );
		if ( json is null )
		{
			EffectValues?.Remove( key );
			return;
		}

		EffectValues ??= new();
		EffectValues[key] = json;
	}

	public void ResetEffect( string path )
	{
		foreach ( var setting in ChangedSettings( path ) )
			_pendingValues[ValueKey( path, setting )] = null;

		ResetEffectOnHost( path );
	}

	[Rpc.Host]
	void ResetEffectOnHost( string path ) => RemoveValues( path );

	void RemoveValues( string path )
	{
		if ( EffectValues is null ) return;

		var prefix = ValueKey( path, "" );
		foreach ( var key in EffectValues.Keys.Where( x => x.StartsWith( prefix, StringComparison.Ordinal ) ).ToList() )
			EffectValues.Remove( key );
	}

	/// <summary>
	/// Changes whenever the effects properties do, so their copies only update then.
	/// </summary>
	internal int EffectsVersion
	{
		get
		{
			var hash = new HashCode();
			foreach ( var path in ActiveEffects ) hash.Add( path );
			foreach ( var key in SyncedKeys ) hash.Add( HashCode.Combine( key, SyncedValue( key ) ) );
			foreach ( var (key, value) in _pendingValues ) hash.Add( HashCode.Combine( key, value ) );
			return hash.ToHashCode();
		}
	}

	void UpdateEffects()
	{
		// an edit is done once the host's copy matches it
		foreach ( var (key, value) in _pendingValues.ToList() )
		{
			if ( SyncedValue( key ) == value )
				_pendingValues.Remove( key );
		}

		LensEffects.Apply( Feed?.Camera?.GameObject, this );
	}
}
