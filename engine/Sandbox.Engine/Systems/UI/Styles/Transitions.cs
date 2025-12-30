using Sandbox.Utility;

namespace Sandbox.UI;

/// <summary>
/// Handles the storage, progression and application of CSS transitions for a single <see cref="Panel"/>.
/// </summary>
public sealed class Transitions
{
	public delegate void TransitionFunction( Styles style, float delta );

	public struct Entry
	{
		public string Property { get; init; }
		public double StartTime { get; init; }
		public double Length { get; init; }
		public int Target { get; init; }
		public Utility.Easing.Function EasingFunction { get; init; }
		public bool IsKilled { get; private set; }

		public TransitionFunction Action { get; init; }

		public Entry( string property, double startTime, double length, int target, TransitionFunction action, Easing.Function easingFunction ) : this()
		{
			Property = property;
			StartTime = startTime;
			Length = length;
			Target = target;
			Action = action;
			EasingFunction = easingFunction;

			IsKilled = false;
		}

		internal void Kill()
		{
			IsKilled = true;
		}

		internal void Restore()
		{
			IsKilled = false;
		}

		public float Ease( float delta ) => EasingFunction.Invoke( delta );
		public void Invoke( Styles style, float delta ) => Action.Invoke( style, delta );

		public override string ToString()
		{
			return $"Entry( '{Property}', {StartTime}s, {Length}s, '{Target}', {Action}, {EasingFunction} )";
		}
	}

	/// <summary>
	/// Active CSS transitions.
	/// </summary>
	public List<Entry> Entries = null;

	/// <summary>
	/// Whether there are any active CSS transitions.
	/// </summary>
	public bool HasAny => Entries?.Count > 0;

	private Panel panel;

	internal Transitions( Panel panel )
	{
		this.panel = panel;
	}

	internal void Kill( Styles from )
	{
		if ( !from.HasTransitions ) return;
		if ( Entries == null ) return;

		for ( int i = 0; i < Entries.Count; i++ )
		{
			if ( !from.Transitions.List.Any( x => x.Property == Entries[i].Property ) )
				continue;

			var transition = Entries[i];
			transition.Kill();
			Entries[i] = transition;
		}
	}

	/// <summary>
	/// Clear all transitions. This will immediately remove transitions, leaving styles wherever they are.
	/// </summary>
	internal void Clear()
	{
		Entries?.Clear();
	}

	/// <summary>
	/// Immediately snaps all transitions to the end point, at which point they're removed.
	/// </summary>
	internal void Kill()
	{
		if ( Entries == null ) return;

		for ( int i = 0; i < Entries.Count; i++ )
		{
			var transition = Entries[i];
			transition.Kill();
			Entries[i] = transition;
		}
	}

	internal void Add( Styles from, Styles to, double startTime )
	{
		if ( !to.HasTransitions ) return;

		foreach ( var desc in to.Transitions.List )
		{
			var fromCopy = (Styles)from.Clone();
			var toCopy = (Styles)to.Clone();

			TransitionFunction action = (desc.Property == "all")
				? ( style, delta ) => style.FromLerp( fromCopy, toCopy, delta )
				: ( style, delta ) => style.LerpProperty( desc.Property, fromCopy, toCopy, delta );

			Transition( desc, from, to, action, startTime );
		}
	}

	void Transition( in TransitionDesc desc, BaseStyles from, BaseStyles to, TransitionFunction action, double startTime )
	{
		if ( from == to ) return;

		var target = HashCode.Combine( to?.GetHashCode(), desc.Property );
		if ( TryRestoreTransition( target ) ) return;

		Add( desc, target, action, startTime );
	}

	void Add( in TransitionDesc desc, int target, TransitionFunction action, double startTime )
	{
		var length = 1.0f;
		var property = desc.Property;

		if ( desc.Duration.HasValue )
			length = desc.Duration.Value / 1000.0f;

		if ( length <= 0 && !desc.Delay.HasValue )
			return;

		if ( desc.Delay.HasValue )
		{
			startTime += desc.Delay.Value / 1000.0f;
		}

		Entries ??= new List<Entry>();
		var easingFunction = Easing.GetFunction( desc.TimingFunction );

		var entry = new Entry( property, startTime, length, target, action, easingFunction );
		Entries.Add( entry );
	}

	internal bool Run( Styles style, double now )
	{
		if ( !HasAny )
			return false;

		if ( Entries.RemoveAll( x => x.StartTime + x.Length < now ) > 0 )
		{
			panel.SetNeedsPreLayout();
		}

		foreach ( var entry in Entries )
		{
			//
			// Pre-start (delay)
			//
			if ( now < entry.StartTime )
			{
				entry.Invoke( style, entry.Ease( 0 ) );
				continue;
			}

			var endTime = entry.StartTime + entry.Length;
			if ( now > endTime ) continue;

			var t = entry.IsKilled ? 1f : (now - entry.StartTime) / entry.Length;

			entry.Invoke( style, entry.Ease( (float)t ) );
		}

		if ( Entries.RemoveAll( x => x.IsKilled ) > 0 )
		{
			panel.SetNeedsPreLayout();
		}

		return true;
	}

	internal bool TryRestoreTransition( int targetValue )
	{
		if ( Entries == null ) return false;

		for ( int i = Entries.Count - 1; i >= 0; i-- )
		{
			if ( Entries[i].Target != targetValue )
				continue;

			var entry = Entries[i];
			entry.Restore();
			Entries[i] = entry;
			return true;
		}

		return false;
	}

}
