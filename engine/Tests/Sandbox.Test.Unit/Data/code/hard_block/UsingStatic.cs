using System;
using static System.Runtime.InteropServices.MemoryMarshal;

class UsingStaticTest
{
	static void Main()
	{
		int[] array = { 10, 20, 30, 40 };
		ReadOnlySpan<int> span = CreateReadOnlySpan( ref array[0], array.Length );
		foreach ( var item in span )
		{
			Console.WriteLine( item );
		}
	}
}
