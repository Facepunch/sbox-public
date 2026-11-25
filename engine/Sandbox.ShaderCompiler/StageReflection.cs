using System.Text.Json;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// A compiled entry point described for a renderer: the bindings its SPIR-V really uses (names from the SPIR-V, what
/// they're bound by from the includes' annotations), its <c>$Globals</c> members and its vertex inputs' semantics from
/// Slang's reflection JSON.
/// </summary>
internal static class StageReflection
{
	/// <summary>The name Slang gives the <c>$Globals</c> constant buffer's variable in SPIR-V.</summary>
	const string GlobalsVariable = "globalParams";

	/// <summary>The material records' buffer (sbox/material.slang).</summary>
	public const string MaterialRecords = "g_SurfaceMaterials";

	public static CompiledStage Describe( StageKind stage, string entry, byte[] spirv, string json, IReadOnlyDictionary<string, Annotations> annotations )
	{
		using var document = JsonDocument.Parse( json );
		var root = document.RootElement;
		var (variables, inputs, _) = Spirv.Read( spirv );

		var bindings = new List<ShaderBinding>();
		GlobalConstants globals = null;
		foreach ( var v in variables )
		{
			annotations.TryGetValue( v.Name, out var annotation );
			var kind = v.Kind;
			if ( v.Name == GlobalsVariable ) kind = BindingKind.Globals;
			else if ( v.Name == MaterialRecords ) kind = BindingKind.MaterialRecords;
			var size = kind == BindingKind.ConstantBuffer ? ConstantBufferSize( root, v.Name ) : 0;
			bindings.Add( new ShaderBinding( v.Name, v.Set, v.Binding, kind, v.Count, v.Shape, annotation ) { Size = size } );

			if ( kind == BindingKind.Globals ) globals = Globals( root, v.Set, v.Binding, annotations );
		}

		return new CompiledStage
		{
			Stage = stage,
			Entry = entry,
			Spirv = spirv,
			Bindings = bindings,
			Globals = globals,
			Inputs = stage == StageKind.Vertex ? Inputs( root, entry, inputs ) : [],
			Specializations = Spirv.SpecializationIds( spirv )
				.Select( id => (Id: id, Name: VfxIncludes.SpecializationName( id )) )
				.Where( s => s.Name is not null )
				.Select( s => new SpecializationSlot( s.Id, s.Name, annotations.GetValueOrDefault( s.Name ) ) )
				.ToList(),
		};
	}

	/// <summary>A constant buffer's size as compiled (its element's uniform size), 0 when the reflection doesn't say.</summary>
	static int ConstantBufferSize( JsonElement root, string name )
	{
		foreach ( var parameter in root.GetProperty( "parameters" ).EnumerateArray() )
		{
			if ( parameter.GetProperty( "name" ).GetString() != name ) continue;
			if ( parameter.GetProperty( "type" ).TryGetProperty( "elementVarLayout", out var element ) && element.TryGetProperty( "binding", out var binding ) && binding.TryGetProperty( "size", out var size ) )
				return size.GetInt32();
		}

		return 0;
	}

	/// <summary>The program's uniform globals: every one keeps its offset in the block whether or not it's read.</summary>
	static GlobalConstants Globals( JsonElement root, int set, int binding, IReadOnlyDictionary<string, Annotations> annotations )
	{
		var members = new List<GlobalMember>();
		var size = 0;
		foreach ( var parameter in root.GetProperty( "parameters" ).EnumerateArray() )
		{
			if ( !Uniform( parameter, out var offset, out var bytes ) ) continue;
			var name = parameter.GetProperty( "name" ).GetString();
			size = Math.Max( size, offset + bytes );
			if ( Numeric( parameter.GetProperty( "type" ) ) is not { } numeric ) continue;
			annotations.TryGetValue( name, out var annotation );
			members.Add( new GlobalMember( name, offset, numeric.Kind, numeric.Components, annotation ) );
		}

		return new GlobalConstants( set, binding, (size + 15) & ~15, members );
	}

