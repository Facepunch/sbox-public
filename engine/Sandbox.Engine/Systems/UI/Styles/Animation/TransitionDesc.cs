
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

				var sub = p.ReadUntilOrEnd( "," );
				p.Pointer++;

				var transition = Parse( sub );
				list.Add( transition );
			}

			return list;
		}

		Log.Warning( $"Didn't handle transition style: {property}" );
		return null;
	}

	static TransitionDesc Parse( string value )
	{
		var p = new Parse( value );

		var t = new TransitionDesc();
		t.Delay = 0;
		t.TimingFunction = "ease"; // default is ease

		p = p.SkipWhitespaceAndNewlines();
		t.Property = p.ReadWord( null, true ).ToLower();
		t.Property = StyleParser.GetPropertyFromAlias( t.Property );
		if ( p.IsEnd ) return t;
		p = p.SkipWhitespaceAndNewlines();
		if ( p.IsEnd ) return t;

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

		t.TimingFunction = p.ReadWord( null, true );

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
