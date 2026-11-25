namespace Sandbox;

internal partial class BytePack
{
	/// <summary>
	/// A value array is useful because we only have to write the header once, and it's a single
	/// block of memory to read. Useful.
	/// </summary>
	class ValueArrayPacker : Packer
	{
		internal override Identifier Header => Identifier.ArrayValue;

		public override void Write( ref ByteStream bs, object obj )
		{
			if ( obj is not Array array ) throw new NotSupportedException();

			bs.Write( array.Length );

			var et = array.GetType().GetElementType();
			var w = GetHandlerFor( et );


			w.WriteTypeIdentifier( ref bs, et );
			bs.WriteValueArray( array );
		}

		public override object Read( ref ByteStream bs, int depth )
		{
			var len = ReadCount( ref bs );

			var handler = GetHandlerFor( ref bs );
			if ( (long)len * handler.TargetType.GetManagedSize() > bs.ReadRemaining )
				throw new System.IO.InvalidDataException( $"Array of {len} {handler.TargetType} doesn't fit in {bs.ReadRemaining} bytes" );

			var array = Array.CreateInstance( handler.TargetType, len );
			bs.ReadValueArray( array );

			return array;
		}
	}

}
