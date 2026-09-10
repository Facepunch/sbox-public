namespace Sandbox.Utility;

/// <summary>
/// Easing functions used for transitions. See <a href="https://easings.net/">https://easings.net/</a> for examples.
/// </summary>
public static class Easing
{
	/// <summary>
	/// An easing function that transforms the linear input into non linear output.
	/// </summary>
	/// <param name="delta">A linear input value from 0 to 1</param>
	/// <returns>The resulting non linear output value, from 0 to 1</returns>
	public delegate float Function( float delta );

	private static readonly Dictionary<string, Function> _functions = new Dictionary<string, Function>
	{
		{ "linear",         Linear },

		{ "ease",           QuadraticInOut },
		{ "ease-in-out",    ExpoInOut },
		{ "ease-out",       QuadraticOut },
		{ "ease-in",        QuadraticIn },

		{ "bounce-in",      BounceIn },
		{ "bounce-out",     BounceOut },
		{ "bounce-in-out",  BounceInOut },

		{ "sin-ease-in",     SineEaseIn },
		{ "sin-ease-out",    SineEaseOut },
		{ "sin-ease-in-out", SineEaseInOut },

		{ "step-start",     StepStart },
		{ "step-end",       StepEnd },
	};

	/// <inheritdoc cref="ExpoInOut"/>
	public static float EaseInOut( float f ) => ExpoInOut( f );
	/// <inheritdoc cref="QuadraticIn"/>
	public static float EaseIn( float f ) => QuadraticIn( f );
	/// <inheritdoc cref="QuadraticOut"/>
	public static float EaseOut( float f ) => QuadraticOut( f );

	/// <summary>
	/// Linear easing function, x=y.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float Linear( float f ) => f;

	/// <summary>
	/// Quadratic ease in.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float QuadraticIn( float f ) => f * f;

	/// <summary>
	/// Quadratic ease out.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float QuadraticOut( float f ) => f * (2.0f - f);

	/// <summary>
	/// Quadratic ease in and out.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float QuadraticInOut( float f ) => (f *= 2.0f) < 1.0f ? 0.5f * f * f : -0.5f * ((f -= 1f) * (f - 2f) - 1f);


	/// <summary>
	/// Exponential ease in.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float ExpoIn( float f ) => f == 0f ? 0f : MathF.Pow( 1024f, f - 1f );

	/// <summary>
	/// Exponential ease out.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float ExpoOut( float f ) => f == 1f ? 1f : 1f - MathF.Pow( 2f, -10f * f );

	/// <summary>
	/// Exponential ease in and out.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float ExpoInOut( float f ) => f < 0.5 ? ExpoIn( f * 2.0f ) * 0.5f : ExpoOut( (f - 0.5f) * 2.0f ) * 0.5f + 0.5f;


	/// <summary>
	/// Bouncy ease in.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float BounceIn( float f ) => 1f - BounceOut( 1f - f );

	/// <summary>
	/// Bouncy ease out.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float BounceOut( float f ) => f < (1f / 2.75f) ? 7.5625f * f * f : f < (2f / 2.75f) ? 7.5625f * (f -= (1.5f / 2.75f)) * f + 0.75f : f < (2.5f / 2.75f) ? 7.5625f * (f -= (2.25f / 2.75f)) * f + 0.9375f : 7.5625f * (f -= (2.625f / 2.75f)) * f + 0.984375f;

	/// <summary>
	/// Bouncy ease in and out.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float BounceInOut( float f ) => f < 0.5 ? BounceIn( f * 2.0f ) * 0.5f : BounceOut( (f - 0.5f) * 2.0f ) * 0.5f + 0.5f;


	/// <summary>
	/// Sine ease in.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float SineEaseIn( float f ) => 1.0f - MathF.Cos( (f * MathF.PI) * 0.5f );

	/// <summary>
	/// Sine ease out.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float SineEaseOut( float f ) => MathF.Sin( (f * MathF.PI) * 0.5f );

	/// <summary>
	/// Sine ease in and out.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float SineEaseInOut( float f ) => -(MathF.Cos( MathF.PI * f ) - 1.0f) * 0.5f;


	/// <summary>
	/// Jumps straight to 1 the moment the animation starts.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float StepStart( float f ) => f > 0 ? 1 : 0;

	/// <summary>
	/// Stays at 0 until the animation ends, then jumps to 1.
	/// </summary>
	/// <param name="f">Input in range of 0 to 1.</param>
	/// <returns>Output in range 0 to 1.</returns>
	public static float StepEnd( float f ) => f < 1 ? 0 : 1;

	/// <summary>
	/// Stepped easing with <paramref name="count"/> equal-sized jumps.
	/// When <paramref name="atStart"/> is true the jump happens at the start of each step,
	/// otherwise it happens at the end. Matches the CSS <c>steps(n, start|end)</c> timing function.
	/// </summary>
	public static Function Steps( int count, bool atStart = false )
	{
		if ( count < 1 ) count = 1;

		return t =>
		{
			if ( t <= 0 ) return 0;
			if ( t >= 1 ) return 1;

			return atStart
				? MathF.Ceiling( t * count ) / count
				: MathF.Floor( t * count ) / count;
		};
	}

