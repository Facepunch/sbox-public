using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox.UI;

public partial class Panel
{
	[ConVar( "ui_cache_wants_mouse", Help = "Keep each UI panel's answer to whether it wants the mouse while nothing in its subtree has changed" )]
	internal static bool CacheWantsMouse { get; set; } = true;

	/// <summary>
	/// The <see cref="PaintClock"/> value when <see cref="_wantsMouse"/> was worked out from this panel and everything under
	/// it, or 0. While nothing in the subtree is stamped since (<see cref="PaintChangedAt"/> is below it), it still holds:
	/// style, visibility and child changes all stamp.
	/// </summary>
	long _wantsMouseAt;
	bool _wantsMouse;

	/// <summary>Panels whose base <see cref="WantsMouseInput"/> answer was worked out rather than kept, for tests.</summary>
	internal static int WantsMouseWorkedOut;

	// 0 = not looked up yet, 1 = the base WantsMouseInput, 2 = the type overrides it
	byte _wantsMouseOverride;

	static readonly ConditionalWeakTable<Type, object> _overridesWantsMouse = new();
	static readonly object _ownWantsMouse = new(), _baseWantsMouse = new();

	/// <summary>
	/// Whether this panel's type overrides <see cref="WantsMouseInput"/>, whose answer can then change without a stamp.
	/// </summary>
	bool OverridesWantsMouseInput
	{
		get
		{
			if ( _wantsMouseOverride == 0 )
			{
				_wantsMouseOverride = _overridesWantsMouse.GetValue( GetType(), static type =>
				{
					var method = type.GetMethod( nameof( WantsMouseInput ), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes );
					return method?.DeclaringType == typeof( Panel ) ? _baseWantsMouse : _ownWantsMouse;
				} ) == _ownWantsMouse ? (byte)2 : (byte)1;
			}

			return _wantsMouseOverride == 2;
		}
	}

	/// <summary>
	/// <see cref="WantsMouseInput"/> for a parent asking a child, with whether the answer can be kept until the child's
	/// subtree is stamped.
	/// </summary>
	bool ChildWantsMouseInput( out bool keepable )
	{
		if ( OverridesWantsMouseInput )
		{
			keepable = false;
			return WantsMouseInput();
		}

		return WantsMouseInputCached( out keepable );
	}

	/// <summary>
	/// The base <see cref="WantsMouseInput"/>: visible with pointer-events, or a child that wants it. Kept while nothing in
	/// the subtree is stamped, unless a panel it asked overrides <see cref="WantsMouseInput"/>.
	/// </summary>
	bool WantsMouseInputCached( out bool keepable )
	{
		keepable = true;
		if ( _wantsMouseAt != 0 && PaintChangedAt < _wantsMouseAt && CacheWantsMouse )
			return _wantsMouse;

		WantsMouseWorkedOut++;
		var wants = false;
		if ( ComputedStyle is { } style && IsVisibleSelf )
		{
			if ( style.PointerEvents == PointerEvents.All )
			{
				wants = true;
			}
			else if ( _children is not null )
			{
				foreach ( var child in _children )
				{
					if ( child is null ) continue;
					var childWants = child.ChildWantsMouseInput( out var childKeepable );
					keepable &= childKeepable;
					if ( childWants )
					{
						wants = true;
						break;
					}
				}
			}
		}

		_wantsMouse = wants;
		_wantsMouseAt = keepable ? PaintClock : 0;
		return wants;
	}
}
