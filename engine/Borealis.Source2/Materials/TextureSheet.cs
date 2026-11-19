using Borealis.Source2.Resources;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Borealis.Source2.Materials;

/// <summary>
/// A compiled texture's sprite sheet (<c>VTEX_EXTRA_DATA_SHEET</c>, <c>Sheet_t</c>): its sequences of frames, each frame's
/// display time and its images' rectangles in the texture as (left u, top v, right u, bottom v), cropped to what the image
/// covers and uncropped.
/// </summary>
public sealed record TextureSheet( TextureSheet.Sequence[] Sequences )
{
	/// <summary>One image of a frame (<c>SheetFrameImage_t</c>).</summary>
	public readonly record struct Image( Vector4 Cropped, Vector4 Uncropped );

	/// <summary>A frame (<c>SheetSequenceFrame_t</c>): how long it shows and its images.</summary>
	public sealed record Frame( float DisplayTime, Image[] Images );

	/// <summary>A sequence (<c>SheetSequence_t</c>): its id, whether it clamps at its end rather than looping, and its frames.</summary>
	public sealed record Sequence( int Id, bool Clamp, bool AlphaCrop, float TotalTime, Frame[] Frames );

	const int ExtraDataSheet = 2;

	/// <summary>Time samples a sequence of more than one frame is expanded to (<c>SEQUENCE_SAMPLE_COUNT</c>).</summary>
	const int SampleCount = 1024;

	/// <summary>Rows of the sheet cache a sequence takes (<c>SHEETCACHE_TEXTURE_NUMROWS_PER_SEQUENCE</c>).</summary>
	public const int RowsPerSequence = 3;

	/// <summary>
	/// The sheet as rows of native's sheet cache texture (<c>CSheetDataTextureCache::AllocateAndUploadSequenceData</c>), which
	/// sprite shaders read through <c>SampleSheetData</c>: <see cref="RowsPerSequence"/> rows of <paramref name="width"/>
	/// 16-bit RGBA texels a sequence, sequence 0's first, by sequence id with any gaps filled from the first sequence. Each
	/// column pair is the sequence at one point of its time: its blend, time scale, clamp and end blend, then its two frames'
	/// rectangles and the crop they share, in 0-65535. <paramref name="sequences"/> is how many sequences the rows hold.
	/// </summary>
	public ushort[] CacheRows( int width, out int sequences )
	{
		var infos = Expand();
		sequences = infos.Length;
		var rows = new ushort[infos.Length * RowsPerSequence * width * 4];
		var records = width / 2;
		var row = width * 4;
		for ( int s = 0; s < infos.Length; s++ )
		{
			var info = infos[s];
			var block = s * RowsPerSequence * row;
			for ( int i = 0; i < records; i++ )
			{
				var at = block + 4 * i * 2;
				var sample = info.At( i * (1.0f / records) );

				// The blend at the next record when it's between the same two frames, so a slow sequence's blend moves smoothly
				ushort end = 65535;
				if ( i < records - 1 || info.Clamp )
				{
					var next = info.At( (i + 1) * (1.0f / records) );
					if ( next.Start == sample.Start ) end = Fixed( next.Blend );
				}

				rows[at] = Fixed( sample.Blend );
				rows[at + 1] = (ushort)Math.Clamp( 16.0f * info.FrameSpan, 0.0f, 65535.0f );
				rows[at + 2] = info.Clamp ? (ushort)0xffff : (ushort)0;
				rows[at + 3] = end;

				if ( info.Frames == 0 ) continue;

				// Cropped rectangles, so the bounds are in 0-1 and fit 16 bits; the crop both frames share between them
				var crop0 = Fraction( sample.Uncropped0, sample.Cropped0, -1 );
				var crop1 = Fraction( sample.Uncropped1, sample.Cropped1, -1 );
				var union = new Vector4( MathF.Min( crop0.X, crop1.X ), MathF.Min( crop0.Y, crop1.Y ), MathF.Max( crop0.Z, crop1.Z ), MathF.Max( crop0.W, crop1.W ) );
				Pixels( rows, at + row, Fraction( sample.Uncropped0, union, +1 ) );
				Pixels( rows, at + row + 4, union );
				Pixels( rows, at + row * 2, Fraction( sample.Uncropped1, union, +1 ) );
			}
		}

		return rows;
	}

