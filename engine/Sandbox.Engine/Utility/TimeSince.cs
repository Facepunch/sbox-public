using System;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// A convenience struct to easily measure time since an event last happened, based on <see cref="Time.Now"/>.<br/>
/// <br/>
/// Typical usage would see you assigning 0 to a variable of this type to reset the timer.
/// Then the struct would return time since the last reset. i.e.:
/// <code>
/// TimeSince lastUsed = 0;
/// if ( lastUsed > 10 ) { /*Do something*/ }
/// </code>
/// </summary>
[Expose]
[StructLayout( LayoutKind.Sequential )]
public struct TimeSince : IEquatable<TimeSince>
{
	double time;

	public static explicit operator double( TimeSince ts ) => Time.NowDouble - ts.time;
	public static implicit operator float( TimeSince ts ) => (float)(Time.NowDouble - ts.time);
	public static implicit operator TimeSince( double ts ) => new() { time = Time.NowDouble - ts };
	public static implicit operator TimeSince( float ts ) => new() { time = Time.NowDouble - ts };
	public static bool operator <( in TimeSince ts, float f ) => ts.Relative < f;
	public static bool operator >( in TimeSince ts, float f ) => ts.Relative > f;
	public static bool operator <=( in TimeSince ts, float f ) => ts.Relative <= f;
	public static bool operator >=( in TimeSince ts, float f ) => ts.Relative >= f;
	public static bool operator <( in TimeSince ts, int f ) => ts.Relative < f;
	public static bool operator >( in TimeSince ts, int f ) => ts.Relative > f;
	public static bool operator <=( in TimeSince ts, int f ) => ts.Relative <= f;
	public static bool operator >=( in TimeSince ts, int f ) => ts.Relative >= f;

	/// <summary>
	/// Time at which the timer reset happened, based on <see cref="Time.Now"/>.
	/// </summary>
	public float Absolute => (float)time;

	/// <summary>
	/// Time passed since last reset, in seconds.
	/// </summary>
	public float Relative => this;

	public override string ToString() => $"{Relative}";

	#region equality
	public static bool operator ==( TimeSince left, TimeSince right ) => left.Equals( right );
	public static bool operator !=( TimeSince left, TimeSince right ) => !(left == right);
	public override bool Equals( object obj ) => obj is TimeSince o && Equals( o );
	public readonly bool Equals( TimeSince o ) => this.time == o.time;
	public readonly override int GetHashCode() => time.GetHashCode();
	#endregion
}

/// <summary>
/// A convenience struct to easily manage a time countdown, based on <see cref="Time.Now"/>.<br/>
/// <br/>
/// Typical usage would see you assigning to a variable of this type a necessary amount of seconds.
/// Then the struct would return the time countdown, or can be used as a bool i.e.:
/// <code>
/// TimeUntil nextAttack = 10;
/// if ( nextAttack ) { /*Do something*/ }
/// </code>
/// </summary>
[Expose]
[StructLayout( LayoutKind.Sequential )]
public struct TimeUntil : IEquatable<TimeUntil>
{
	private double time;
	private double startTime;

	public static implicit operator bool( TimeUntil ts ) => Time.NowDouble >= ts.time;
	public static explicit operator double( TimeUntil ts ) => ts.time - Time.NowDouble;
	public static implicit operator float( TimeUntil ts ) => (float)(ts.time - Time.NowDouble);
	public static bool operator <( in TimeUntil ts, float f ) => ts.Relative < f;
	public static bool operator >( in TimeUntil ts, float f ) => ts.Relative > f;
	public static bool operator <=( in TimeUntil ts, float f ) => ts.Relative <= f;
	public static bool operator >=( in TimeUntil ts, float f ) => ts.Relative >= f;
	public static bool operator <( in TimeUntil ts, int f ) => ts.Relative < f;
	public static bool operator >( in TimeUntil ts, int f ) => ts.Relative > f;
	public static bool operator <=( in TimeUntil ts, int f ) => ts.Relative <= f;
	public static bool operator >=( in TimeUntil ts, int f ) => ts.Relative >= f;
	public static implicit operator TimeUntil( double ts ) => new() { time = Time.NowDouble + ts, startTime = Time.NowDouble };
	public static implicit operator TimeUntil( float ts ) => new() { time = Time.NowDouble + ts, startTime = Time.NowDouble };

	/// <summary>
	/// Time to which we are counting down to, based on <see cref="Time.Now"/>.
	/// </summary>
	public float Absolute => (float)time;

	/// <summary>
	/// The actual countdown, in seconds.
	/// </summary>
	public float Relative => this;

	/// <summary>
	/// Amount of seconds passed since the countdown started.
	/// </summary>
	public float Passed => (float)(Time.NowDouble - startTime);

	/// <summary>
	/// The countdown, but as a fraction, i.e. a value from 0 (start of countdown) to 1 (end of countdown)
	/// </summary>
	public float Fraction => (float)Math.Clamp( (Time.NowDouble - startTime) / (time - startTime), 0.0f, 1.0f );

	public override string ToString() => $"{Relative}";

	#region equality
	public static bool operator ==( TimeUntil left, TimeUntil right ) => left.Equals( right );
	public static bool operator !=( TimeUntil left, TimeUntil right ) => !(left == right);
	public readonly override bool Equals( object obj ) => obj is TimeUntil o && Equals( o );
	public readonly bool Equals( TimeUntil o ) => time == o.time;
	public readonly override int GetHashCode() => HashCode.Combine( time );
	#endregion
}
