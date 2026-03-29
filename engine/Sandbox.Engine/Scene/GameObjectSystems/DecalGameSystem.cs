namespace Sandbox;

public sealed class DecalGameSystem : GameObjectSystem<DecalGameSystem>
{
	[ConVar( "maxdecals", ConVarFlags.Saved | ConVarFlags.Protected )]
	public static int MaxDecals { get; internal set; } = 1000;

	/// <summary>
	/// A list of decals that can be destroyed after a certain time.
	/// </summary>
	LinkedList<Decal> _transients = new();

	public DecalGameSystem( Scene scene ) : base( scene )
	{

	}

	[ConCmd( "r_cleardecals" )]
	internal static void ClearDecalsCmd() => Current?.ClearDecals();

	public void ClearDecals()
	{
		foreach ( var decal in _transients.ToArray() )
		{
			decal.Destroy();
		}

		_transients.Clear();
	}

	internal void AddTransient( Decal decal )
	{
		if ( decal is null || !decal.IsValid() )
			return;

		_transients.AddLast( decal );

		int max = MaxDecals;
		while ( _transients.Count > max )
		{
			var first = _transients.First;
			first.Value.Destroy();

			if ( first.List == _transients )
			{
				// If the decal was not removed by the destroy call, remove it here.
				_transients.Remove( first );
			}
		}
	}

	internal void RemoveTransient( Decal decal )
	{
		if ( decal is null ) return;

		_transients.Remove( decal );
	}
}
