using SkiaSharp;
using System.IO;
using System.Text;

namespace Sandbox;

public partial class Bitmap
{
	public unsafe static Bitmap CreateFromIesBytes( byte[] data )
	{
		if ( !IsIes( data ) )
			return default;

		const int width = 512;
		const int height = 512;

		var ies = new IES();

		try
		{
			using var stream = new MemoryStream( data );
			ies.ParseIESInfo( stream );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to parse IES file: {ex.Message}" );
			return default;
		}

		if ( ies.CandelasMax <= 0 )
		{
			Log.Warning( "IES file has invalid maximum candela value." );
			return default;
		}

		var brightness = 1.0f / ies.CandelasMax;
		var imageData = new Half[width * height * 4];

		for ( var y = 0; y < height; y++ )
		{
			for ( var x = 0; x < width; x++ )
			{
				var px = (x / (float)width) * 2 - 1;
				var py = (y / (float)height) * 2 - 1;

				var radius = MathF.Sqrt( px * px + py * py );
				var angle = MathF.Atan2( py, px ) * 180 / MathF.PI;

				var candela = ies.Sample2D( radius, angle );
				candela = MathF.Max( 0, candela * brightness );
				candela = MathF.Pow( candela, 2.2f );

				var idx = (y * width + x) * 4;

				imageData[idx + 0] = (Half)candela;
				imageData[idx + 1] = (Half)candela;
				imageData[idx + 2] = (Half)candela;
				imageData[idx + 3] = (Half)1.0f;
			}
		}

		var bitmap = new SKBitmap( width, height, SKColorType.RgbaF16, SKAlphaType.Unpremul );

		unsafe
		{
			fixed ( Half* ptr = imageData )
			{
				bitmap.SetPixels( (IntPtr)ptr );
			}
		}

		return new Bitmap( bitmap );
	}

	private class IES
	{
		public int NumLamps;
		public float LumensPerLamp;
		public float CandelaMultiplier;

		public int NumHorizontal;
		public int NumVertical;

		public int PhotometricType;
		public int UnitsType;

		public float Width;
		public float Length;
		public float Height;

		public float BallastFactor;
		public float Unused;
		public float InputWatts;

		public List<float> VerticalAngles;
		public List<float> HorizontalAngles;
		public List<float> Candelas;

		public float CandelasMax;

		public void ParseIESInfo( Stream stream )
		{
			using var reader = new StreamReader( stream );
			_ = reader.ReadLine();

			// Read until tilt information, we don't care the metadata that's behind
			while ( !reader.EndOfStream )
			{
				var line = reader.ReadLine();
				if ( line.StartsWith( "TILT=" ) )
				{
					var tilt = line.Substring( 5 );
					if ( tilt != "NONE" )
					{
						throw new Exception( "Tilted IES files are not supported" );
					}
					break;
				}
			}

			if ( reader.EndOfStream )
			{
				throw new Exception( "IES file is missing TILT information" );
			}

			// We get the light info on this line
			var lightInfo = reader.ReadToEnd().Split( new string[] { " ", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries );

			NumLamps = int.Parse( lightInfo[0] );
			LumensPerLamp = float.Parse( lightInfo[1] );
			CandelaMultiplier = float.Parse( lightInfo[2] );
			NumVertical = int.Parse( lightInfo[3] );
			NumHorizontal = int.Parse( lightInfo[4] );
			PhotometricType = int.Parse( lightInfo[5] );
			UnitsType = int.Parse( lightInfo[6] );
			Width = float.Parse( lightInfo[7] );
			Length = float.Parse( lightInfo[8] );
			Height = float.Parse( lightInfo[9] );
			BallastFactor = float.Parse( lightInfo[10] );
			Unused = float.Parse( lightInfo[11] );
			InputWatts = float.Parse( lightInfo[12] );

			VerticalAngles = new List<float>();
			HorizontalAngles = new List<float>();
			Candelas = new List<float>();

			int i = 13;

			for ( int y = 0; y < NumVertical; y++, i++ )
			{
				VerticalAngles.Add( float.Parse( lightInfo[i] ) );
			}

			for ( int x = 0; x < NumHorizontal; x++, i++ )
			{
				HorizontalAngles.Add( float.Parse( lightInfo[i] ) );
			}

			for ( int x = 0; x < NumHorizontal; x++ )
			{
				for ( int y = 0; y < NumVertical; y++, i++ )
				{
					Candelas.Add( float.Parse( lightInfo[i] ) );
				}
			}

			CandelasMax = Candelas.Max();
		}

		public float Sample2D( float radius, float angleDeg )
		{
			// Early out if radius is beyond 1
			if ( radius > 1.0f ) return 0;

			// Convert angle to 0-360 range
			angleDeg = ((angleDeg % 360) + 360) % 360;

			// Mirror angles above 180 degrees back to 0-180 range
			if ( angleDeg > 180.0f )
			{
				angleDeg = 360.0f - angleDeg;
			}

			// Find horizontal angle indices
			int h1 = 0, h2 = 0;
			float hBlend = 0;

			for ( int i = 0; i < NumHorizontal - 1; i++ )
			{
				if ( angleDeg >= HorizontalAngles[i] && angleDeg <= HorizontalAngles[i + 1] )
				{
					h1 = i;
					h2 = i + 1;
					hBlend = (angleDeg - HorizontalAngles[i]) / (HorizontalAngles[i + 1] - HorizontalAngles[i]);
					break;
				}
			}

			// Convert radius to vertical angle (0 = straight down, 1 = horizontal)
			float vertAngle = 90.0f * radius;

			// Find vertical angle indices
			int v1 = 0, v2 = 0;
			float vBlend = 0;

			for ( int i = 0; i < NumVertical - 1; i++ )
			{
				if ( vertAngle >= VerticalAngles[i] && vertAngle <= VerticalAngles[i + 1] )
				{
					v1 = i;
					v2 = i + 1;
					vBlend = (vertAngle - VerticalAngles[i]) / (VerticalAngles[i + 1] - VerticalAngles[i]);
					break;
				}
			}

			// Bilinear interpolation of candela values
			float c11 = Candelas[h1 * NumVertical + v1];
			float c12 = Candelas[h1 * NumVertical + v2];
			float c21 = Candelas[h2 * NumVertical + v1];
			float c22 = Candelas[h2 * NumVertical + v2];

			float c1 = c11 * (1 - vBlend) + c12 * vBlend;
			float c2 = c21 * (1 - vBlend) + c22 * vBlend;

			float candela = c1 * (1 - hBlend) + c2 * hBlend;

			// Apply candela multiplier
			return candela * CandelaMultiplier;
		}
	}

	/// <summary>
	/// Return true if this data is a Ies file
	/// </summary>
	public static bool IsIes( byte[] data )
	{
		if ( data == null || data.Length < 10 )
			return false;

		var header = Encoding.ASCII.GetString( data, 0, Math.Min( 20, data.Length ) ).Trim();
		return header.StartsWith( "IESNA" ) || header.StartsWith( "IESNA:LM-63" );
	}
}
