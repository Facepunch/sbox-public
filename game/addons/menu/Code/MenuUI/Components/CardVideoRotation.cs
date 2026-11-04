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

	// A breather between one card's clip ending and the next one starting. A bit under half an
	// interval: the turns start half an interval apart, so two turns alternate, one starting midway
	// through the other - and the next start lands a second or two before the other clip ends,
	// which is what its download and fade-in take, so the shelf never sits with nothing playing.
	float Rest => Interval * 0.35f;

	// Shared by every rotation, so a page of shelves brings its videos in one at a time
	// rather than all at once, which swamps a slow connection.
	const float StartGap = 0.75f;
	static float lastStart = float.MinValue;

	// Within one rotation, starts are at least this far apart - the interval shared out between
	// the turns. Enforced on every start, not just the first, so turns that end together (a
	// scroll took their cards away, or they all began before any card was laid out) are spread
	// out again instead of ending and resting in lockstep, which leaves the set dark for a while.
	float LocalGap => turns.Count > 0 ? Interval / turns.Count : 0f;
	float lastLocalStart = float.MinValue;
	bool primed;

	class Turn
	{
		public Panel Card;
		public Panel Last;
		public float NextSwitch;
		public float RestUntil;
	}

	readonly List<Panel> cards = new();
	readonly List<Turn> turns = new();

	// When each card last had a turn, so the longest wait goes next.
	readonly Dictionary<Panel, float> lastPlayed = new();

	// A card's wait counts this many times over, so a heavy card gets turns more often.
	readonly Dictionary<Panel, float> weights = new();

	// Random per instance, so separate shelves don't start and hand over on the same beat.
	readonly float phase = Game.Random.Float( 0f, 0.5f );

	public void Register( Panel card, float weight = 1f )
	{
		if ( card is null )
			return;

		weights[card] = weight;

		if ( cards.Contains( card ) )
			return;

		cards.Add( card );

		// A random head start, so shelves don't all walk their cards in the same order and
		// end up playing the same column on top of each other.
		lastPlayed[card] = -Game.Random.Float( 0f, Interval );
	}

	public void Unregister( Panel card )
	{
		if ( card is null )
			return;

		cards.Remove( card );
		lastPlayed.Remove( card );
		weights.Remove( card );

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

			foreach ( var card in weights.Keys.Where( x => !x.IsValid() ).ToArray() )
				weights.Remove( card );
		}

		var want = Math.Max( 0, Count );

		while ( turns.Count > want ) turns.RemoveAt( turns.Count - 1 );
		while ( turns.Count < want ) turns.Add( new Turn() );

		// The first start lands part way into a gap, by phase, so stacked shelves differ.
		if ( !primed && turns.Count > 0 )
		{
			primed = true;
			lastLocalStart = RealTime.Now - LocalGap * (1f - phase);
		}

		for ( int i = 0; i < turns.Count; i++ )
		{
			var turn = turns[i];

			if ( Eligible( turn.Card ) && RealTime.Now < turn.NextSwitch )
				continue;

			// Time's up: drop the card so its clip fades out, and rest before the next. A card
			// scrolled away mid-turn just stops - no rest, so the shelf isn't dark when you come back.
			if ( turn.Card.IsValid() )
			{
				turn.Last = turn.Card;
				turn.RestUntil = Eligible( turn.Card ) ? RealTime.Now + Rest : RealTime.Now;
				turn.Card = null;
				continue;
			}

			if ( RealTime.Now < turn.RestUntil )
				continue;

			if ( RealTime.Now - lastStart < StartGap || RealTime.Now - lastLocalStart < LocalGap )
				break;

			var next = PickNext( turn );
			if ( next is null )
				continue;

			lastStart = lastLocalStart = RealTime.Now;

			turn.Card = next;
			turn.NextSwitch = RealTime.Now + Interval * (1f + Game.Random.Float( -Jitter, Jitter ));
		}
	}

	Panel PickNext( Turn turn )
	{
		if ( cards.Count == 0 )
			return null;

		// Passes drop a rule at a time, so a cramped list still fills its quota.
		for ( int pass = 0; pass < 3; pass++ )
		{
			Panel best = null;
			var longestWait = float.MinValue;

			foreach ( var card in cards )
			{
				if ( !Eligible( card ) || Taken( card, turn ) )
					continue;

				// Move on while there's somewhere to move to.
				if ( pass < 2 && card == turn.Last )
					continue;

				if ( pass < 2 && Crowded( card, turn, pass == 0 ) )
					continue;

				// Longest wait first. In list order the playing cards settle onto one set of
				// positions and everything between them stays blocked forever.
				var wait = (RealTime.Now - lastPlayed.GetValueOrDefault( card )) * weights.GetValueOrDefault( card, 1f );

				if ( wait <= longestWait )
					continue;

				best = card;
				longestWait = wait;
			}

			if ( best is null )
				continue;

			lastPlayed[best] = RealTime.Now;

			return best;
		}

		return null;
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
