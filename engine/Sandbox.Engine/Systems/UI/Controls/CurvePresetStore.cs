using System.Runtime.CompilerServices;

namespace Sandbox.UI;

/// <summary>
/// Shared, persistent presets for one cookie store.
/// </summary>
internal sealed class CurvePresetStore
{
	internal sealed record Preset( Guid Id, Curve Curve );

	const string CookieKey = "curveeditor.saved-presets";
	const string LegacyCookieKey = "curveeditor.presets";
	static readonly ConditionalWeakTable<CookieContainer, CurvePresetStore> Stores = new();
	static CookieContainer _defaultCookies;

	readonly CookieContainer _cookies;
	readonly List<Preset> _presets;

	public event Action Changed;
	public IReadOnlyList<Preset> Presets => _presets.ToArray();

	public static CurvePresetStore ForCurrentContext()
	{
		var cookies = Game.Cookies ?? (_defaultCookies ??= new CookieContainer( "curve-presets", true ));
		return Stores.GetValue( cookies, store => new CurvePresetStore( store ) );
	}

	CurvePresetStore( CookieContainer cookies )
	{
		_cookies = cookies;
		_presets = cookies.Get<List<Preset>>( CookieKey, null );
		if ( _presets is not null )
			return;

		// Keep curves saved before presets had stable identities.
		var legacy = cookies.Get<List<Curve>>( LegacyCookieKey, new() );
		_presets = legacy.Select( curve => new Preset( Guid.NewGuid(), curve ) ).ToList();
	}

	public void Add( Curve curve )
	{
		_presets.Add( new Preset( Guid.NewGuid(), curve ) );
		Save();
	}

	public void Replace( Guid id, Curve curve )
	{
		var index = _presets.FindIndex( preset => preset.Id == id );
		if ( index < 0 )
			return;

		_presets[index] = new Preset( id, curve );
		Save();
	}

	public void Remove( Guid id )
	{
		if ( _presets.RemoveAll( preset => preset.Id == id ) == 0 )
			return;
		Save();
	}

	void Save()
	{
		_cookies.Set( CookieKey, _presets );
		_cookies.Save();
		Changed?.Invoke();
	}
}
