namespace Sandbox
{
	/// <summary>
	/// A convenience struct to easily measure time since an event last happened, based on <see cref="RealTime.GlobalNow"/>.<br/>
	/// <br/>
	/// Typical usage would see you assigning 0 to a variable of this type to reset the timer.
	/// Then the struct would return time since the last reset. i.e.:
	/// <code>
	/// RealTimeSince lastUsed = 0;
	/// if ( lastUsed > 10 ) { /*Do something*/ }
	/// </code>
	/// </summary>
	public struct RealTimeSince : IEquatable<RealTimeSince>
	{
		double time;

		public static implicit operator float( RealTimeSince ts ) => (float)(RealTime.GlobalNow - ts.time);
		public static explicit operator double( RealTimeSince ts ) => RealTime.GlobalNow - ts.time;
		public static implicit operator RealTimeSince( double ts ) => new() { time = RealTime.GlobalNow - ts };
		public static implicit operator RealTimeSince( float ts ) => new() { time = RealTime.GlobalNow - ts };
		public static bool operator <( in RealTimeSince ts, float f ) => ts.Relative < f;
		public static bool operator >( in RealTimeSince ts, float f ) => ts.Relative > f;
		public static bool operator <=( in RealTimeSince ts, float f ) => ts.Relative <= f;
		public static bool operator >=( in RealTimeSince ts, float f ) => ts.Relative >= f;
		public static bool operator <( in RealTimeSince ts, int f ) => ts.Relative < f;
		public static bool operator >( in RealTimeSince ts, int f ) => ts.Relative > f;
		public static bool operator <=( in RealTimeSince ts, int f ) => ts.Relative <= f;
		public static bool operator >=( in RealTimeSince ts, int f ) => ts.Relative >= f;

		/// <summary>
		/// Time at which the timer reset happened, based on <see cref="RealTime.GlobalNow"/>.
		/// </summary>
		public double Absolute => time;

		/// <summary>
		/// Time passed since last reset, in seconds.
		/// </summary>
		public float Relative => this;

		public override string ToString() => $"{Relative}";

		#region equality
		public static bool operator ==( RealTimeSince left, RealTimeSince right ) => left.Equals( right );
		public static bool operator !=( RealTimeSince left, RealTimeSince right ) => !(left == right);
		public override bool Equals( object obj ) => obj is RealTimeSince o && Equals( o );
		public bool Equals( RealTimeSince o ) => time == o.time;
		public readonly override int GetHashCode() => HashCode.Combine( time );
		#endregion
	}


	/// <summary>
	/// A convenience struct to easily manage a time countdown, based on <see cref="RealTime.GlobalNow"/>.<br/>
	/// <br/>
	/// Typical usage would see you assigning to a variable of this type a necessary amount of seconds.
	/// Then the struct would return the time countdown, or can be used as a bool i.e.:
	/// <code>
	/// RealTimeUntil nextAttack = 10;
	/// if ( nextAttack ) { /*Do something*/ }
	/// </code>
	/// </summary>
	public struct RealTimeUntil : IEquatable<RealTimeUntil>
	{
		double time;
		double startTime;

		public static implicit operator bool( RealTimeUntil ts ) => RealTime.GlobalNow >= ts.time;
		public static implicit operator float( RealTimeUntil ts ) => (float)(ts.time - RealTime.GlobalNow);
		public static explicit operator double( RealTimeUntil ts ) => ts.time - RealTime.GlobalNow;
		public static implicit operator RealTimeUntil( double ts ) => new() { time = RealTime.GlobalNow + ts, startTime = RealTime.GlobalNow };
		public static implicit operator RealTimeUntil( float ts ) => new() { time = RealTime.GlobalNow + ts, startTime = RealTime.GlobalNow };
		public static bool operator <( in RealTimeUntil ts, float f ) => ts.Relative < f;
		public static bool operator >( in RealTimeUntil ts, float f ) => ts.Relative > f;
		public static bool operator <=( in RealTimeUntil ts, float f ) => ts.Relative <= f;
		public static bool operator >=( in RealTimeUntil ts, float f ) => ts.Relative >= f;
		public static bool operator <( in RealTimeUntil ts, int f ) => ts.Relative < f;
		public static bool operator >( in RealTimeUntil ts, int f ) => ts.Relative > f;
		public static bool operator <=( in RealTimeUntil ts, int f ) => ts.Relative <= f;
		public static bool operator >=( in RealTimeUntil ts, int f ) => ts.Relative >= f;

		/// <summary>
		/// Time to which we are counting down to, based on <see cref="RealTime.GlobalNow"/>.
		/// </summary>
		public double Absolute => time;

		/// <summary>
		/// The actual countdown, in seconds.
		/// </summary>
		public double Relative => this;

		/// <summary>
		/// Amount of seconds passed since the countdown started.
		/// </summary>
		public double Passed => (RealTime.GlobalNow - startTime);

		/// <summary>
		/// The countdown, but as a fraction, i.e. a value from 0 (start of countdown) to 1 (end of countdown)
		/// </summary>
		public double Fraction => Math.Clamp( (RealTime.GlobalNow - startTime) / (time - startTime), 0.0f, 1.0f );

		public override string ToString() => $"{Relative}";

		#region equality
		public static bool operator ==( RealTimeUntil left, RealTimeUntil right ) => left.Equals( right );
		public static bool operator !=( RealTimeUntil left, RealTimeUntil right ) => !(left == right);
		public readonly override bool Equals( object obj ) => obj is RealTimeUntil o && Equals( o );
		public readonly bool Equals( RealTimeUntil o ) => time == o.time;
		public readonly override int GetHashCode() => HashCode.Combine( time );
		#endregion
	}
}
