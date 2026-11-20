using Borealis.Source2.Shaders;
using System.Linq;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// The constant buffers the backend fills by hand, field by field, for its own shaders: each one's C# struct, the
/// shader's name for it, the prefix of its members' names (a member is the prefix and the field's name; <c>Pad</c> fields
/// pad) and the shaders that declare it. <c>Borealis.Gpu.Test</c> checks every struct against each shader's reflection,
/// and Debug builds check the size of what's bound against the shader's (<see cref="CheckSize"/>), since a struct that
/// drifts from its shader reads garbage on the GPU without a word.
/// </summary>
internal static class HandFilledConstants
{
	public readonly record struct Entry( Type Struct, string Name, string MemberPrefix, string[] Shaders );

	public static readonly Entry[] All =
	[
		new( typeof( BackendContext.GpuDrivenConstants ), "BorealisGpuDriven_t", "g_nBorealis", ["shaders/borealis_gpu_driven_ids.shader", "shaders/borealis_ids_normals_cs.shader"] ),
		new( typeof( BackendContext.GpuDrivenCullConstants ), "BorealisGpuDrivenCull_t", "g_nCull", ["shaders/borealis_gpu_driven_cull_cs.shader", "shaders/borealis_gpu_driven_compact_cs.shader"] ),
		new( typeof( BackendContext.GpuDrivenMarkConstants ), "BorealisGpuDrivenMark_t", "g_nMark", ["shaders/borealis_gpu_driven_mark_cs.shader"] ),
		new( typeof( ResidentCullConstants ), "BorealisResidentCull_t", "g_nResident", ["shaders/borealis_resident_cull_cs.shader"] ),
		new( typeof( BackendContext.MeshletCullConstants ), "BorealisMeshletCull_t", "g_nMeshlet", ["shaders/borealis_meshlet_cull_cs.shader"] ),
		new( typeof( BackendContext.HiZConstants ), "BorealisHiZ_t", "g_nHiZ", ["shaders/borealis_hiz_cs.shader"] ),
		new( typeof( BackendContext.IdsNormalsConstants ), "BorealisIdsNormals_t", "g_nIds", ["shaders/borealis_ids_normals_cs.shader"] ),
	];

	/// <summary>
	/// Where a struct and a shader's layout of its constant buffer disagree: a member with no field of its name (the
	/// prefix taken off), at another offset or of another size; a field (not padding) the shader doesn't have; a buffer
	/// larger than the struct. Empty when they agree.
	/// </summary>
	public static List<string> Compare( Entry entry, ConstantBufferLayout layout )
	{
		var problems = new List<string>();
		var size = Marshal.SizeOf( entry.Struct );
		if ( layout.Size > size ) problems.Add( $"{layout.Name} is {layout.Size} bytes, {entry.Struct.Name} {size}" );

		var fields = entry.Struct.GetFields( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance );
		foreach ( var member in layout.Members ?? [] )
		{
			var name = member.Name.StartsWith( entry.MemberPrefix, StringComparison.Ordinal ) ? member.Name[entry.MemberPrefix.Length..] : null;
			var field = name is null ? null : Array.Find( fields, f => f.Name == name );
			if ( field is null )
			{
				problems.Add( $"{layout.Name}.{member.Name} has no field in {entry.Struct.Name}" );
				continue;
			}

			var offset = (int)Marshal.OffsetOf( entry.Struct, field.Name );
			if ( offset != member.Offset ) problems.Add( $"{layout.Name}.{member.Name} is at {member.Offset}, {entry.Struct.Name}.{field.Name} at {offset}" );

			var bytes = Math.Max( member.Columns, 1 ) * Math.Max( member.Rows, 1 ) * 4 * Math.Max( member.ArraySize, 1 );
			var fieldBytes = Marshal.SizeOf( field.FieldType );
			if ( bytes != fieldBytes ) problems.Add( $"{layout.Name}.{member.Name} is {bytes} bytes, {entry.Struct.Name}.{field.Name} {fieldBytes}" );
		}

		foreach ( var field in fields )
		{
			if ( field.Name.StartsWith( "Pad", StringComparison.Ordinal ) ) continue;
			if ( !Array.Exists( layout.Members ?? [], m => m.Name == entry.MemberPrefix + field.Name ) ) problems.Add( $"{entry.Struct.Name}.{field.Name} isn't in {layout.Name}" );
		}

		return problems;
	}

	/// <summary>
	/// Where a struct and a stage's compiled uniform buffer disagree, from its SPIR-V: a member (by name where the module
	/// keeps names, else by offset) with no field, at another offset or of another size, or past the struct's end. A
	/// member the compiler dropped isn't missed.
	/// </summary>
	public static List<string> CompareSpirv( Entry entry, SpirvBlock block, string where )
	{
		var problems = new List<string>();
		var size = Marshal.SizeOf( entry.Struct );
		var fields = entry.Struct.GetFields( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance );
		foreach ( var member in block.Members )
		{
			if ( member.Offset + member.Size > size ) problems.Add( $"{where}: a member at {member.Offset} ({member.Size} bytes) is past the end of {entry.Struct.Name} ({size} bytes)" );

			System.Reflection.FieldInfo field;
			if ( !string.IsNullOrEmpty( member.Name ) )
			{
				var name = member.Name.StartsWith( entry.MemberPrefix, StringComparison.Ordinal ) ? member.Name[entry.MemberPrefix.Length..] : member.Name;
				field = Array.Find( fields, f => f.Name == name );
				if ( field is null )
				{
					problems.Add( $"{where}: {member.Name} has no field in {entry.Struct.Name}" );
					continue;
				}

				var offset = (int)Marshal.OffsetOf( entry.Struct, field.Name );
				if ( offset != member.Offset ) problems.Add( $"{where}: {member.Name} is at {member.Offset}, {entry.Struct.Name}.{field.Name} at {offset}" );
			}
			else
			{
				field = Array.Find( fields, f => (int)Marshal.OffsetOf( entry.Struct, f.Name ) == member.Offset );
				if ( field is null )
				{
					problems.Add( $"{where}: a member at {member.Offset} starts inside a field of {entry.Struct.Name}" );
					continue;
				}
			}

			var fieldBytes = Marshal.SizeOf( field.FieldType );
			if ( member.Size != 0 && member.Size != fieldBytes ) problems.Add( $"{where}: the member at {member.Offset} is {member.Size} bytes, {entry.Struct.Name}.{field.Name} {fieldBytes}" );
		}

		return problems;
	}

	static readonly Dictionary<string, int> Sizes = All.ToDictionary( e => e.Name, e => Marshal.SizeOf( e.Struct ) );

	/// <summary>
	/// Debug builds: throw when what's bound for one of these constant buffers isn't the size of its struct, or the shader
	/// compiled it larger than the struct (it would read past what's written).
	/// </summary>
	[System.Diagnostics.Conditional( "DEBUG" )]
	public static void CheckSize( ShaderStage stage, string name, int bytes )
	{
		if ( name is null || !Sizes.TryGetValue( name, out var size ) ) return;
		if ( bytes != size ) throw new InvalidOperationException( $"{name}: {bytes} bytes bound, its struct is {size}" );
		foreach ( var layout in stage?.Program.ConstantBuffers ?? [] )
		{
			if ( layout.Name == name && layout.Size > size ) throw new InvalidOperationException( $"{name}: {stage.Program.Shader?.Name} compiles it as {layout.Size} bytes, its struct is {size}" );
		}
	}
}
