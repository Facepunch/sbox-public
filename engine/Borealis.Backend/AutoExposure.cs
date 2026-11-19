using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Runtime.InteropServices;
using Num = System.Numerics;

namespace Borealis.Backend;

/// <summary>
/// Auto exposure as native's <c>CTonemapSystem</c> (<c>src/scenesystem/utils/tonemapsystem.cpp</c>) works it out, on a
/// Borealis.Gpu device: a histogram of the frame's luminance - sixteen buckets of log luminance from 2^-8 to 2^5, as s&amp;box's
/// game info sets (<c>Tonemapping_UseLogLuminance</c>), or the classic eight - one bucket counted a frame by drawing
/// <c>tonemap_query</c> over the middle of the frame inside an occlusion query, the count read back three frames later, and
/// the exposure moved towards a target found from the histogram at a rate in f-stops a second. Call <see cref="FrameUpdate"/> once a frame before the frame uses <see cref="Scale"/>, and
/// <see cref="Draw"/> once a frame over the frame's HDR colour before it's tonemapped.
/// </summary>
/// <remarks>
/// The arithmetic is native's, in the same order and precision, so with the same counts and times it gives the same
/// scale. Not ported: the debug histogram and spew (<c>mat_tonemap_debug</c>), the cheat convars that force values
/// (<c>mat_tonemap_force_*</c>, <c>mat_tonemap_uncap_exposure</c>), alpha-channel luminance (a game info key s&amp;box
/// doesn't set) and the weighted override (<c>OverrideScaleWithWeight</c>, which nothing calls).
/// </remarks>
public sealed class AutoExposure : IDisposable
{
	/// <summary>
	/// <c>SceneTonemapParameters_t</c>: what a camera sets. A negative value isn't set, and the system's default is used.
	/// </summary>
	public struct Settings
	{
		/// <summary>The exposure's range (<c>m_flAutoExposureMin</c>, <c>Max</c>).</summary>
		public float Min, Max;

		/// <summary>A scale on the target exposure (<c>m_flExposureCompensationScalar</c>).</summary>
		public float Compensation;

		/// <summary>The histogram's targets, in percent (<c>m_flTonemapPercentTarget</c>, <c>PercentBrightPixels</c>, <c>MinAvgLum</c>).</summary>
		public float PercentTarget, PercentBrightPixels, MinAverageLuminance;

		/// <summary>How fast the exposure moves (<c>m_flRate</c>; 0 is at once) and the legacy downward factor (<c>m_flAccelerateExposureDown</c>).</summary>
		public float Rate, AccelerateDown;

		/// <summary>F-stops a second, up and down, before the rate (<c>m_flExposureAdaptationSpeedUp</c>, <c>Down</c>).</summary>
		public float SpeedUp, SpeedDown;

		/// <summary>Within this many f-stops of the target, it moves half the difference a second (<c>m_flTonemapEVSmoothingRange</c>).</summary>
		public float EvSmoothingRange;

		/// <summary>Nothing set.</summary>
		public static Settings Unset => new()
		{
			Min = -1,
			Max = -1,
			Compensation = -1,
			PercentTarget = -1,
			PercentBrightPixels = -1,
			MinAverageLuminance = -1,
			Rate = -1,
			AccelerateDown = -1,
			SpeedUp = -1,
			SpeedDown = -1,
			EvSmoothingRange = -1,
		};
	}

	/// <summary>Histogram buckets: sixteen of log luminance, or the classic algorithm's eight.</summary>
	public int Buckets { get; }

	/// <summary>Whether the histogram is of log luminance (s&amp;box's) rather than the classic one.</summary>
	public bool LogLuminance { get; }

	const float LogHistogramMin = -8.0f, LogHistogramMax = 5.0f;

	enum State { Initial, FirstQueryInFlight, QueryInFlight, QueryDone }

	struct Bucket
	{
		public State State;
		public bool Queried;
		public int FrameQueued;
		public int Pixels;
		public float Min, Max;
		public bool IgnoreNext;

		public readonly bool Valid => State is not (State.Initial or State.FirstQueryInFlight) && Pixels >= 0;
	}

	static readonly Settings Defaults = new()
	{
		Min = 0.25f,
		Max = 8.0f,
		Compensation = 1.0f,
		PercentTarget = 60.0f,
		PercentBrightPixels = 5.0f,
		MinAverageLuminance = 3.0f,
		Rate = 2.0f,
		AccelerateDown = 4.0f,
		SpeedUp = 1.0f,
		SpeedDown = 2.0f,
		EvSmoothingRange = 100.0f,
	};

