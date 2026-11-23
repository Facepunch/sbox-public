
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Sandbox.UI
{
	public partial struct PanelTransform
	{
		internal void Parse( string value )
		{
			var p = new Parse( value );

			p.SkipWhitespaceAndNewlines();

			if ( List == null )
				List = ImmutableList.Create<Entry>();

			while ( !p.IsEnd )
			{
				var key = p.ReadWord( "(" );

				p = p.SkipWhitespaceAndNewlines();

				if ( p.Current != '(' ) throw new System.Exception( $"Expecting ( {p.FileAndLine}" );
				p.Pointer++;
				p = p.SkipWhitespaceAndNewlines();

				var val = p.ReadUntil( ")" );
				if ( p.Current != ')' ) throw new System.Exception( $"Expecting ) {p.FileAndLine}" );
				p.Pointer++;

				p = p.SkipWhitespaceAndNewlines();

				Set( key, val );
			}
		}

		private bool ParseSkew( string val )
		{
			var p = new Parse( val );
			if ( !p.TryReadFloat( out var valueX ) )
				return false;
			p = p.SkipWhitespaceAndNewlines();

			float x = StyleHelpers.RotationDegrees( valueX, p.ReadUntilWhitespaceOrNewlineOrEnd() );
			p = p.SkipWhitespaceAndNewlines();

			if ( !p.TryReadFloat( out var valueY ) )
				return false;

			float y = StyleHelpers.RotationDegrees( valueY, p.ReadRemaining( true ) );

			return AddSkew( x, y, 0 );
		}

		private bool ParseTranslate( string val, bool is3d = false )
		{
			var p = new Parse( val );

			if ( !p.TryReadLength( out var x ) )
				return false;

			if ( !p.TryReadLength( out var y ) )
			{
				return AddTranslate( x, new Length() );
			}

			if ( is3d && p.TryReadLength( out var z ) )
			{
				return AddTranslate( x, y, z );
			}

			return AddTranslate( x, y );
		}

		private bool ParseMatrix( string val )
		{
			var p = new Parse( val );
			float[] matrix = new float[6];
			for ( int i = 0; i < 6; i++ )
			{
				if ( !p.TryReadFloat( out matrix[i] ) )
				{
					return false;
				}
				p = p.SkipWhitespaceAndNewlines( "," );
			}

			float[] matrix3d = new float[16];
			matrix3d[0] = matrix[0];
			matrix3d[1] = matrix[1];
			matrix3d[4] = matrix[2];
			matrix3d[5] = matrix[3];
			matrix3d[10] = 1.0f;
			matrix3d[12] = matrix[4];
			matrix3d[13] = matrix[5];
			matrix3d[15] = 1.0f;

			return AddMatrix3D( Matrix.CreateMatrix3D( matrix3d ) );
		}

		private bool ParseMatrix3D( string val )
		{
			var p = new Parse( val );
			float[] matrix = new float[16];
			for ( int i = 0; i < 16; i++ )
			{
				if ( !p.TryReadFloat( out matrix[i] ) )
				{
					return false;
				}
				p = p.SkipWhitespaceAndNewlines( "," );
			}

			return AddMatrix3D( Matrix.CreateMatrix3D( matrix ) );
		}

		private bool Set( string key, string val )
		{
			if ( key.Equals( "rotate", StringComparison.OrdinalIgnoreCase ) ) return AddRotation( 0, 0, ReadRotation( val ) );
			if ( key.Equals( "rotatex", StringComparison.OrdinalIgnoreCase ) ) return AddRotation( ReadRotation( val ), 0, 0 );
			if ( key.Equals( "rotatey", StringComparison.OrdinalIgnoreCase ) ) return AddRotation( 0, ReadRotation( val ), 0 );
			if ( key.Equals( "rotatez", StringComparison.OrdinalIgnoreCase ) ) return AddRotation( 0, 0, ReadRotation( val ) );
			if ( key.Equals( "rotate3d", StringComparison.OrdinalIgnoreCase ) ) return AddRotation( Read3DRotation( val ) );

			if ( key.Equals( "scale", StringComparison.OrdinalIgnoreCase ) ) return AddScale( ReadVector( val, false ) );
			if ( key.Equals( "scale3d", StringComparison.OrdinalIgnoreCase ) ) return AddScale( ReadVector( val, true ) );
			if ( key.Equals( "scalex", StringComparison.OrdinalIgnoreCase ) ) return AddScale( new Vector3( ReadFloat( val ), 1, 1 ) );
			if ( key.Equals( "scaley", StringComparison.OrdinalIgnoreCase ) ) return AddScale( new Vector3( 1, ReadFloat( val ), 1 ) );
			if ( key.Equals( "scalez", StringComparison.OrdinalIgnoreCase ) ) return AddScale( new Vector3( 1, 1, ReadFloat( val ) ) );

			if ( key.Equals( "skew", StringComparison.OrdinalIgnoreCase ) ) return ParseSkew( val );
			if ( key.Equals( "skewx", StringComparison.OrdinalIgnoreCase ) ) return AddSkew( ReadRotation( val ), 0, 0 );
			if ( key.Equals( "skewy", StringComparison.OrdinalIgnoreCase ) ) return AddSkew( 0, ReadRotation( val ), 0 );

			if ( key.Equals( "translate", StringComparison.OrdinalIgnoreCase ) ) return ParseTranslate( val, false );
			if ( key.Equals( "translate3d", StringComparison.OrdinalIgnoreCase ) ) return ParseTranslate( val, true );
			if ( key.Equals( "translatex", StringComparison.OrdinalIgnoreCase ) ) return AddTranslateX( Length.Parse( val ) ?? new Length() );
			if ( key.Equals( "translatey", StringComparison.OrdinalIgnoreCase ) ) return AddTranslateY( Length.Parse( val ) ?? new Length() );
			if ( key.Equals( "translatez", StringComparison.OrdinalIgnoreCase ) ) return AddTranslateZ( Length.Parse( val ) ?? new Length() );

			if ( string.Compare( key, "matrix" ) == 0 ) return ParseMatrix( val );
			if ( key.Equals( "matrix3d", StringComparison.OrdinalIgnoreCase ) ) return ParseMatrix3D( val );

			if ( key.Equals( "perspective", StringComparison.OrdinalIgnoreCase ) ) return AddPerspective( Length.Parse( val ) ?? new Length() );

			return false;
		}

		private bool AddEntry( EntryType type, Vector3 data )
		{
			List = List.Add( new Entry { Type = type, Data = data } );
			return true;
		}

		private float ReadFloat( string value )
		{
			var p = new Parse( value );

			if ( !p.TryReadFloat( out var val ) )
				return 0;

			return val;
		}

		private float ReadRotation( string value )
		{
			var p = new Parse( value );

			if ( !p.TryReadFloat( out var val ) )
				return 0;

			p = p.SkipWhitespaceAndNewlines();
			return StyleHelpers.RotationDegrees( val, p.ReadRemaining( true ) );
		}

		private Vector3 Read3DRotation( string value )
		{
			var p = new Parse( value );

			Vector3 angles = Vector3.Zero;

			for ( int i = 0; i < 3; i++ )
			{
				if ( !p.TryReadFloat( out var component ) )
					return 0;

				p = p.SkipWhitespaceAndNewlines();
				float actual = StyleHelpers.RotationDegrees( component, p.ReadUntilWhitespaceOrNewlineOrEnd() );

				angles[i] = actual;
			}

			return angles;
		}

		private Vector3 ReadVector( string value, bool is3d = false )
		{
			var p = new Parse( value );
			var val = Vector3.One;

			if ( p.TryReadFloat( out var x ) )
			{
				val.x = x;
				val.y = x;
			}

			p = p.SkipWhitespaceAndNewlines( "," );

			if ( p.TryReadFloat( out var y ) )
				val.y = y;

			p = p.SkipWhitespaceAndNewlines( "," );

			if ( is3d && p.TryReadFloat( out var z ) )
				val.z = z;

			return new Vector3( val );
		}
	}
}
