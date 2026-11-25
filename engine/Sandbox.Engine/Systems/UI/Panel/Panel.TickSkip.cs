using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox.UI;

public partial class Panel
{
	[ConVar( "ui_skip_quiet_ticks", Help = "Skip ticking a UI subtree whose last tick had nothing to do while nothing in it has changed since" )]
	internal static bool SkipQuietTicks { get; set; } = true;

	/// <summary>
	/// The <see cref="PaintClock"/> value when this panel and everything under it last ticked with nothing to do, or 0. While
	/// nothing in the subtree is stamped since (<see cref="PaintChangedAt"/> is below it), ticking it again would do nothing,
	/// so its parent skips it. Things that give a tick work without stamping wake it (<see cref="WakeTick"/>).
	/// </summary>
	long _tickQuietAt;

	/// <summary>Subtrees skipped by their parent's tick, for tests and diagnostics.</summary>
	internal static int SkippedTicks;

	static readonly ConditionalWeakTable<Type, object> _ticksItself = new();
	static readonly object _ticks = new(), _doesNotTick = new();

	/// <summary>
	/// Whether this panel's type overrides <see cref="Tick"/> or <see cref="AddScrollVelocity"/>: its tick does work every
	/// frame that nothing stamps.
	/// </summary>
	bool TicksItself => _ticksItself.GetValue( GetType(), static type =>
	{
		for ( var current = type; current != typeof( Panel ); current = current.BaseType )
		{
			foreach ( var method in current.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly ) )
			{
				if ( method.Name is not (nameof( Tick ) or nameof( AddScrollVelocity )) || method.GetParameters().Length != 0 ) continue;
				if ( method.GetBaseDefinition().DeclaringType == typeof( Panel ) ) return _ticks;
			}
		}

		return _doesNotTick;
	} ) == _ticks;

	/// <summary>
	/// Whether this panel's own part of a tick that just ran had nothing to do and nothing to wait for: no tick of its own,
	/// no razor tree, no queued events or class binds, no style to work out, no transitions or animations, no textures that
	/// may still change size, and no scrolling.
	/// </summary>
	bool TickedQuietly( bool hasRenderTree )
	{
		if ( hasRenderTree || parametersChanged || ParentHasChanged || IsDeleting ) return false;
		if ( PendingEvents is { Count: > 0 } || classBinds is { Count: > 0 } ) return false;
		if ( Style is null || Style.IsDirty || HasActiveTransitions ) return false;
		if ( ComputedStyle is not { } style || style.IsAnimationActive ) return false;
		if ( style.OverflowX == OverflowMode.Scroll || style.OverflowY == OverflowMode.Scroll ) return false;
		if ( ScrollVelocity != default || isScrolling || IsDragScrolling ) return false;
		if ( _paintCache.Background.Texture is not null || _paintCache.Layer?.HasMask == true ) return false;
		return !TicksItself;
	}

	/// <summary>
	/// Whether this child's tick can be skipped: its subtree ticked quietly, nothing in it is stamped since, and it's in
	/// the scene its parent is in (which it would otherwise take on).
	/// </summary>
	bool CanSkipTick( Panel parent )
	{
		return _tickQuietAt != 0 && PaintChangedAt < _tickQuietAt && _gameObject is null && _indexedScene == parent._indexedScene
			&& SkipQuietTicks;
	}

	/// <summary>
	/// Called when something gives this panel's tick work without stamping it: an event queued, a class bound, parameters
	/// set, a new parent, deletion, a ::before or ::after element to make. Its ancestors tick it again. Goes all the way up:
	/// a hidden panel is quiet over children it doesn't tick.
	/// </summary>
	internal void WakeTick()
	{
		for ( var panel = this; panel is not null; panel = panel._parent )
			panel._tickQuietAt = 0;
	}
}
