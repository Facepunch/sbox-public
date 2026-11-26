public sealed partial class CameraEntity
{
	/// <summary>
	/// Effects applied to the camera feed and to the player's view through it.
	/// </summary>
	[Property, Sync] public NetList<string> Effects { get; set; } = new();

	/// <summary>
	/// Saved property overrides for the camera's effects.
	/// </summary>
	[Property, Sync] public NetDictionary<string, string> EffectValues { get; set; } = new();

	/// <summary>
	/// Local effect components on the camera's lens.
	/// </summary>
	public CameraEffectStack LensEffects { get; } = new();

	/// <summary>
	/// Effects to instantiate for this camera, in saved order.
	/// </summary>
	public IEnumerable<string> ActiveEffects => Effects ?? Enumerable.Empty<string>();

	/// <summary>
	/// Whether the camera has the given effect enabled.
	/// </summary>
	public bool HasEffect( string path ) => Effects?.Contains( path ) == true;

	/// <summary>
	/// Key used to store an effect's property override on the camera.
	/// </summary>
	public static string ValueKey( string path, string setting ) => $"{path}|{setting}";

	/// <summary>
	/// Saved value of an effect property, or null to use its default.
	/// </summary>
	public string GetEffectValue( string path, string setting )
	{
		return EffectValues is not null && EffectValues.TryGetValue( ValueKey( path, setting ), out var value ) ? value : null;
	}

	/// <summary>
	/// Changes whenever saved effects or their property overrides change.
	/// </summary>
	internal int EffectsVersion
	{
		get
		{
			var hash = new HashCode();
			foreach ( var path in ActiveEffects ) hash.Add( path );
			if ( EffectValues is not null )
			{
				foreach ( var (key, value) in EffectValues ) hash.Add( HashCode.Combine( key, value ) );
			}
			return hash.ToHashCode();
		}
	}
}
