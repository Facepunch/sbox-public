using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Engine.Shaders;

namespace EngineTests;

/// <summary>
/// shader-stats measures what the driver compiles for a combo, so a combo that only differs by specialization
/// constants has to come out of the dump as its own module with those values baked in.
/// </summary>
[TestClass]
public class SpirvDumpTest
{
	const uint OpDecorate = 71, OpSpecConstant = 50, OpSpecConstantFalse = 49, OpSpecConstantTrue = 48;
	const uint DecorationSpecId = 1;

	static uint Op( uint opcode, int words ) => (uint)words << 16 | opcode;

	/// <summary>
	/// A module with uint spec constant %10 (SpecId 0), bool %11 (SpecId 1) and %12 that no constant names.
	/// Not a valid shader, just the instructions the patch reads.
	/// </summary>
	static uint[] Module() =>
	[
		0x07230203, 0x00010500, 0, 20, 0,
		Op( OpDecorate, 4 ), 10, DecorationSpecId, 0,
		Op( OpDecorate, 4 ), 11, DecorationSpecId, 1,
		Op( OpSpecConstant, 4 ), 1, 10, 0,
		Op( OpSpecConstantFalse, 3 ), 2, 11,
		Op( OpSpecConstant, 4 ), 1, 12, 7,
	];

	static byte[] Blob( int version, uint[] spirv, byte[] reflection, (uint Id, uint Value)[] constants )
	{
		var words = new List<uint> { (uint)version, (uint)(spirv.Length * 4) };
		words.AddRange( spirv );
		var bytes = words.SelectMany( BitConverter.GetBytes ).Concat( reflection ).ToList();
		if ( constants is not null )
		{
			bytes.AddRange( BitConverter.GetBytes( (uint)constants.Length ) );
			foreach ( var (id, value) in constants )
			{
				bytes.AddRange( BitConverter.GetBytes( id ) );
				bytes.AddRange( BitConverter.GetBytes( value ) );
			}
		}
		return bytes.ToArray();
	}

	static uint[] Words( byte[] spirv ) => Enumerable.Range( 0, spirv.Length / 4 ).Select( i => BitConverter.ToUInt32( spirv, i * 4 ) ).ToArray();

	[TestMethod]
	public void PlainModuleIsReturnedAsIs()
	{
		var module = Module();
		var spirv = SpirvDump.GetSpirv( Blob( 4, module, [1, 2, 3, 4, 5, 6], null ) );

		CollectionAssert.AreEqual( module, Words( spirv ) );
	}

	[TestMethod]
	public void SpecializationConstantsAreBakedIn()
	{
		// Reflection that happens to end in values which look like a count, to catch a parse that stops too early
		byte[] reflection = [.. BitConverter.GetBytes( 1u ), .. BitConverter.GetBytes( 0u ), .. BitConverter.GetBytes( 1u )];
		var spirv = Words( SpirvDump.GetSpirv( Blob( 5, Module(), reflection, [(0, 3), (1, 1)] ) ) );

		Assert.AreEqual( 3u, spirv[5 + 4 + 4 + 3], "SpecId 0 takes the combo's value" );
		Assert.AreEqual( Op( OpSpecConstantTrue, 3 ), spirv[5 + 4 + 4 + 4], "A bool constant flips to true" );
		Assert.AreEqual( 7u, spirv[5 + 4 + 4 + 4 + 3 + 3], "A constant without a SpecId keeps its value" );
	}

	[TestMethod]
	public void EmptyStageHasNoModule()
	{
		Assert.IsNull( SpirvDump.GetSpirv( [] ) );
		Assert.IsNull( SpirvDump.GetSpirv( Blob( 4, [], [], null ) ) );
	}
}
