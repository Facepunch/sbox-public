using System.Runtime.CompilerServices;

namespace Sandbox;

public abstract partial class Component
{
	internal enum TickPhase { Update, FixedUpdate, PreRender }

	sealed class TickProfileScopes( Type type )
	{
		internal readonly ProfileScope Update = new( $"{type.FullName ?? type.Name}.Update", "", 0 );
		internal readonly ProfileScope FixedUpdate = new( $"{type.FullName ?? type.Name}.FixedUpdate", "", 0 );
		internal readonly ProfileScope PreRender = new( $"{type.FullName ?? type.Name}.PreRender", "", 0 );
	}

	[SkipHotload] static readonly ConditionalWeakTable<Type, TickProfileScopes> TickScopeCache = new();
	[SkipHotload] TickProfileScopes _tickProfileScopes;

	internal ProfileScope GetTickProfileScope( TickPhase phase )
	{
		var scopes = _tickProfileScopes ??= TickScopeCache.GetValue( GetType(), static type => new( type ) );
		return phase switch
		{
			TickPhase.Update => scopes.Update,
			TickPhase.FixedUpdate => scopes.FixedUpdate,
			TickPhase.PreRender => scopes.PreRender,
			_ => throw new ArgumentOutOfRangeException( nameof( phase ) )
		};
	}
}