	/// <summary>A rectangle's four values as 0-65535 (<c>SetTexturePixelsForRect</c>).</summary>
	static void Pixels( ushort[] rows, int at, Vector4 rect )
	{
		rows[at] = Fixed( rect.X );
		rows[at + 1] = Fixed( rect.Y );
		rows[at + 2] = Fixed( rect.Z );
		rows[at + 3] = Fixed( rect.W );
	}

	static ushort Fixed( float value ) => float.IsNaN( value ) ? (ushort)0 : (ushort)Math.Clamp( value * 65535.0f, 0.0f, 65535.0f );

	/// <summary><c>ComputeRelativeRect</c>: <paramref name="crop"/> moved by <paramref name="sign"/> times <paramref name="full"/>'s corner, its size kept.</summary>
	static Vector4 Relative( Vector4 full, Vector4 crop, int sign )
	{
		var left = crop.X + sign * full.X;
		var top = crop.Y + sign * full.Y;
		return new Vector4( left, top, left + (crop.Z - crop.X), top + (crop.W - crop.Y) );
	}

	/// <summary><c>ComputeFractionRect</c>: <paramref name="crop"/> as a fraction of <paramref name="full"/> (-1), or that fraction back in the texture (+1).</summary>
	static Vector4 Fraction( Vector4 full, Vector4 crop, int sign )
	{
		var size = Relative( full, full, -1 );
		if ( sign < 0 )
		{
			var r = Relative( full, crop, -1 );
			return new Vector4( r.X / size.Z, r.Y / size.W, r.Z / size.Z, r.W / size.W );
		}

		return Relative( full, new Vector4( crop.X * size.Z, crop.Y * size.W, crop.Z * size.Z, crop.W * size.W ), +1 );
	}

	/// <summary>A point of a sequence's time (<c>SheetSequenceSample_t</c>): the two frames it's between and how far.</summary>
	readonly record struct Sample( float Blend, int Start, Vector4 Cropped0, Vector4 Uncropped0, Vector4 Cropped1, Vector4 Uncropped1 );

	/// <summary>A sequence expanded to its time samples (<c>SheetInfo_t</c>).</summary>
	sealed class Expanded
	{
		public Sample[] Samples;
		public bool Clamp;
		public int Frames;
		public float FrameSpan;

		/// <summary><c>CSheet::GetSampleForSequence</c> at <paramref name="age"/> (0-1) of the sequence.</summary>
		public Sample At( float age )
		{
			if ( Frames == 1 ) return Samples[0];
			var frame = (uint)(age * SampleCount);
			frame = Clamp ? Math.Min( frame, SampleCount - 1 ) : frame & (SampleCount - 1);
			return Samples[frame];
		}
	}

	/// <summary><c>CSheet( const Sheet_t * )</c>: every sequence by id, each sampled at <see cref="SampleCount"/> points of its time.</summary>
	Expanded[] Expand()
	{
		var count = 0;
		foreach ( var sequence in Sequences ) count = Math.Max( count, sequence.Id + 1 );
		var infos = new Expanded[count];
		for ( int i = 0; i < count; i++ ) infos[i] = new Expanded();

		foreach ( var sequence in Sequences )
		{
			if ( sequence.Id < 0 ) continue;
			var info = infos[sequence.Id];
			info.Clamp = sequence.Clamp;
			var frames = sequence.Frames.Length;
			info.Frames = frames;
			var samples = frames == 1 ? 1 : SampleCount;
			info.Samples = new Sample[samples];

			var total = sequence.TotalTime <= 0.0f ? 1.0f : sequence.TotalTime;

			// Clamped, the last frame ends at a blend of 1; looping, the first frame does
			var range = info.Clamp ? SampleCount - 1 : SampleCount;
			var knots = new float[frames];
			var time = 0.0f;
			for ( int f = 0; f < frames; f++ )
			{
				knots[f] = range * (time / total);
				time += sequence.Frames[f].DisplayTime;
				info.FrameSpan = time;
			}

			if ( frames == 0 ) continue;

			for ( int i = 0; i < samples; i++ )
			{
				var (a, b, t) = Interpolate( knots, range, i, !info.Clamp );
				var first = FirstImage( sequence.Frames[a] );
				var second = FirstImage( sequence.Frames[b] );
				info.Samples[i] = new Sample( t, a, first.Cropped, first.Uncropped, second.Cropped, second.Uncropped );
			}
		}

		// Sequences the sheet skips are copies of its first, as native's
		var seen = Array.Find( infos, i => i.Samples is not null );
		if ( seen is not null )
		{
			foreach ( var info in infos )
			{
				if ( info.Samples is not null ) continue;
				info.Samples = seen.Samples;
				info.Clamp = seen.Clamp;
				info.Frames = seen.Frames;
				info.FrameSpan = seen.FrameSpan;
			}
		}

		return infos;
	}