	readonly Device gpu;
	readonly OcclusionQueries queries;
	readonly Bucket[] buckets;
	readonly float[] movingAverage = new float[10];
	Settings overrides = Settings.Unset;
	Settings current;
	int queryFrame;
	int movingAverageValid;
	float targetScale = 1, currentScale = 1, shaderScale = 1;
	double lastTime;

	/// <summary>
	/// An exposure counting with occlusion queries on <paramref name="gpu"/>, with a histogram of log luminance as s&amp;box's
	/// game info asks for, or the classic one.
	/// </summary>
	public AutoExposure( Device gpu, bool logLuminance = true )
	{
		this.gpu = gpu;
		LogLuminance = logLuminance;
		Buckets = logLuminance ? 16 : 8;
		buckets = new Bucket[Buckets];
		queries = gpu.CreateOcclusionQueries( Buckets );
		for ( int i = 0; i < Buckets; i++ ) buckets[i].Pixels = -1;
		for ( int i = 0; i < movingAverage.Length - 1; i++ ) movingAverage[i] = 1.0f;
		UpdateParameters();
		UpdateBucketRanges();
	}

	/// <summary>The exposure to draw this frame with (<c>GetCurrentTonemappingScale</c>).</summary>
	public float Scale => shaderScale;

	/// <summary>The exposure it's moving towards.</summary>
	public float Target => targetScale;

	/// <summary>A bucket's luminance range and the pixels counted in it, or -1 when it has no count yet, for reports.</summary>
	public (float Min, float Max, int Pixels) BucketAt( int index ) => (buckets[index].Min, buckets[index].Max, buckets[index].Valid ? buckets[index].Pixels : -1);

	/// <summary>Set what a camera sets (<c>SetTonemapParameters</c>); <see cref="Settings.Unset"/> goes back to the defaults.</summary>
	public void SetSettings( in Settings settings )
	{
		overrides = settings;
		UpdateParameters();
	}

	/// <summary>
	/// Start a frame at <paramref name="time"/> seconds (<c>FrameUpdate</c>): read the counts that have come back, then move
	/// the exposure towards its target by the time since the last frame. The first call only notes the time.
	/// </summary>
	public void FrameUpdate( double time )
	{
		UpdateParameters();
		UpdateBucketRanges();
		queryFrame++;
		ReceiveQueries();
		CalculateScale( time );
	}

	/// <summary>
	/// Count this frame's bucket (<c>RenderLuminanceQuery</c>): <paramref name="shader"/> (<c>tonemap_query</c>) drawn over
	/// the middle 90% of the viewport (<paramref name="x"/>, <paramref name="y"/>, <paramref name="width"/>, <paramref name="height"/>, in pixels) of <paramref name="color"/>, in
	/// <paramref name="context"/>'s list, into an occlusion query. <paramref name="context"/> has a colour target of the
	/// frame's size bound, which isn't drawn to. False when every bucket is still waiting for its count.
	/// <paramref name="multisampled"/>: <paramref name="color"/> is a multisampled frame's resolved colour, metered as
	/// native meters the multisampled colour itself (its query reads only red there), so both expose the same.
	/// </summary>
	public bool Draw( GraphicsContext context, ShaderFile shader, TextureHandle color, int x, int y, int width, int height, bool multisampled = false )
	{
		var bucket = NextBucket();
		if ( bucket < 0 ) return false;

		var info = gpu.InfoOf( color );
		int aw = (int)(width * 0.9f), ah = (int)(height * 0.9f);
		int ax = x + ((width - aw) >> 1), ay = y + ((height - ah) >> 1);

		// ComputeScreenSpaceVertices: the adjusted rect in the viewport's clip space, reading the same rect of the colour
		float left = ax / (float)info.Width, right = (ax + aw) / (float)info.Width, top = ay / (float)info.Height, bottom = (ay + ah) / (float)info.Height;
		float x0 = Lerp( -1, 1, x, x + width, ax ), x1 = Lerp( -1, 1, x, x + width, ax + aw );
		float y0 = Lerp( 1, -1, y, y + height, ay ), y1 = Lerp( 1, -1, y, y + height, ay + ah );
		ReadOnlySpan<QueryVertex> vertices =
		[
			new( x0, y1, left, bottom ),
			new( x1, y1, right, bottom ),
			new( x0, y0, left, top ),
			new( x1, y0, right, top ),
		];

		var attributes = drawAttributes;
		attributes.Clear();
		attributes.Set( "LuminanceMin", buckets[bucket].Min );
		attributes.Set( "LuminanceMax", buckets[bucket].Max );
		attributes.Set( "UseAlphaForAutoExposure", 0.0f );
		attributes.Set( "MeterRedAsMultisampled", multisampled ? 1.0f : 0.0f );
		attributes.SetCombo( "D_TONEMAP_LOG_LUM", LogLuminance ? 1 : 0 );
		attributes.SetCombo( "D_MSAA_COLOR_BUFFER", 0 );
		attributes.Set( "ColorBuffer", color );

		return context.DrawCounted( shader, attributes, MemoryMarshal.AsBytes( vertices ), QueryLayout, 4, PrimitiveTopology.TriangleStrip, queries, bucket );
	}

