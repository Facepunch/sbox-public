using Borealis.Backend;
using Borealis.Source2.Shaders;

namespace BorealisGpuTests;

/// <summary>
/// The constant buffers the backend fills by hand (<see cref="HandFilledConstants"/>) match every shader that reads them, as
/// each shader compiles them: member offsets and sizes, no member without a field, no field without a member.
/// </summary>
[TestClass]
[DoNotParallelize]
public class HandFilledConstantsTests : GpuTest
{
	[TestMethod]
	public void StructsMatchTheirShaders()
	{
		using var backend = new BackendDevice( Device );
		using var drawer = new ShaderDrawer( backend );
		var problems = new List<string>();
		var members = 0;
		foreach ( var entry in HandFilledConstants.All )
		{
			foreach ( var path in entry.Shaders )
			{
				var shader = backend.Shaders.Load( path );
				if ( shader is null )
				{
					problems.Add( $"{path} didn't load" );
					continue;
				}

				// The stages the device draws it with: its module's where it has one
				var variant = drawer.Variant( shader, new ShaderAttributes() );
				var stages = new[] { variant?.Vertex, variant?.Pixel, variant?.Compute }.Where( s => s?.Bytecode is not null ).ToArray();

				// The tables' layout, where they describe the members (a .shader's do, a module's give the size alone)
				var layouts = stages.SelectMany( s => s.Program.ConstantBuffers ).Where( l => l.Name == entry.Name ).ToArray();
				if ( layouts.Length == 0 ) problems.Add( $"{path} has no {entry.Name}" );
				foreach ( var layout in layouts )
				{
					if ( layout.Members.Length > 0 ) problems.AddRange( HandFilledConstants.Compare( entry, layout ).Select( p => $"{path}: {p}" ) );
					else if ( layout.Size > System.Runtime.InteropServices.Marshal.SizeOf( entry.Struct ) ) problems.Add( $"{path}: {entry.Name} is {layout.Size} bytes" );
				}

				// And the SPIR-V each stage runs: the uniform buffer at the binding the variant gives the name
				var resource = variant.Resources.FirstOrDefault( r => r.Kind == ShaderResourceKind.ConstantBuffer && r.Name == entry.Name );
				var blocks = stages.SelectMany( s => s.Reflection.Blocks.Where( b => b.Set == resource.Set && b.Binding == resource.Binding ) ).ToArray();
				if ( blocks.Length == 0 ) problems.Add( $"{path}: no SPIR-V uniform buffer at {entry.Name}'s binding" );
				foreach ( var block in blocks )
				{
					problems.AddRange( HandFilledConstants.CompareSpirv( entry, block, $"{path} SPIR-V {block.Name}" ) );
					members += block.Members.Length;
				}
			}
		}

		Assert.AreEqual( 0, problems.Count, string.Join( Environment.NewLine, problems ) );
		Assert.IsTrue( members >= 20, $"members compared ({members})" );
	}

	/// <summary>
	/// The comparisons find a field that moved, a member with no field and a field the shader lacks; and in SPIR-V without
	/// names, a member starting inside a field.
	/// </summary>
	[TestMethod]
	public void ComparisonFindsDrift()
	{
		var entry = HandFilledConstants.All.First( e => e.Name == "BorealisGpuDrivenMark_t" );
		var layout = new ConstantBufferLayout
		{
			Name = entry.Name,
			Size = 32,
			Members = [("g_nMarkWidth", 0, 1, 1, 0), ("g_nMarkHeight", 8, 1, 1, 0), ("g_nMarkSamples", 8, 1, 1, 0), ("g_nMarkDepth", 12, 1, 1, 0)],
		};

		var problems = HandFilledConstants.Compare( entry, layout );
		Assert.IsTrue( problems.Any( p => p.Contains( "g_nMarkHeight is at 8", StringComparison.Ordinal ) ), string.Join( "; ", problems ) );
		Assert.IsTrue( problems.Any( p => p.Contains( "g_nMarkDepth has no field", StringComparison.Ordinal ) ), string.Join( "; ", problems ) );
		Assert.IsTrue( problems.Any( p => p.Contains( "Bits isn't in", StringComparison.Ordinal ) ), string.Join( "; ", problems ) );

		var spirv = HandFilledConstants.CompareSpirv( entry, new SpirvBlock( 0, 0, null, [new SpirvMember( null, 0, 4 ), new SpirvMember( null, 6, 4 ), new SpirvMember( null, 40, 4 )] ), "test" );
		Assert.IsTrue( spirv.Any( p => p.Contains( "at 6 starts inside", StringComparison.Ordinal ) ), string.Join( "; ", spirv ) );
		Assert.IsTrue( spirv.Any( p => p.Contains( "past the end", StringComparison.Ordinal ) ), string.Join( "; ", spirv ) );
	}
}