	static Image FirstImage( Frame frame ) => frame.Images.Length > 0 ? frame.Images[0] : default;

	/// <summary>
	/// mathlib's <c>GetInterpolationData</c> over knots whose values are their indices: the frames <paramref name="at"/> is
	/// between and how far from the first, wrapping from the last knot to the first when <paramref name="wrap"/>.
	/// </summary>
	static (int A, int B, float T) Interpolate( float[] knots, int range, float at, bool wrap )
	{
		var count = knots.Length;
		int index = 0;
		while ( index < count && !(knots[index] > at) ) index++;

		int first, second;
		float gap, offset;
		if ( index == 0 )
		{
			if ( !wrap || knots[0] == at ) return (0, count > 1 ? 1 : 0, 0.0f);
			first = count - 1;
			second = 0;
			gap = knots[second] + (range - knots[first]);
			offset = at + (range - knots[first]);
		}
		else if ( index == count )
		{
			if ( !wrap ) return (count > 1 ? count - 2 : count - 1, count - 1, 1.0f);
			first = count - 1;
			second = 0;
			gap = knots[second] + (range - knots[first]);
			offset = at - knots[first];
		}
		else
		{
			first = index - 1;
			second = index;
			gap = knots[second] - knots[first];
			offset = at - knots[first];
		}

		return (first, second, offset / gap);
	}

	/// <summary>The sheet in a compiled texture's bytes, or null when it has none.</summary>
	public static TextureSheet Read( byte[] data )
	{
		if ( data is null ) return null;

		try
		{
			var resource = ResourceFile.Read( data );
			var (offset, _) = resource.Find( "DATA" );

			// VTEX_Header_t: version, flags, reflectivity, size, format, mips, picmip; then the extra data's offset and count
			var extraStart = offset + 4 + 16 + 6 + 2 + 4;
			var extraOffset = Int( data, extraStart );
			var extraCount = Int( data, extraStart + 4 );
			for ( int i = 0; i < extraCount; i++ )
			{
				var entry = extraStart + extraOffset + i * 12;
				if ( Int( data, entry ) != ExtraDataSheet ) continue;
				return ReadSheet( data, entry + 4 + Int( data, entry + 4 ) );
			}
		}
		catch ( Exception e ) when ( e is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException )
		{
			// Not a texture, or one cut short: no sheet
		}

		return null;
	}

	// Offsets in the sheet are relative to where they're stored
	static TextureSheet ReadSheet( byte[] data, int at )
	{
		var count = Int( data, at + 4 );
		var sequences = new Sequence[count];
		var position = at + 8;
		for ( int s = 0; s < count; s++, position += 32 )
		{
			var id = Int( data, position );
			var clamp = data[position + 4] != 0;
			var alphaCrop = data[position + 5] != 0;
			var framesAt = position + 8 + Int( data, position + 8 );
			var frameCount = Int( data, position + 12 );
			var totalTime = Float( data, position + 16 );

			var frames = new Frame[frameCount];
			for ( int f = 0; f < frameCount; f++ )
			{
				var frame = framesAt + f * 12;
				var imagesAt = frame + 4 + Int( data, frame + 4 );
				var images = new Image[Int( data, frame + 8 )];
				for ( int i = 0; i < images.Length; i++ )
				{
					var image = imagesAt + i * 32;
					images[i] = new Image(
						new Vector4( Float( data, image ), Float( data, image + 4 ), Float( data, image + 8 ), Float( data, image + 12 ) ),
						new Vector4( Float( data, image + 16 ), Float( data, image + 20 ), Float( data, image + 24 ), Float( data, image + 28 ) ) );
				}

				frames[f] = new Frame( Float( data, frame ), images );
			}

			sequences[s] = new Sequence( id, clamp, alphaCrop, totalTime, frames );
		}

		return new TextureSheet( sequences );
	}

	static int Int( byte[] data, int at ) => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( at, 4 ) );
	static float Float( byte[] data, int at ) => BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( at, 4 ) );
}
