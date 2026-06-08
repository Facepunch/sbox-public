
namespace Sandbox.UI;

/// <summary>
/// Describes transition of a single CSS property, a.k.a. the values of a <c>transition</c> CSS property.
/// <para>Utility to create a transition by comparing the
/// panel style before and after the scope.</para>
/// </summary>
public struct TransitionDesc
{
	/// <summary>
	/// The CSS property to transition.
	/// </summary>
	public string Property;

	/// <summary>
	/// Duration of the transition between old value and new value.
	/// </summary>
	public float? Duration;

	/// <summary>
	/// If set, delay before starting the transition after the property was changed.
	/// </summary>
	public float? Delay;

	/// <summary>
	/// The timing or "easing" function. <c>transition-timing-function</c> CSS property.
	/// Example values would be <c>ease</c>,  <c>ease-in</c>,  <c>ease-out</c> and  <c>ease-in-out</c>.
	/// </summary>
	public string TimingFunction;

	internal static TransitionList ParseProperty( string property, string value, TransitionList list )
	{
		var p = new Parse( value );

		if ( list == null )
			list = new TransitionList();

		if ( property == "transition" )
		{
			while ( !p.IsEnd )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) break;

				var sub = p.ReadUntilOrEnd( ",", true, true );
				if ( !p.IsEnd ) p.Pointer++;

				if ( string.IsNullOrWhiteSpace( sub ) ) continue;

				var transition = Parse( sub );
				list.Add( transition );
			}

			return list;
		}

		// Longhand properties (transition-duration/-delay/-property/-timing-function). Each is a
		// comma-separated list whose i-th value updates the i-th transition entry, creating entries
		// with sensible defaults as needed.
		var items = value.Split( ',' );
		for ( int i = 0; i < items.Length; i++ )
		{
			var item = items[i].Trim();
			if ( string.IsNullOrEmpty( item ) ) continue;

			while ( list.List.Count <= i )
				list.List.Add( new TransitionDesc { Property = "all", TimingFunction = "ease", Delay = 0, Duration = 0 } );

			var t = list.List[i];

			switch ( property )
			{
				case "transition-property":
					t.Property = StyleParser.GetPropertyFromAlias( item.ToLower() );
					break;

				case "transition-duration":
					{
						var pp = new Parse( item );
						if ( pp.TryReadTime( out var d ) ) t.Duration = d;
					}
					break;

				case "transition-delay":
					{
						var pp = new Parse( item );
						if ( pp.TryReadTime( out var d ) ) t.Delay = d;
					}
					break;

				case "transition-timing-function":
					t.TimingFunction = item;
					break;

				default:
					Log.Warning( $"Didn't handle transition style: {property}" );
					return null;
			}

			list.List[i] = t;
		}

		return list;
	}

	static TransitionDesc Parse( string value )
	{
		var p = new Parse( value );

		var t = new TransitionDesc();
		t.Delay = 0;
		t.TimingFunction = "ease"; // default is ease

		p = p.SkipWhitespaceAndNewlines();

		// The property is optional and defaults to 'all' (eg "transition: 0.3s ease"). If the first
		// token reads as a time then there's no property - don't consume it as one.
		var probe = p;
		if ( probe.TryReadTime( out _ ) )
		{
			t.Property = "all";
		}
		else
		{
			t.Property = p.ReadWord( null, true ).ToLower();
			t.Property = StyleParser.GetPropertyFromAlias( t.Property );
			if ( p.IsEnd ) return t;
			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return t;
		}

		//
		// Duration is mandatory
		//
		if ( !p.TryReadTime( out var duration ) )
			throw new System.Exception( "Expecting time in transition" );

		t.Duration = duration;

		if ( p.IsEnd ) return t;
		p = p.SkipWhitespaceAndNewlines();
		if ( p.IsEnd ) return t;

		//
		// Try to read the delay now, since it could be here
		//
		if ( p.TryReadTime( out var delay ) )
		{
			t.Delay = delay;
		}

		if ( p.IsEnd ) return t;
		p = p.SkipWhitespaceAndNewlines();
		if ( p.IsEnd ) return t;

		t.TimingFunction = p.ReadWord( null, true, true );

		if ( p.IsEnd ) return t;
		p = p.SkipWhitespaceAndNewlines();
		if ( p.IsEnd ) return t;

		if ( p.TryReadTime( out delay ) )
		{
			t.Delay = delay;
		}

		return t;
	}
}
