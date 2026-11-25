using System.Collections.Immutable;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Describes a curve, which can have multiple key frames.
/// </summary>
[JsonConverter( typeof( Curve.JsonConverter ) )]
public unsafe struct Curve
{
	/// <summary>
	/// The range of this curve. This affects looping.
	/// </summary>
	[JsonPropertyName( "x" )]
	public Vector2 TimeRange { readonly get; set; }

	/// <summary>
	/// The value range. This should affect nothing but what it looks like in the editor.
	/// </summary>
	[JsonPropertyName( "y" )]
	public Vector2 ValueRange { readonly get; set; }

	/// <summary>
	/// A curve that linearly interpolates from 0 to 1
	/// </summary>
	public static readonly Curve Linear = new Curve( new List<Frame>() { new Frame( 0, 0, -1, 1 ), new Frame( 1, 1, -1, 1 ) } );

	/// <summary>
	/// A curve that eases from 0 to 1
	/// </summary>
	public static readonly Curve Ease = new Curve( new List<Frame>() { new Frame( 0, 0 ), new Frame( 1, 1 ) } );

	/// <summary>
	/// A curve that eases in from 0 to 1
	/// </summary>
	public static readonly Curve EaseIn = new Curve( new List<Frame>() { new Frame( 0, 0, 0, 0 ), new Frame( 1, 1, -MathF.PI, MathF.PI ) } );

	/// <summary>
	/// A curve that eases out from 0 to 1
	/// </summary>
	public static readonly Curve EaseOut = new Curve( new List<Frame>() { new Frame( 0, 0, -MathF.PI, MathF.PI ), new Frame( 1, 1, 0, 0 ) } );

	public Curve( ImmutableArray<Frame> frames )
	{
		TimeRange = new Vector2( 0, 1 );
		ValueRange = new Vector2( 0, 1 );
		this.Frames = frames;
	}

	public Curve( IEnumerable<Frame> frames ) : this( frames.ToImmutableArray() ) { }

	public Curve( params Frame[] frames ) : this( frames.ToImmutableArray() ) { }

	public Curve() : this( ImmutableArray<Frame>.Empty ) { }

	/// <summary>
	/// A single float creates a flat curve
	/// </summary>
	static public implicit operator Curve( float value )
	{
		var c = new Curve();
		c.AddPoint( c.TimeRange.x.LerpTo( c.TimeRange.y, 0.5f ), value );
		return c;
	}

	/// <summary>
	/// Make a copy of this curve with changed keyframes
	/// </summary>
	public readonly Curve WithFrames( ImmutableList<Frame> frames )
	{
		var c = this;
		c.Frames = frames.ToImmutableArray();
		return c;
	}

	/// <summary>
	/// Make a copy of this curve with changed keyframes
	/// </summary>
	public readonly Curve WithFrames( ImmutableArray<Frame> frames )
	{
		var c = this;
		c.Frames = frames;
		return c;
	}

	/// <summary>
	/// Make a copy of this curve with changed keyframes
	/// </summary>
	public readonly Curve WithFrames( IEnumerable<Frame> frames )
	{
		var c = this;
		c.Frames = frames.ToImmutableArray();
		return c;
	}

	/// <summary>
	/// Make a copy of this curve that is reversed (If input eases from 0 to 1 then output will ease from 1 to 0)
	/// </summary>
	public readonly Curve Reverse()
	{
		var c = this;
		var frames = new List<Frame>();
		foreach ( var frame in Frames.Reverse() )
		{
			var frameTime = 1f - frame.Time;
			var frameVal = frame.Value;
			var frameIn = -frame.In;
			var frameOut = -frame.Out;
			frames.Add( new Frame( frameTime, frameVal, frameIn, frameOut ) );
		}
		c.Frames = frames.ToImmutableArray();
		return c;
	}

	/// <summary>
	/// Keyframes times and values should range between 0 and 1
	/// </summary>
	public struct Frame : IComparable<Frame>
	{
		/// <summary>
		/// The delta position on the time line (0-1)
		/// </summary>
		[JsonPropertyName( "x" )]
		public float Time { readonly get; set; }

		/// <summary>
		/// The delta position on the value line (0-1)
		/// </summary>
		[JsonPropertyName( "y" )]
		public float Value { readonly get; set; }