	static bool Uniform( JsonElement parameter, out int offset, out int size )
	{
		offset = size = 0;
		foreach ( var binding in Bindings( parameter ) )
		{
			if ( binding.GetProperty( "kind" ).GetString() != "uniform" ) continue;
			offset = binding.TryGetProperty( "offset", out var o ) ? o.GetInt32() : 0;
			size = binding.TryGetProperty( "size", out var s ) ? s.GetInt32() : 0;
			return true;
		}

		return false;
	}

	static IEnumerable<JsonElement> Bindings( JsonElement parameter )
	{
		if ( parameter.TryGetProperty( "binding", out var one ) ) yield return one;
		if ( parameter.TryGetProperty( "bindings", out var many ) )
		{
			foreach ( var b in many.EnumerateArray() ) yield return b;
		}
	}

	static (ScalarKind Kind, int Components)? Numeric( JsonElement type )
	{
		switch ( type.GetProperty( "kind" ).GetString() )
		{
			case "scalar":
				return Scalar( type ) is { } s ? (s, 1) : null;
			case "vector":
				return Scalar( type.GetProperty( "elementType" ) ) is { } v ? (v, type.GetProperty( "elementCount" ).GetInt32()) : null;
			case "matrix":
				return Scalar( type.GetProperty( "elementType" ) ) is { } m ? (m, type.GetProperty( "rowCount" ).GetInt32() * type.GetProperty( "columnCount" ).GetInt32()) : null;
			default:
				return null;
		}
	}

	static ScalarKind? Scalar( JsonElement type ) => type.TryGetProperty( "scalarType", out var s ) ? s.GetString() switch
	{
		"float32" => ScalarKind.Float,
		"int32" => ScalarKind.Int,
		"uint32" => ScalarKind.UInt,
		"bool" => ScalarKind.Bool,
		_ => null,
	} : null;

	/// <summary>The vertex inputs the SPIR-V reads, each with the semantic its field was declared with.</summary>
	static List<VertexInputSlot> Inputs( JsonElement root, string entry, List<int> locations )
	{
		var byLocation = new Dictionary<int, VertexInputSlot>();
		foreach ( var point in root.GetProperty( "entryPoints" ).EnumerateArray() )
		{
			if ( point.GetProperty( "name" ).GetString() != entry ) continue;
			foreach ( var parameter in point.GetProperty( "parameters" ).EnumerateArray() ) Collect( parameter, byLocation );
		}

		var result = new List<VertexInputSlot>();
		foreach ( var location in locations )
		{
			if ( byLocation.TryGetValue( location, out var slot ) ) result.Add( slot );
		}

		return result;
	}

	// A struct's fields' locations are relative to the struct's own, so a second struct parameter's start at its offset
	static void Collect( JsonElement variable, Dictionary<int, VertexInputSlot> into, int baseLocation = 0, string parent = null )
	{
		var type = variable.GetProperty( "type" );
		var name = variable.TryGetProperty( "name", out var n ) ? n.GetString() : null;
		var path = parent is null ? name : $"{parent}.{name}";
		var location = -1;
		foreach ( var binding in Bindings( variable ) )
		{
			if ( binding.GetProperty( "kind" ).GetString() == "varyingInput" ) location = baseLocation + binding.GetProperty( "index" ).GetInt32();
		}

		if ( type.GetProperty( "kind" ).GetString() == "struct" && type.TryGetProperty( "fields", out var fields ) )
		{
			foreach ( var field in fields.EnumerateArray() ) Collect( field, into, Math.Max( location, baseLocation ), path );
			return;
		}

		if ( !variable.TryGetProperty( "semanticName", out var semantic ) ) return;
		if ( location < 0 || Numeric( type ) is not { } numeric ) return;
		var index = variable.TryGetProperty( "semanticIndex", out var i ) ? i.GetInt32() : 0;
		into[location] = new VertexInputSlot( location, semantic.GetString().ToUpperInvariant(), index, numeric.Kind, numeric.Components ) { Field = path };
	}
}