	// The count draw's attributes, emptied and set again for each: drawn at once, so one set serves every frame
	readonly ShaderAttributes drawAttributes = new();

	/// <summary>FLerp: <paramref name="f1"/> at <paramref name="i1"/> to <paramref name="f2"/> at <paramref name="i2"/>.</summary>
	static float Lerp( float f1, float f2, float i1, float i2, float x ) => f1 + (f2 - f1) * (x - i1) / (i2 - i1);

	/// <summary>mathlib's Lerp: <paramref name="a"/> to <paramref name="b"/> by <paramref name="t"/>.</summary>
	static float Lerp( float t, float a, float b ) => a + (b - a) * t;

	/// <summary><c>VertexPos2DTex2D_t</c>.</summary>
	[StructLayout( LayoutKind.Sequential )]
	readonly record struct QueryVertex( float X, float Y, float U, float V );

	static readonly VertexStreamLayout QueryLayout = new( 16,
		new VertexField( VertexSemantic.Position, 0, VertexFormat.Float2, 0 ),
		new VertexField( VertexSemantic.TexCoord, 0, VertexFormat.Float2, 8 ) );

	/// <summary>Start over at <paramref name="scale"/> (<c>ResetTonemappingScale</c>; 0 or less, between the min and max).</summary>
	public void Reset( float scale )
	{
		if ( scale <= 0.0f ) scale = (current.Min + current.Max) * 0.5f;
		currentScale = targetScale = shaderScale = scale;
		movingAverageValid = 0;
		for ( int i = 0; i < Buckets; i++ )
		{
			buckets[i].Pixels = -1;
			buckets[i].IgnoreNext = buckets[i].State is State.FirstQueryInFlight or State.QueryInFlight;
		}
	}

	static float Choose( float value, float fallback ) => value >= 0 ? value : fallback >= 0 ? fallback : -1;

	void UpdateParameters()
	{
		current.Min = Choose( overrides.Min, Defaults.Min );
		current.Max = Choose( overrides.Max, Defaults.Max );
		if ( current.Max < current.Min ) current.Max = current.Min;

		current.PercentTarget = Choose( overrides.PercentTarget, Defaults.PercentTarget );
		current.PercentTarget = MathF.Min( current.PercentTarget, (0.99f - 0.0175f - 0.05f) * 100.0f );
		current.PercentBrightPixels = Choose( overrides.PercentBrightPixels, Defaults.PercentBrightPixels );
		current.MinAverageLuminance = Choose( overrides.MinAverageLuminance, Defaults.MinAverageLuminance );
		current.MinAverageLuminance = MathF.Min( current.MinAverageLuminance, current.PercentTarget );

		current.Rate = Choose( overrides.Rate, Defaults.Rate );
		current.AccelerateDown = Choose( overrides.AccelerateDown, Defaults.AccelerateDown );
		current.SpeedUp = Choose( overrides.SpeedUp, Defaults.SpeedUp );
		current.SpeedDown = Choose( overrides.SpeedDown, Defaults.SpeedDown );
		current.Compensation = Choose( overrides.Compensation, Defaults.Compensation );
		current.EvSmoothingRange = Choose( overrides.EvSmoothingRange, Defaults.EvSmoothingRange );
	}

	void UpdateBucketRanges()
	{
		Span<float> ranges = stackalloc float[17];
		if ( LogLuminance )
		{
			// Even steps of log luminance; the first bucket takes every darker pixel and the last every brighter one
			for ( int i = 0; i <= Buckets; i++ ) ranges[i] = Lerp( (float)i / (float)Buckets, LogHistogramMin, LogHistogramMax );
			ranges[0] = -float.MaxValue;
			ranges[Buckets] = float.MaxValue;
		}
		else
		{
			ReadOnlySpan<float> classic =
			[
				0.0f,
				current.MinAverageLuminance / 100.0f,
				current.MinAverageLuminance / 100.0f * 2.0f,
				current.PercentTarget / 100.0f - 0.0175f - 0.05f,
				current.PercentTarget / 100.0f - 0.0175f,
				current.PercentTarget / 100.0f + 0.0175f,
				current.PercentTarget / 100.0f + 0.0175f + 0.05f,
				0.99f,
				1.0f,
			];
			classic.CopyTo( ranges );
		}

		for ( int i = 0; i < Buckets; i++ )
		{
			// Increasing, in case the values above were out of range; a bucket's top is the next one's bottom before that
			if ( i > 0 ) ranges[i] = MathF.Max( ranges[i], ranges[i - 1] );
			buckets[i].Min = ranges[i];
			buckets[i].Max = ranges[i + 1];
		}
	}