		/// <summary>
		/// This is the slope of entry, formula is something like tan( angle )
		/// </summary>
		[JsonPropertyName( "in" )]
		public float In { readonly get; set; }

		/// <summary>
		/// This is the slope of exit, formula is something like tan( angle )
		/// </summary>
		[JsonPropertyName( "out" )]
		public float Out { readonly get; set; }

		/// <summary>
		/// How the line should behave when entering/leaving this frame
		/// </summary>
		[JsonPropertyName( "mode" )]
		public HandleMode Mode { readonly get; set; }

		public Frame( float timedelta, float valuedelta )
		{
			Time = timedelta;
			Value = valuedelta;
			In = default;
			Out = default;
			Mode = HandleMode.Mirrored;
		}

		public Frame( float timedelta, float valuedelta, float inTangent, float outTangent )
		{
			Time = timedelta;
			Value = valuedelta;
			In = inTangent;
			Out = outTangent;
			Mode = HandleMode.Mirrored;
		}

		public readonly Frame WithTime( float time )
		{
			var f = this;
			f.Time = time;
			return f;
		}

		public readonly Frame WithValue( float value )
		{
			var f = this;
			f.Value = value;
			return f;
		}

		public int CompareTo( Frame other )
		{
			return Time.CompareTo( other.Time );
		}
	}

	/// <summary>
	/// Describes how the line should behave when entering/leaving a frame
	/// </summary>
	public enum HandleMode
	{
		/// <summary>
		/// The In and Out are user set, but are joined (mirrored)
		/// </summary>
		[Icon( "open_in_full" )]
		[Description( "Can change the angle but tangents are mirrored" )]
		Mirrored,

		/// <summary>
		/// The In and Out are user set and operate independently
		/// </summary>
		[Icon( "call_split" )]
		[Description( "Can change in and out tangents independantly" )]
		Split,

		/// <summary>
		/// Curves are generated automatically
		/// </summary>
		[Icon( "horizontal_rule" )]
		[Description( "Tangents are locked to 0" )]
		Flat,

		/// <summary>
		/// No curves, linear interpolation from this handle to the next
		/// </summary>
		[Icon( "show_chart" )]
		[Description( "No tangents - interpolate linearly from this point to the next" )]
		Linear,

		/// <summary>
		/// No interpolation use raw values
		/// </summary>
		[Icon( "turn_sharp_right" )]
		[Description( "No tangents or interpolation, use this handle's value until we reach the next point" )]
		Stepped,


	}

	/// <summary>
	/// A list of keyframes or points on the curve.
	/// </summary>
	public ImmutableArray<Frame> Frames;

	/// <summary>
	/// Amount of key frames or points on the curve.
	/// </summary>

