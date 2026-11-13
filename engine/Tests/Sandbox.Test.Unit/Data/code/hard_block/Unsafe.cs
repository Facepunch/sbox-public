using System;
using System.Runtime.CompilerServices;

class Example
{
	static void Main()
	{
		// Use Unsafe.As
		float value = 123.45f;
		int intBits = Unsafe.As<float, int>( ref value );
		Console.WriteLine( $"Float bits as int: {intBits}" );
	}
}
