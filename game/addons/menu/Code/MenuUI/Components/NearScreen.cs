using Sandbox;
using Sandbox.UI;
using System;

namespace MenuProject.UI;

/// <summary>
/// For a long list of rows of different heights: which are near enough what's on screen to build, and
/// how tall each was when it last was, so the rest can keep their room as an empty row of the same
/// height - the list and its scrollbar don't move as rows come and go. A screen's worth either side
/// is built, so a row's there before it scrolls into view, and it's only taken down again a good way
/// further off. The list's owner calls <see cref="Update"/> every tick, and rebuilds when it says the
/// built rows changed.
/// </summary>
public sealed class NearScreen
{
	bool[] _built = Array.Empty<bool>();
	float[] _heights = Array.Empty<float>();

	/// <summary>
	/// Changes whenever which rows are built does - for the owner's BuildHash.
	/// </summary>
	public int Hash { get; private set; }

	/// <summary>
	/// Start again - a new set of rows. Nothing built until the next update has a look.
	/// </summary>
	public void Reset()
	{
		_built = Array.Empty<bool>();
		_heights = Array.Empty<float>();
		Hash = 0;
	}

	public bool IsBuilt( int i ) => i < _built.Length && _built[i];

	/// <summary>
	/// The row's height, in unscaled px, as the style wants it - its measured one once it's been built,
	/// the guess until then.
	/// </summary>
	public string HeightOf( int i, float guess )
	{
		var height = i < _heights.Length && _heights[i] > 0 ? _heights[i] : guess;
		return height.ToString( "0.#", System.Globalization.CultureInfo.InvariantCulture );
	}

	/// <summary>
	/// Have a look at what's on screen. <paramref name="rows"/> holds the rows, one child each, first;
	/// <paramref name="built"/> gives the part of a row whose height to remember, or null while it's
	/// still its empty stand-in. True if which rows are built changed - rebuild. The list should hide
	/// its rows' :outro, so one being swapped out doesn't sit beside its replacement for a frame.
	/// </summary>
	public bool Update( Panel owner, Panel rows, int count, Func<Panel, Panel> built )
	{
		if ( !rows.IsValid() ) return false;

		if ( _built.Length != count )
		{
			_built = new bool[count];
			_heights = new float[count];
		}

		// What's on screen - the screen, cut down by everything that scrolls the list, its owner included
		var top = float.MinValue;
		var bottom = float.MaxValue;
		for ( var p = owner; p is not null; p = p.Parent )
		{
			if ( p.Parent is not null && p.ComputedStyle?.OverflowY != OverflowMode.Scroll ) continue;

			top = MathF.Max( top, p.Box.Rect.Top );
			bottom = MathF.Min( bottom, p.Box.Rect.Bottom );
		}

		// Not laid out yet - nothing to go on
		if ( bottom <= top ) return false;

		// Built within a screen of what's on screen, taken down only past three - the gap between the two
		// is what stops a row on the edge flipping every frame: built, it moves the rows round it, which
		// unbuilds it, which moves them back
		var screen = bottom - top;
		var buildTop = top - screen;
		var buildBottom = bottom + screen;
		var keepTop = top - screen * 3;
		var keepBottom = bottom + screen * 3;

		var changed = false;
		var i = 0;

		foreach ( var row in rows.Children )
		{
			if ( i >= count ) break;

			// One swapped out a moment ago, still there through its outro - not a row any more. Counted,
			// every row after it was off by one, and the flips that set off kept it going
			if ( row.IsDeleting ) continue;

			// Built and laid out - remember how tall it is, for when it's off screen again
			if ( built( row ) is { } part && part.Box.Rect.Height > 0 )
				_heights[i] = part.Box.Rect.Height * owner.ScaleFromScreen;

			var rect = row.Box.Rect;
			var near = _built[i]
				? rect.Bottom >= keepTop && rect.Top <= keepBottom
				: rect.Bottom >= buildTop && rect.Top <= buildBottom;

			if ( near != _built[i] )
			{
				_built[i] = near;
				changed = true;
			}

			i++;
		}

		if ( !changed ) return false;

		var hash = 0;
		for ( var k = 0; k < count; k++ )
			if ( _built[k] ) hash = HashCode.Combine( hash, k );

		Hash = hash;
		return true;
	}
}