	public readonly int Length
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		get => Frames.IsDefaultOrEmpty ? 0 : Frames.Length;
	}

	public Frame this[int index]
	{
		readonly get => Frames[index];
		set
		{
			Frames = Frames.SetItem( index, value );
		}
	}

	/// <summary>
	/// Add a new keyframe at given position to this curve.
	/// </summary>
	/// <param name="x">Position of the keyframe on the X axis.</param>
	/// <param name="y">Position of the keyframe on the Y axis.</param>
	/// <returns>The position of newly added keyframe in the <see cref="Frames"/> list.</returns>
	public int AddPoint( float x, float y ) => AddPoint( new Frame( x, y ) );

	/// <summary>
	/// Add given keyframe to this curve.
	/// </summary>
	/// <param name="keyframe">The keyframe to add.</param>
	/// <returns>The position of newly added keyframe in the <see cref="Frames"/> list.</returns>
	public int AddPoint( in Frame keyframe )
	{
		if ( Frames.IsDefaultOrEmpty ) Frames = ImmutableArray<Frame>.Empty;

		Frames = Frames.Add( keyframe );
		return Length - 1;
	}

	/// <summary>
	/// Remove all of the frames at the current time
	/// </summary>
	public void RemoveAtTime( float time, float within )
	{
		Frames = Frames.RemoveAll( x => MathF.Abs( x.Time - time ) <= within );
	}

	/// <summary>
	/// Make sure we're all sorted by time
	/// </summary>
	public void Sort()
	{
		Frames = Frames.Order().ToImmutableArray();
	}

	/// <summary>
	/// Add given keyframe to this curve.
	/// </summary>
	/// <returns>True if we added a new point. False if we just edited an existing point.</returns>
	public bool AddOrReplacePoint( in Frame keyframe )
	{
		if ( Frames.IsDefaultOrEmpty ) Frames = ImmutableArray<Frame>.Empty;

		for ( int i = 0; i < Frames.Length; i++ )
		{
			if ( Frames[i].Time == keyframe.Time )
			{
				Frames = Frames.RemoveAt( i );
				Frames = Frames.Insert( i, keyframe );
				return false;
			}
		}

		RemoveAtTime( keyframe.Time, 0.0001f );
		Frames = Frames.Add( keyframe );
		Sort();

		return true;
	}

	/// <summary>
	/// Returns the value on the curve at given time position.
	/// </summary>
	/// <param name="time">The time point (x axis) at which </param>
	/// <param name="angles">Is this an angle?</param>
	/// <returns>The absolute value at given time. (y axis)</returns>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float Evaluate( float time, bool angles )
	{
		// convert to normalized
		time = time.LerpInverse( TimeRange.x, TimeRange.y, false );

		// can add ping pong, clamping, looping here on time - which is now 0-1

		var delta = EvaluateDelta( time, angles );

		return delta.Remap( 0, 1, ValueRange.x, ValueRange.y, false );
	}

	/// <summary>
	/// Returns the value on the curve at given time position.
	/// </summary>
	/// <param name="time">The time point (x axis) at which </param>
	/// <returns>The absolute value at given time. (y axis)</returns>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float Evaluate( float time ) => Evaluate( time, false );

	/// <summary>
	/// Like evaluate but takes a normalized time between 0 and 1 and returns a normalized value between 0 and 1
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float EvaluateDelta( float time ) => EvaluateDelta( time, false );

	/// <summary>
	/// Like evaluate but takes a normalized time between 0 and 1 and returns a normalized value between 0 and 1
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float EvaluateDelta( float time, bool angles )
	{
		if ( Length == 0 ) return 0;
		if ( Length == 1 ) return Frames[0].Value;

		// Search for frame at exact time first
		int baseIndex = Frames.BinarySearch( new() { Time = time }, null );
		if ( baseIndex >= 0 )
		{
			return Frames[baseIndex].Value;
		}

		// If the index is before or after the curve range, return the first or last value
		baseIndex = ~baseIndex;
		if ( baseIndex == 0 )
		{
			return Frames[0].Value;
		}
		if ( baseIndex >= Frames.Length )
		{
			return Frames[^1].Value;
		}

		return GetInterpolatedValue( Frames[baseIndex - 1], Frames[baseIndex], time );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static float GetInterpolatedValue( in Frame frameA, in Frame frameB, float time )
	{
		switch ( frameA.Mode )
		{
			case HandleMode.Stepped:
				{
					return frameA.Value;
				}
			case HandleMode.Linear:
				{
					float lerpTime = (time - frameA.Time) / (frameB.Time - frameA.Time);
					return frameA.Value + (frameB.Value - frameA.Value) * lerpTime;
				}
			default:
				{
					float p0 = frameA.Value;
					float p1 = frameB.Value;
					float t = (time - frameA.Time) / (frameB.Time - frameA.Time);

					float it = frameB.In * -1.0f;
					float ot = frameA.Out;

					float dx = frameB.Time - frameA.Time;
					float dy = p1 - p0;

					if ( frameA.Mode == HandleMode.Flat ) ot = 0;
					if ( frameB.Mode == HandleMode.Flat ) it = 0;

					return p0 + t * (t * (t * ((it + ot) * dx - 2.0f * dy) + (-it - 2.0f * ot) * dx + 3.0f * dy) + ot * dx);
				}
		}
	}

	/// <summary>
	/// If the curve is broken in some way, we can fix it here.
	/// Ensures correct time and value ranges, and that the curve has at least one point.
	/// </summary>
	public void Fix()
	{
		if ( ValueRange == 0 ) ValueRange = new Vector2( 0, 1 );
		if ( TimeRange == 0 ) TimeRange = new Vector2( 0, 1 );
		if ( Length == 0 ) AddPoint( 0.5f, 0.5f );
	}

	private sealed class JsonConverter : JsonConverter<Curve>
	{
		public override Curve Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.Number )
			{
				return reader.GetSingle();
			}

			if ( reader.TokenType == JsonTokenType.String )
			{
				// TODo I dunno
				return 0.5f;
			}

			if ( reader.TokenType == JsonTokenType.StartArray )
			{
				Curve c = new Curve();
				var keys = JsonSerializer.Deserialize<ImmutableArray<Frame>>( ref reader, options );
				c = c.WithFrames( keys );
				c.Fix();
				return c;
			}

			if ( reader.TokenType == JsonTokenType.StartObject )
			{
				reader.Read();

				Curve c = new Curve();

				while ( reader.TokenType != JsonTokenType.EndObject )
				{
					if ( reader.TokenType == JsonTokenType.PropertyName )
					{
						var name = reader.GetString();
						reader.Read();

						if ( name == "rangex" )
						{
							c.TimeRange = JsonSerializer.Deserialize<Vector2>( ref reader, options );
						}

						if ( name == "rangey" )
						{
							c.ValueRange = JsonSerializer.Deserialize<Vector2>( ref reader, options );
						}

						if ( name == "frames" )
						{
							if ( reader.TokenType == JsonTokenType.StartArray )
							{
								var keys = JsonSerializer.Deserialize<ImmutableArray<Frame>>( ref reader, options );
								c = c.WithFrames( keys );
							}
						}

						continue;

					}

					reader.Read();
				}

				c.Fix();
				return c;
			}

			return 0.54f;
		}

		public override void Write( Utf8JsonWriter writer, Curve val, JsonSerializerOptions options )
		{
			//
			// If ranges are default, just do an array
			//
			if ( val.TimeRange == new Vector2( 0, 1 ) && val.ValueRange == new Vector2( 0, 1 ) )
			{
				JsonSerializer.Serialize( writer, val.Frames, options );
				return;
			}

			//
			// else do a more verbose object
			//
			writer.WriteStartObject();
			{
				if ( val.TimeRange != new Vector2( 0, 1 ) )
				{
					writer.WritePropertyName( "rangex" );
					JsonSerializer.Serialize( writer, val.TimeRange, options );
				}

				if ( val.ValueRange != new Vector2( 0, 1 ) )
				{
					writer.WritePropertyName( "rangey" );
					JsonSerializer.Serialize( writer, val.ValueRange, options );
				}

				if ( !val.Frames.IsDefaultOrEmpty )
				{
					writer.WritePropertyName( "frames" );
					JsonSerializer.Serialize( writer, val.Frames, options );
				}

			}
			writer.WriteEndObject();
		}
	}

	static float RemapDelta( float delta, in Vector2 oldRange, in Vector2 range )
	{
		var value = delta.Remap( 0, 1, oldRange.x, oldRange.y, false );
		return value.Remap( range.x, range.y, 0.0f, 1.0f, false );
	}

	public void UpdateValueRange( Vector2 newRange, bool retainValues )
	{
		if ( retainValues )
		{
			var oldRange = ValueRange;
			Frames = Frames.Select( x => { var a = x; a.Value = RemapDelta( a.Value, oldRange, newRange ); return a; } ).ToImmutableArray();
		}

		ValueRange = newRange;
	}

	public void UpdateTimeRange( Vector2 newRange, bool retainTimes )
	{
		if ( retainTimes )
		{
			var oldRange = TimeRange;
			Frames = Frames.Select( x => { var a = x; a.Time = RemapDelta( a.Time, oldRange, newRange ); return a; } ).ToImmutableArray();
		}
		TimeRange = newRange;
	}
}


/// <summary>
/// Two curves
/// </summary>
public struct CurveRange
{
	[JsonPropertyName( "a" )]
	public Curve A { readonly get; set; }

	[JsonPropertyName( "b" )]
	public Curve B { readonly get; set; }

	public CurveRange()
	{
		A = new Curve();
		B = new Curve();
	}

	public CurveRange( in Curve a, in Curve b )
	{
		A = a;
		B = b;
	}

	public readonly float Evaluate( float x, float y )
	{
		var a = A.Evaluate( x );
		var b = B.Evaluate( x );

		var v = MathX.Lerp( a, b, y );

		return v;
	}

	public readonly float EvaluateDelta( float x, float y )
	{
		var a = A.EvaluateDelta( x );
		var b = B.EvaluateDelta( x );

		return MathX.Lerp( a, b, y );
	}
}