	/// <summary>
	/// Cubic bezier easing with control points (x1,y1) and (x2,y2). The curve runs from (0,0) to (1,1).
	/// Matches the CSS <c>cubic-bezier(x1, y1, x2, y2)</c> timing function.
	/// </summary>
	public static Function CubicBezier( float x1, float y1, float x2, float y2 )
	{
		// One axis of B(t) with endpoints 0 and 1
		static float Sample( float t, float p1, float p2 )
		{
			float u = 1f - t;
			return 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t;
		}

		return t =>
		{
			if ( t <= 0 ) return 0;
			if ( t >= 1 ) return 1;

			// Newton-Raphson to find s where X(s) = t, then sample Y at s
			float s = t;
			for ( int i = 0; i < 8; i++ )
			{
				float x = Sample( s, x1, x2 ) - t;
				if ( MathF.Abs( x ) < 1e-5f ) break;

				float u = 1f - s;
				float dx = 3f * u * u * x1 + 6f * u * s * (x2 - x1) + 3f * s * s * (1f - x2);
				if ( MathF.Abs( dx ) < 1e-6f ) break;

				s -= x / dx;
			}

			return Sample( s, y1, y2 );
		};
	}


	/// <summary>
	/// Add an easing function.
	/// If the function already exists we silently return.
	/// </summary>
	internal static void AddFunction( string name, Function func )
	{
		if ( _functions.ContainsKey( name ) )
			return;

		_functions[name] = func;
	}

	/// <summary>
	/// Get an easing function by name (ie, "ease-in", "cubic-bezier(0.1, 0.7, 1, 0.1)" or "steps(4, end)").
	/// If the function doesn't exist we return QuadraticInOut
	/// </summary>
	public static Function GetFunction( string name )
	{
		if ( name != null && _functions.TryGetValue( name, out var f ) )
			return f;

		if ( TryParseCubicBezier( name, out var bezier ) ) return bezier;
		if ( TryParseSteps( name, out var steps ) ) return steps;

		return QuadraticInOut;
	}

	/// <summary>
	/// Get an easing function by name (ie, "ease-in", "cubic-bezier(0.1, 0.7, 1, 0.1)" or "steps(4, end)").
	/// If the function exists we return true, otherwise return false.
	/// </summary>
	public static bool TryGetFunction( string name, out Function function )
	{
		if ( name != null && _functions.TryGetValue( name, out function ) )
			return true;

		if ( TryParseCubicBezier( name, out function ) ) return true;
		if ( TryParseSteps( name, out function ) ) return true;

		return false;
	}

	static bool TryParseSteps( string s, out Function function )
	{
		function = null;
		if ( string.IsNullOrEmpty( s ) ) return false;

		var p = new Parse( s );
		p = p.SkipWhitespaceAndNewlines();
		if ( !p.TrySkip( "steps" ) ) return false;
		p = p.SkipWhitespaceAndNewlines();
		if ( !p.Is( '(' ) ) return false;

		var inner = new Parse( p.ReadInnerBrackets() ?? string.Empty );

		if ( !inner.TryReadFloat( out var countFloat ) ) return false;
		int count = (int)countFloat;
		if ( count < 1 ) return false;

		bool atStart = false;
		if ( inner.TrySkipCommaSeparation() )
		{
			var word = inner.ReadWord( null, true );
			if ( word == "start" || word == "jump-start" ) atStart = true;
			else if ( word == "end" || word == "jump-end" ) atStart = false;
			else return false;
		}

		function = Steps( count, atStart );
		return true;
	}

	static bool TryParseCubicBezier( string s, out Function function )
	{
		function = null;
		if ( string.IsNullOrEmpty( s ) ) return false;

		var p = new Parse( s );
		p = p.SkipWhitespaceAndNewlines();
		if ( !p.TrySkip( "cubic-bezier" ) ) return false;
		p = p.SkipWhitespaceAndNewlines();
		if ( !p.Is( '(' ) ) return false;

		var inner = new Parse( p.ReadInnerBrackets() ?? string.Empty );

		if ( !inner.TryReadFloat( out var x1 ) ) return false;
		if ( !inner.TrySkipCommaSeparation() ) return false;
		if ( !inner.TryReadFloat( out var y1 ) ) return false;
		if ( !inner.TrySkipCommaSeparation() ) return false;
		if ( !inner.TryReadFloat( out var x2 ) ) return false;
		if ( !inner.TrySkipCommaSeparation() ) return false;
		if ( !inner.TryReadFloat( out var y2 ) ) return false;

		function = CubicBezier( x1, y1, x2, y2 );
		return true;
	}
}
