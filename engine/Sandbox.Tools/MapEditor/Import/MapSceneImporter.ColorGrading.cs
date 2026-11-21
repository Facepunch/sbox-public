using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	const int GradingSize = 16;

	static bool IsGradingLayer( JsonObject layer ) => layer["m_pLayerMask"] is null &&
		(string)layer["_class"] is "CCurvesColorCorrectionLayer" or "CLevelsColorCorrectionLayer" or "CColorLookupColorCorrectionLayer";

	sealed partial class Conversion
	{
		readonly Dictionary<JsonArray, Texture> gradingTextures = new();

		public Texture GetGradingTexture( JsonArray layers )
		{
			if ( gradingTextures.TryGetValue( layers, out var texture ) ) return texture;
			if ( BakeGradingTable( layers ) is { } table )
			{
				using var bitmap = new Bitmap( GradingSize * GradingSize, GradingSize );
				var pixels = bitmap.GetBuffer();
				for ( int i = 0, j = 0; i < table.Length; i += 3, j += 4 )
				{
					pixels[j] = table[i];
					pixels[j + 1] = table[i + 1];
					pixels[j + 2] = table[i + 2];
					pixels[j + 3] = 255;
				}
				var png = bitmap.ToPng();
				var name = Convert.ToHexStringLower( SHA256.HashData( png ) );
				var folder = Path.Combine( Path.GetDirectoryName( scenePath ), "grading" );
				Directory.CreateDirectory( folder );
				var file = Path.Combine( folder, name + ".png" );
				if ( !File.Exists( file ) )
				{
					var temporary = file + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
					try
					{
						File.WriteAllBytes( temporary, png );
						File.Move( temporary, file, overwrite: true );
					}
					finally { File.Delete( temporary ); }
				}
				texture = Texture.Load( Path.GetRelativePath( assetsPath, file ).NormalizeFilename( false, false ) );
				if ( texture is null || texture.IsError ) throw new IOException( $"Could not load the generated grading texture '{file}'." );
			}
			gradingTextures.Add( layers, texture );
			return texture;
		}
	}

	// The scene LUT samples linear RGB. Evaluate Hammer's sRGB operators at those sample positions.
	internal static byte[] BakeGradingTable( JsonArray layers )
	{
		double[] samples = null;
		for ( int i = layers.Count - 1; i >= 0; i-- )
		{
			if ( layers[i] is not JsonObject layer || !IsGradingLayer( layer ) || layer["m_bVisible"]?.ToString() == "false" ) continue;
			double opacity = Math.Clamp( Number( layer, "m_nOpacityPercent", 100 ) / 100.0, 0, 1 );
			if ( opacity == 0 ) continue;
			bool lookup = (string)layer["_class"] == "CColorLookupColorCorrectionLayer";
			// Hammer compiles the saved table. The file name is the original editor import path.
			var data = lookup ? layer["m_lut"]?.GetValue<float[]>() : null;
			if ( lookup && data is not { Length: > 0 } ) continue;
			if ( samples is null )
			{
				samples = new double[GradingSize * 3];
				for ( int j = 0; j < GradingSize; j++ )
				{
					var linear = new Color( j / (float)(GradingSize - 1), 0, 0 );
					samples.AsSpan( j * 3, 3 ).Fill( linear.ToSrgb().r * 255.0 );
				}
			}
			if ( lookup )
			{
				samples = ExpandGradingTable( samples );
				ApplyLookup( samples, data, (int)Number( layer, "m_nDim", 0 ), opacity );
				continue;
			}
			bool levels = (string)layer["_class"] == "CLevelsColorCorrectionLayer";
			Func<double, double> Transfer( string channel ) => levels ? Levels( layer, channel ) : GradingCurve( layer["m_curvePoints" + channel] as JsonArray );
			var master = Transfer( "RGB" );
			for ( int c = 0; c < 3; c++ )
			{
				var channel = Transfer( "RGB"[c].ToString() );
				for ( int j = c; j < samples.Length; j += 3 )
				{
					double input = samples[j];
					samples[j] += (Math.Clamp( master( channel( input ) ), 0, 255 ) - input) * opacity;
				}
			}
		}
		return samples is null ? null : ExpandGradingTable( Array.ConvertAll( samples, ToLinearByte ) );

		static byte ToLinearByte( double sample )
		{
			var srgb = new Color( (float)(sample / 255), 0, 0 );
			return (byte)Math.Clamp( Math.Round( srgb.ToLinear().r * 255 ), 0, 255 );
		}
	}

	static T[] ExpandGradingTable<T>( T[] channels )
	{
		if ( channels.Length != GradingSize * 3 ) return channels;
		var cube = new T[GradingSize * GradingSize * GradingSize * 3];
		for ( int g = 0, i = 0; g < GradingSize; g++ )
			for ( int b = 0; b < GradingSize; b++ )
				for ( int r = 0; r < GradingSize; r++ )
				{
					cube[i++] = channels[3 * r];
					cube[i++] = channels[3 * g + 1];
					cube[i++] = channels[3 * b + 2];
				}
		return cube;
	}

	static void ApplyLookup( double[] samples, float[] lut, int size, double opacity )
	{
		if ( size < 1 || (long)size * size * size * 3 != lut.Length ) throw new InvalidDataException( "Invalid post-processing lookup dimensions." );
		int row = size * 3, slice = size * row;
		for ( int i = 0; i < samples.Length; i += 3 )
		{
			float r = Coordinate( samples[i] ), g = Coordinate( samples[i + 1] ), b = Coordinate( samples[i + 2] );
			int index = (int)r * 3 + (int)g * row + (int)b * slice;
			int dx = r < size - 1 ? 3 : 0, dy = g < size - 1 ? row : 0, dz = b < size - 1 ? slice : 0;
			float u = r - (int)r, v = g - (int)g, w = b - (int)b;
			for ( int c = 0; c < 3; c++, index++ )
			{
				float Row( int start ) => Lerp( lut[start], lut[start + dx], u );
				float Plane( int start ) => Lerp( Row( start ), Row( start + dy ), v );
				float graded = Lerp( Plane( index ), Plane( index + dz ), w );
				samples[i + c] = 255f * Lerp( (float)(samples[i + c] / 255), graded, (float)opacity );
			}
		}

		float Coordinate( double value ) => (float)Math.Clamp( value / 255, 0, 1 ) * (size - 1);
		// Hammer blends normalized floats before truncating to bytes, including at full opacity.
		static float Lerp( float a, float b, float t ) => a + (b - a) * t;
	}

	static Func<double, double> Levels( JsonObject layer, string channel )
	{
		double low = Number( layer, "m_nInputBlackPoint" + channel, 0 ), high = Number( layer, "m_nInputWhitePoint" + channel, 255 );
		double black = Number( layer, "m_nOutputBlackPoint" + channel, 0 ), white = Number( layer, "m_nOutputWhitePoint" + channel, 255 );
		double gamma = Number( layer, "m_flGamma" + channel, 1 );
		return x => black + (white - black) * Math.Pow( high == low ? (x >= high ? 1 : 0) : Math.Clamp( (x - low) / (high - low), 0, 1 ), gamma );
	}

	static Func<double, double> GradingCurve( JsonArray points )
	{
		if ( points is null || points.Count == 0 ) return x => x;
		int count = points.Count;
		var x = new double[count];
		var y = new double[count];
		var curvature = new double[count];
		var rhs = new double[count];
		for ( int i = 0; i < count; i++ )
		{
			x[i] = double.Parse( points[i][0].ToString(), CultureInfo.InvariantCulture );
			y[i] = double.Parse( points[i][1].ToString(), CultureInfo.InvariantCulture );
		}
		// Natural cubic spline: solve the tridiagonal system for the second derivatives.
		for ( int i = 1; i < count - 1; i++ )
		{
			double left = x[i] - x[i - 1], right = x[i + 1] - x[i];
			double weight = left / (left + right);
			double pivot = weight * curvature[i - 1] + 2;
			curvature[i] = (weight - 1) / pivot;
			double delta = (y[i + 1] - y[i]) / right - (y[i] - y[i - 1]) / left;
			rhs[i] = (6 * delta / (left + right) - weight * rhs[i - 1]) / pivot;
		}
		for ( int i = count - 2; i > 0; i-- ) curvature[i] = curvature[i] * curvature[i + 1] + rhs[i];
		return input =>
		{
			if ( input <= x[0] ) return y[0];
			if ( input >= x[^1] ) return y[^1];
			// Hammer traverses control points in authored order, including non-monotone curves.
			int upper = 1;
			while ( x[upper] < input ) upper++;
			int lower = upper - 1;
			double width = x[upper] - x[lower], a = (x[upper] - input) / width, b = 1 - a;
			return a * y[lower] + b * y[upper] + ((a * a * a - a) * curvature[lower] + (b * b * b - b) * curvature[upper]) * width * width / 6;
		};
	}
}
