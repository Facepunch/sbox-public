namespace Sandbox;

internal partial class BytePack
{
	class StringPacker : Packer
	{
		public override Type TargetType => typeof( string );
		internal override Identifier Header => Identifier.String;

		public override void Write( ref ByteStream bs, object obj )
		{
			// null strings are a thing
			if ( obj is null )
			{
				bs.Write( (string)null );
				return;
			}

			// this isn't even a string!
			if ( obj is not string str )
			{
				throw new NotSupportedException( $"{obj} is not a string!" );
			}

			bs.Write( str );
		}

		public override object Read( ref ByteStream bs, int depth )
		{
			return bs.Read<string>();
		}
	}
}