	void ReceiveQueries()
	{
		for ( int i = 0; i < Buckets; i++ )
		{
			ref var bucket = ref buckets[i];
			if ( !bucket.Queried || bucket.State is not (State.FirstQueryInFlight or State.QueryInFlight) ) continue;
			if ( queryFrame <= bucket.FrameQueued + 2 || !queries.TryRead( i, out var pixels ) ) continue;

			if ( !bucket.IgnoreNext ) bucket.Pixels = (int)pixels;
			bucket.IgnoreNext = false;
			bucket.State = State.QueryDone;
		}
	}

	/// <summary>The bucket to count this frame (<c>GenerateRendererData</c>): one never counted, else the oldest counted; -1 for none.</summary>
	int NextBucket()
	{
		var index = -1;
		for ( int i = 0; i < Buckets && index < 0; i++ )
		{
			if ( buckets[i].State == State.Initial ) index = i;
		}

		if ( index < 0 )
		{
			for ( int i = 0; i < Buckets; i++ )
			{
				if ( buckets[i].State == State.QueryDone && (index < 0 || buckets[i].FrameQueued < buckets[index].FrameQueued) ) index = i;
			}
		}

		if ( index < 0 ) return -1;

		ref var bucket = ref buckets[index];
		bucket.State = bucket.State == State.Initial ? State.FirstQueryInFlight : State.QueryInFlight;
		bucket.FrameQueued = queryFrame;
		bucket.Queried = true;
		return index;
	}

	float FindLocationOfPercentBrightPixels( float percentBrightPixels, float percentTargetToSnapTo = -1.0f )
	{
		var total = 0;
		for ( int i = 0; i < Buckets; i++ )
		{
			if ( buckets[i].Valid ) total += buckets[i].Pixels;
		}

		if ( total == 0 ) return -1.0f;

		// From the bright end
		float rangeTested = 0.0f, pixelsTested = 0.0f;
		for ( int i = Buckets - 1; i >= 0; i-- )
		{
			ref var bucket = ref buckets[i];
			if ( !bucket.Valid ) return -1.0f;

			var needed = (percentBrightPixels / 100.0f) - pixelsTested;
			var share = (float)bucket.Pixels / (float)total;
			var range = bucket.Max - bucket.Min;
			if ( share >= needed )
			{
				// Sticky: in the target's bucket, the scale stays where it is
				if ( percentTargetToSnapTo >= 0.0f && bucket.Min <= (percentTargetToSnapTo / 100.0f) && bucket.Max >= (percentTargetToSnapTo / 100.0f) )
					return percentTargetToSnapTo / 100.0f;

				var part = needed / share;
				var border = 1.0f - (rangeTested + (range * part));
				return MathF.Max( bucket.Min, MathF.Min( bucket.Max, border ) );
			}

			pixelsTested += share;
			rangeTested += range;
		}

		return -1.0f;
	}

	float ComputeTargetScale()
	{
		if ( LogLuminance ) return ComputeTargetLogLuminance();

		var location = FindLocationOfPercentBrightPixels( current.PercentBrightPixels, current.PercentTarget );
		if ( location < 0.0f ) location = current.PercentTarget / 100.0f;
		location = MathF.Max( 0.001f, location );

		var target = (current.PercentTarget / 100.0f) / location;

		// Brighten more if half the pixels are darker than the minimum average
		var average = FindLocationOfPercentBrightPixels( 50.0f );
		if ( average > 0.0f )
		{
			var target2 = (current.MinAverageLuminance / 100.0f) / average;
			if ( target2 > target ) target = target2;
		}

		target *= currentScale;
		var clamped = Clamp( target, current.Min, current.Max );
		return MathF.Max( 0.001f, clamped );
	}

