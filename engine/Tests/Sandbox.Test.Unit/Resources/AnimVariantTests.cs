using NativeEngine;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace ResourceTests;

/// <summary>
/// Anim graph bools, enums and ints are stored as raw integer bits, and small integers are denormal floats.
/// A thread that flushes denormals (FTZ/DAZ) must still round-trip them, including constants the JIT folds
/// while compiling on that thread - a literal <c>Set( "b_grounded", true )</c> used to arrive as false.
/// </summary>
[TestClass]
public class AnimVariantTests
{
	const uint DenormalFlush = 0x01000000; // _DN_FLUSH
	const uint DenormalMask = 0x03000000; // _MCW_DN

	[DllImport( "ucrtbase.dll", CallingConvention = CallingConvention.Cdecl )]
	static extern int _controlfp_s( out uint current, uint newControl, uint mask );

	// Compiled fully optimised on its first call, so the conversions are inlined and their constants folded
	// on whichever thread calls it first.
	[MethodImpl( MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining )]
	static (bool Bool, int Int, byte Enum, int FoldedBits) ConvertConstants()
	{
		AnimVariant b = true;
		AnimVariant i = 2;
		AnimVariant e = (byte)3;
		// The JIT's own fold of the old encoding, for checking the test conditions reproduce the bug.
		var folded = new[] { BitConverter.Int32BitsToSingle( 1 ) };
		return ((bool)b, (int)i, (byte)e, BitConverter.SingleToInt32Bits( folded[0] ));
	}

	[TestMethod]
	public void ConstantsSurviveDenormalFlushing()
	{
		if ( !OperatingSystem.IsWindows() )
			Assert.Inconclusive( "Uses the MSVC runtime to flush denormals." );

		(bool Bool, int Int, byte Enum, int FoldedBits) result = default;
		var flushedSum = -1f;

		var thread = new Thread( () =>
		{
			_controlfp_s( out _, DenormalFlush, DenormalMask );

			// Proves the thread really flushes: the smallest denormal doubled comes out as zero.
			var tiny = new[] { BitConverter.Int32BitsToSingle( Environment.TickCount64 > 0 ? 1 : 0 ) };
			flushedSum = tiny[0] + tiny[0];

			result = ConvertConstants();
		} );

		thread.Start();
		thread.Join();

		Assert.AreEqual( 0f, flushedSum, "The test thread should be flushing denormals" );
		Console.WriteLine( $"Int32BitsToSingle( 1 ) folded on the flushing thread: bits {result.FoldedBits}" );
		Assert.IsTrue( result.Bool, "Literal true" );
		Assert.AreEqual( 2, result.Int, "Literal int" );
		Assert.AreEqual( (byte)3, result.Enum, "Literal enum" );
	}

	[TestMethod]
	public void ScalarValuesRoundTrip()
	{
		Assert.IsTrue( (bool)(AnimVariant)true );
		Assert.IsFalse( (bool)(AnimVariant)false );
		Assert.AreEqual( int.MaxValue, (int)(AnimVariant)int.MaxValue );
		Assert.AreEqual( -1, (int)(AnimVariant)(-1) );
		Assert.AreEqual( (byte)255, (byte)(AnimVariant)(byte)255 );
		Assert.AreEqual( 0.25f, (float)(AnimVariant)0.25f );

		Assert.IsTrue( ((AnimVariant)true).TryConvertTo( AnimParamType.Int, out var asInt ) );
		Assert.AreEqual( 1, (int)asInt );
		Assert.IsTrue( ((AnimVariant)2).TryConvertTo( AnimParamType.Bool, out var asBool ) );
		Assert.IsTrue( (bool)asBool );
		Assert.IsTrue( ((AnimVariant)(byte)4).TryConvertTo( AnimParamType.Float, out var asFloat ) );
		Assert.AreEqual( 4f, (float)asFloat );
	}
}
