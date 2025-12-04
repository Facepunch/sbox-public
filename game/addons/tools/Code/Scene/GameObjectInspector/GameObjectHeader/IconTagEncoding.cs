using System;
using System.Text;

namespace Editor
{
	public static class IconTagEncoding
	{
		public static string EncodeIconToTag( string icon )
		{
			if ( string.IsNullOrEmpty( icon ) ) return null;
			var bytes = Encoding.UTF8.GetBytes( icon );
			var sb = new StringBuilder();
			sb.Append( "icon_" );
			foreach ( var b in bytes ) sb.Append( b.ToString( "X2" ) );
			return sb.ToString();
		}

		public static string DecodeIconFromTag( string tag )
		{
			if ( string.IsNullOrEmpty( tag ) ) return null;
			if ( !tag.StartsWith( "icon_" ) ) return null;
			var hex = tag.Substring( 5 );
			if ( hex.Length == 0 || hex.Length % 2 != 0 ) return null;
			try
			{
				var bytes = new byte[hex.Length / 2];
				for ( int i = 0; i < bytes.Length; i++ ) bytes[i] = Convert.ToByte( hex.Substring( i * 2, 2 ), 16 );
				return Encoding.UTF8.GetString( bytes );
			}
			catch
			{
				return null;
			}
		}
	}
}
