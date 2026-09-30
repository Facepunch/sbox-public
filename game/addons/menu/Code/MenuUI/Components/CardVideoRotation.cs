using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Sandbox.UI;

namespace MenuProject.UI;

/// <summary>
/// Decides which cards in a set play their video, keeping them apart on screen and handing
/// over one at a time. Cards register themselves; the owning panel drives <see cref="Tick"/>.
/// </summary>
public sealed class CardVideoRotation
{
	/// <summary>How many cards play at once.</summary>
	public int Count { get; set; } = 2;

	/// <summary>How long a card holds its turn before handing over.</summary>
	public float Interval { get; set; } = 10f;

	// Fraction of Interval each hand-over may drift, so the timing isn't mechanical.
	const float Jitter = 0.15f;

	class Turn
	{
		public Panel Card;
		public float NextSwitch;
	}

	readonly List<Panel> cards = new();
	readonly List<Turn> turns = new();

	// When each card last had a turn, so the longest wait goes next.
	readonly Dictionary<Panel, float> lastPlayed = new();

	// Random per instance, so separate shelves don't hand over together.
	readonly float phase = Game.Random.Float( 0f, 1f );

	public void Register( Panel card )
	{
		if ( card is null || cards.Contains( card ) )
			return;

		cards.Add( card );
	}

	public void Unregister( Panel card )
	{
		if ( card is null )
			return;

		cards.Remove( card );
		lastPlayed.Remove( card );

		foreach ( var turn in turns )
		{
			if ( turn.Card == card )
				turn.Card = null;
		}
	}

	public bool IsPlaying( Panel card )
	{
		foreach ( var turn in turns )
		{
			if ( turn.Card == card )
				return true;
		}

		return false;
	}

	/// <summary>Hand over any turn whose time is up, or whose card has gone away.</summary>
	public void Tick()
	{
		// Catches cards deleted without unregistering.
		if ( cards.RemoveAll( x => !x.IsValid() ) > 0 )
		{
			foreach ( var card in lastPlayed.Keys.Where( x => !x.IsValid() ).ToArray() )
				lastPlayed.Remove( card );
		}

		var want = Math.Max( 0, Count );

		while ( turns.Count > want ) turns.RemoveAt( turns.Count - 1 );
		while ( turns.Count < want ) turns.Add( new Turn() );

		for ( int i = 0; i < turns.Count; i++ )
		{
			var turn = turns[i];

			if ( Eligible( turn.Card ) && RealTime.Now < turn.NextSwitch )
				continue;

			var next = PickNext( turn );
			if ( next is null )
				continue;

			var first = !turn.Card.IsValid();

			turn.Card = next;
			turn.NextSwitch = RealTime.Now + Delay( i, first );
		}
	}

	// First hand-over spreads across the interval by turn and by phase; the rest just jitter.
	float Delay( int index, bool first )
	{
		var jitter = 1f + Game.Random.Float( -Jitter, Jitter );

		if ( first )
			return Interval * (index + 1f + phase) / turns.Count * jitter;

		return Interval * jitter;
	}

	Panel PickNext( Turn turn )
	{
		if ( cards.Count == 0 )
			return null;

		// Passes drop a rule at a time, so a cramped list still fills its quota.
		for ( int pass = 0; pass < 3; pass++ )
		{
			Panel best = null;
			var longestWait = float.MaxValue;

			foreach ( var card in cards )
			{
				if ( !Eligible( card ) || Taken( card, turn ) )
					continue;

				// Move on while there's somewhere to move to.
				if ( pass < 2 && card == turn.Card )
					continue;

				if ( pass < 2 && Crowded( card, turn, pass == 0 ) )
					continue;

				// Longest wait first. In list order the playing cards settle onto one set of
				// positions and everything between them stays blocked forever.
				var played = lastPlayed.GetValueOrDefault( card );

				if ( played >= longestWait )
					continue;

				best = card;
				longestWait = played;
			}

			if ( best is null )
				continue;

			lastPlayed[best] = RealTime.Now;

			return best;
		}

		return turn.Card;
	}

	bool Taken( Panel card, Turn turn )
	{
		foreach ( var other in turns )
		{
			if ( other != turn && other.Card == card )
				return true;
		}

		return false;
	}

	bool Crowded( Panel card, Turn turn, bool includeVertical )
	{
		foreach ( var other in turns )
		{
			if ( other == turn || !other.Card.IsValid() )
				continue;

			if ( Touches( card, other.Card, includeVertical ) )
				return true;
		}

		return false;
	}

	// Side by side always counts; stacked only when asked, that gap being the softer goal.
	static bool Touches( Panel a, Panel b, bool includeVertical )
	{
		var ra = a.Box.Rect;
		var rb = b.Box.Rect;

		// Just over half a card, so only immediate neighbours count.
		var padX = MathF.Max( ra.Width, rb.Width ) * 0.6f;
		var padY = MathF.Max( ra.Height, rb.Height ) * 0.6f;

		if ( Overlap( ra.Top, ra.Bottom, rb.Top, rb.Bottom ) &&
			 Overlap( ra.Left - padX, ra.Right + padX, rb.Left, rb.Right ) )
			return true;

		return includeVertical &&
			   Overlap( ra.Left, ra.Right, rb.Left, rb.Right ) &&
			   Overlap( ra.Top - padY, ra.Bottom + padY, rb.Top, rb.Bottom );
	}

	static bool Overlap( float a0, float a1, float b0, float b1 ) => a0 < b1 && b0 < a1;

	// Laid out and at least partly on screen - don't spend a decoder on a scrolled-away card.
	static bool Eligible( Panel card )
	{
		if ( !card.IsValid() )
			return false;

		var rect = card.Box.Rect;

		if ( rect.Width <= 0 || rect.Height <= 0 )
			return false;

		return Overlap( rect.Left, rect.Right, 0, Screen.Width ) &&
			   Overlap( rect.Top, rect.Bottom, 0, Screen.Height );
	}
}
