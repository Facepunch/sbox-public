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
			var len = bs.Read<int>();

			var handler = GetHandlerFor( ref bs );

			var array = Array.CreateInstance( handler.TargetType, len );
			bs.ReadValueArray( array );

			return array;
		}
	}

}