	/// <summary>
	/// The exposure that maps the median of the log histogram to middle grey (<c>ComputeTargetExposureLogLuminance</c>):
	/// the median found between bucket centres, not against the current exposure.
	/// </summary>
	float ComputeTargetLogLuminance()
	{
		Span<float> accumulated = stackalloc float[Buckets];
		Span<float> logLuminance = stackalloc float[Buckets];
		var total = 0.0f;
		for ( int i = 0; i < Buckets; i++ )
		{
			logLuminance[i] = Lerp( ((float)i + 0.5f) / (float)Buckets, LogHistogramMin, LogHistogramMax );
			total += buckets[i].Pixels;
			accumulated[i] = total;
		}

		// The median (mat_tonemap_force_log_lum_min and max at their defaults, 0.5)
		const float rangeMin = 0.5f, rangeMax = 0.5f;
		var minCount = rangeMin * total;
		var maxCount = rangeMax * total;
		int minBucket = -1, maxBucket = -1;
		for ( int i = 0; i < Buckets; i++ )
		{
			if ( accumulated[i] >= minCount && minBucket == -1 ) minBucket = i;
			if ( accumulated[i] >= maxCount && maxBucket == -1 ) maxBucket = i;
		}

		var minLum = BucketLuminance( accumulated, logLuminance, minBucket, minCount );
		var maxLum = BucketLuminance( accumulated, logLuminance, maxBucket, maxCount );
		var midPoint = MathF.Pow( 2.0f, 0.5f * (maxLum + minLum) );
		return 0.18f / midPoint;
	}

	static float BucketLuminance( ReadOnlySpan<float> accumulated, ReadOnlySpan<float> logLuminance, int bucket, float count )
	{
		if ( bucket <= 0 ) return logLuminance[Math.Max( bucket, 0 )];
		var t = (count - accumulated[bucket - 1]) / (accumulated[bucket] - accumulated[bucket - 1]);
		return Lerp( t, logLuminance[bucket - 1], logLuminance[bucket] );
	}

	/// <summary>mathlib's clamp.</summary>
	static float Clamp( float value, float min, float max ) => value < min ? min : value > max ? max : value;

	void CalculateScale( double time )
	{
		if ( lastTime == 0.0 ) lastTime = time;
		var frameTime = (float)(time - lastTime);
		lastTime = time;

		// Every bucket needs a count first
		for ( int i = 0; i < Buckets; i++ )
		{
			if ( !buckets[i].Valid ) return;
		}

		var target = ComputeTargetScale();
		if ( !float.IsFinite( target ) ) return;

		if ( movingAverageValid < movingAverage.Length )
		{
			movingAverage[movingAverageValid++] = target;
		}
		else
		{
			for ( int i = 0; i < movingAverage.Length - 1; i++ ) movingAverage[i] = movingAverage[i + 1];
			movingAverage[^1] = target;
		}

		// Weighted towards the oldest and newest of the last ten targets
		if ( movingAverageValid == movingAverage.Length )
		{
			float average = 0.0f, weights = 0.0f;
			var middle = movingAverage.Length / 2;
			for ( int i = 0; i < movingAverage.Length; i++ )
			{
				var weight = Math.Abs( i - middle ) * (1.0f / (movingAverage.Length / 2));
				weights += weight;
				average += weight * movingAverage[i];
			}

			average *= 1.0f / weights;
			average = Clamp( average, current.Min, current.Max );
			if ( float.IsFinite( average ) ) targetScale = average;
		}
		else
		{
			targetScale = Clamp( target, current.Min, current.Max );
		}

		if ( current.Compensation > 0.0f ) targetScale *= current.Compensation;

		// Towards the target in f-stops a second
		var elapsed = MathF.Max( frameTime, 0.0f );
		var speed = current.SpeedUp * current.Rate;
		if ( speed == 0.0f )
		{
			currentScale = targetScale;
		}
		else
		{
			var rate = current.Rate;
			if ( targetScale < currentScale ) speed = current.SpeedDown;

			var currentEv = MathF.Log2( currentScale );
			var targetEv = MathF.Log2( targetScale );
			var delta = MathF.Abs( targetEv - currentEv );
			var half = delta * 0.5f;

			// Near the target, half the difference a second, for a smooth approach
			if ( delta < current.EvSmoothingRange && half < speed ) speed = half;
			if ( targetScale < currentScale ) speed = -speed;
			speed *= rate;

			currentScale = MathF.Pow( 2.0f, currentEv + speed * elapsed );

			// No overshoot
			currentScale = speed < 0.0f ? MathF.Max( currentScale, targetScale ) : MathF.Min( currentScale, targetScale );
			if ( !float.IsFinite( currentScale ) ) currentScale = targetScale;
		}

		shaderScale = currentScale;
	}

	/// <summary>Free the queries; the GPU must be done with them.</summary>
	public void Dispose() => queries.Dispose();
}
