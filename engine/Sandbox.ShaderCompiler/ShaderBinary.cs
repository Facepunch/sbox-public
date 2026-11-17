using System.IO;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// Compiled shaders as compact binary: what the disk cache (<see cref="CompileCache"/>, <see cref="PassCache"/>) and the
/// offline compiles (<see cref="SlangOutput"/>) hold. SPIR-V as bytes, everything else field by field; stages shared between a
/// shader's passes are written once. Paths aren't written: the reader gives them.
/// </summary>
internal static class ShaderBinary
{
	/// <summary>Raised with any change to what's written.</summary>
	public const int Format = 1;

	public static void WriteShader( BinaryWriter w, CompiledShader shader )
	{
		w.Write( (byte)shader.Tier );
		Str( w, shader.Name );
		Str( w, shader.Generated );
		Str( w, shader.GpuDrivenError );
		Layout( w, shader.Material );

		var stages = new List<CompiledStage>();
		int Index( CompiledStage stage )
		{
			if ( stage is null ) return -1;
			var at = stages.IndexOf( stage );
			if ( at >= 0 ) return at;
			stages.Add( stage );
			return stages.Count - 1;
		}

		var passes = shader.Passes.Select( p => (p.Key, Vertex: Index( p.Value.Vertex ), Pixel: Index( p.Value.Pixel )) ).ToList();
		w.Write( stages.Count );
		foreach ( var stage in stages ) Stage( w, stage );
		w.Write( passes.Count );
		foreach ( var (name, vertex, pixel) in passes )
		{
			Str( w, name );
			w.Write( vertex );
			w.Write( pixel );
		}
	}

	public static CompiledShader ReadShader( BinaryReader r, string path, IReadOnlyList<string> dependencies, bool offline )
	{
		var tier = (ShaderTier)r.ReadByte();
		var name = Str( r );
		var generated = Str( r );
		var gpuDrivenError = Str( r );
		var layout = Layout( r );
		var stages = new CompiledStage[r.ReadInt32()];
		for ( int i = 0; i < stages.Length; i++ ) stages[i] = Stage( r );
		var passes = new Dictionary<string, CompiledPass>();
		var count = r.ReadInt32();
		for ( int i = 0; i < count; i++ )
		{
			var pass = Str( r );
			var vertex = r.ReadInt32();
			var pixel = r.ReadInt32();
			passes[pass] = new CompiledPass { Name = pass, Vertex = vertex >= 0 ? stages[vertex] : null, Pixel = pixel >= 0 ? stages[pixel] : null };
		}

		return new CompiledShader
		{
			Name = name,
			Tier = tier,
			Path = path,
			Material = layout,
			Passes = passes,
			Generated = generated,
			GpuDrivenError = gpuDrivenError,
			Dependencies = dependencies,
			FromCache = true,
			Offline = offline,
		};
	}

	public static void WritePassStage( BinaryWriter w, CompiledPassStage stage )
	{
		Stage( w, stage.Stage );
		w.Write( stage.ThreadGroupSize.X );
		w.Write( stage.ThreadGroupSize.Y );
		w.Write( stage.ThreadGroupSize.Z );
	}

	public static CompiledPassStage ReadPassStage( BinaryReader r, IReadOnlyList<string> dependencies, bool offline )
	{
		var stage = Stage( r );
		return new CompiledPassStage { Stage = stage, ThreadGroupSize = (r.ReadInt32(), r.ReadInt32(), r.ReadInt32()), Dependencies = dependencies, FromCache = true, Offline = offline };
	}

	static void Stage( BinaryWriter w, CompiledStage stage )
	{
		w.Write( (byte)stage.Stage );
		Str( w, stage.Entry );
		w.Write( stage.Spirv.Length );
		w.Write( stage.Spirv );
		List( w, stage.Bindings, b =>
		{
			Str( w, b.Variable );
			w.Write( b.Set );
			w.Write( b.Binding );
			w.Write( (byte)b.Kind );
			w.Write( b.Count );
			w.Write( (byte)b.Shape );
			Annotations( w, b.Annotations );
			w.Write( b.Size );
		} );
		w.Write( stage.Globals is not null );
		if ( stage.Globals is { } g )
		{
			w.Write( g.Set );
			w.Write( g.Binding );
			w.Write( g.Size );
			List( w, g.Members, m =>
			{
				Str( w, m.Name );
				w.Write( m.Offset );
				w.Write( (byte)m.Kind );
				w.Write( m.Components );
				Annotations( w, m.Annotations );
			} );
		}

		List( w, stage.Inputs, i =>
		{
			w.Write( i.Location );
			Str( w, i.Semantic );
			w.Write( i.SemanticIndex );
			w.Write( (byte)i.Kind );
			w.Write( i.Components );
			Str( w, i.Field );
		} );
		List( w, stage.Specializations, s =>
		{
			w.Write( s.Id );
			Str( w, s.Name );
			Annotations( w, s.Annotations );
		} );
	}

	static CompiledStage Stage( BinaryReader r )
	{
		var kind = (StageKind)r.ReadByte();
		var entry = Str( r );
		var spirv = r.ReadBytes( r.ReadInt32() );
		var bindings = List( r, () => new ShaderBinding( Str( r ), r.ReadInt32(), r.ReadInt32(), (BindingKind)r.ReadByte(), r.ReadInt32(), (TextureShape)r.ReadByte(), Annotations( r ) ) { Size = r.ReadInt32() } );
		GlobalConstants globals = null;
		if ( r.ReadBoolean() )
		{
			var (set, binding, size) = (r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
			globals = new GlobalConstants( set, binding, size, List( r, () => new GlobalMember( Str( r ), r.ReadInt32(), (ScalarKind)r.ReadByte(), r.ReadInt32(), Annotations( r ) ) ) );
		}

		var inputs = List( r, () => new VertexInputSlot( r.ReadInt32(), Str( r ), r.ReadInt32(), (ScalarKind)r.ReadByte(), r.ReadInt32() ) { Field = Str( r ) } );
		var specializations = List( r, () => new SpecializationSlot( r.ReadInt32(), Str( r ), Annotations( r ) ) );
		return new CompiledStage { Stage = kind, Entry = entry, Spirv = spirv, Bindings = bindings, Globals = globals, Inputs = inputs, Specializations = specializations };
	}

	static void Layout( BinaryWriter w, MaterialLayout layout )
	{
		w.Write( layout is not null );
		if ( layout is null ) return;
		Str( w, layout.Type );
		w.Write( layout.Size );
		var state = layout.RenderState;
		w.Write( state is not null );
		if ( state is not null )
		{
			w.Write( (byte)state.Blend );
			w.Write( (sbyte)(state.DepthWrite is { } depthWrite ? (depthWrite ? 1 : 0) : -1) );
			w.Write( state.DepthTest );
			w.Write( state.DepthBias );
			w.Write( state.SlopeDepthBias );
			w.Write( state.TwoSided );
			w.Write( state.AlphaTest );
			w.Write( state.Translucent );
		}

		List( w, layout.Fields, f =>
		{
			Str( w, f.Name );
			w.Write( (byte)f.Kind );
			w.Write( f.Components );
			w.Write( f.Offset );
			Floats( w, f.Default );
			Str( w, f.Group );
			OptionalFloat( w, f.Min );
			OptionalFloat( w, f.Max );
			w.Write( f.Srgb );
			w.Write( f.Opacity );
			Str( w, f.Source );
			w.Write( f.Specialize );
			List( w, f.RenderStates, s =>
			{
				w.Write( (byte)s.State );
				w.Write( s.Values is not null );
				if ( s.Values is null ) return;
				w.Write( s.Values.Length );
				foreach ( var v in s.Values ) w.Write( v );
			} );
		} );
	}

	static MaterialLayout Layout( BinaryReader r )
	{
		if ( !r.ReadBoolean() ) return null;
		var type = Str( r );
		var size = r.ReadInt32();
		SurfaceRenderDefaults state = null;
		if ( r.ReadBoolean() )
		{
			var blend = (SurfaceBlend)r.ReadByte();
			var depthWrite = r.ReadSByte();
			state = new SurfaceRenderDefaults( blend, depthWrite < 0 ? null : depthWrite == 1, r.ReadBoolean(), r.ReadSingle(), r.ReadSingle(), r.ReadBoolean(), r.ReadBoolean(), r.ReadBoolean() );
		}

		var fields = List( r, () =>
		{
			var field = new MaterialField( Str( r ), (MaterialFieldKind)r.ReadByte(), r.ReadInt32(), r.ReadInt32(), Floats( r ), Str( r ), OptionalFloat( r ), OptionalFloat( r ), r.ReadBoolean() );
			var opacity = r.ReadBoolean();
			var source = Str( r );
			var specialize = r.ReadBoolean();
			var states = List( r, () =>
			{
				var kind = (SurfaceStateKind)r.ReadByte();
				int[] values = null;
				if ( r.ReadBoolean() )
				{
					values = new int[r.ReadInt32()];
					for ( int i = 0; i < values.Length; i++ ) values[i] = r.ReadInt32();
				}

				return new SurfaceStateRule( kind, values );
			} );
			return field with { Opacity = opacity, Source = source, Specialize = specialize, RenderStates = states };
		} );

		return new MaterialLayout { Type = type, Size = size, Fields = fields, RenderState = state ?? SurfaceRenderDefaults.None };
	}

	static void Annotations( BinaryWriter w, Annotations a )
	{
		w.Write( a is not null );
		if ( a is null ) return;
		Str( w, a.Attribute );
		Floats( w, a.Default );
		w.Write( a.SrgbRead );
		Str( w, a.Filter );
		Str( w, a.AddressU );
		Str( w, a.AddressV );
		Str( w, a.AddressW );
		w.Write( a.MaxAniso );
		Str( w, a.Comparison );
		w.Write( a.Specialized );
		Str( w, a.Source );
		Str( w, a.SourceArg );
	}

	static Annotations Annotations( BinaryReader r )
	{
		if ( !r.ReadBoolean() ) return null;
		return new Annotations
		{
			Attribute = Str( r ),
			Default = Floats( r ),
			SrgbRead = r.ReadBoolean(),
			Filter = Str( r ),
			AddressU = Str( r ),
			AddressV = Str( r ),
			AddressW = Str( r ),
			MaxAniso = r.ReadInt32(),
			Comparison = Str( r ),
			Specialized = r.ReadBoolean(),
			Source = Str( r ),
			SourceArg = Str( r ),
		};
	}

	static void List<T>( BinaryWriter w, IReadOnlyList<T> items, Action<T> write )
	{
		w.Write( items is null ? -1 : items.Count );
		foreach ( var item in items ?? [] ) write( item );
	}

	static T[] List<T>( BinaryReader r, Func<T> read )
	{
		var count = r.ReadInt32();
		if ( count < 0 ) return null;
		var items = new T[count];
		for ( int i = 0; i < count; i++ ) items[i] = read();
		return items;
	}

	static void Floats( BinaryWriter w, float[] values )
	{
		w.Write( values is null ? -1 : values.Length );
		foreach ( var v in values ?? [] ) w.Write( v );
	}

	static float[] Floats( BinaryReader r )
	{
		var count = r.ReadInt32();
		if ( count < 0 ) return null;
		var values = new float[count];
		for ( int i = 0; i < count; i++ ) values[i] = r.ReadSingle();
		return values;
	}

	static void OptionalFloat( BinaryWriter w, float? value )
	{
		w.Write( value.HasValue );
		if ( value is { } v ) w.Write( v );
	}

	static float? OptionalFloat( BinaryReader r ) => r.ReadBoolean() ? r.ReadSingle() : null;

	static void Str( BinaryWriter w, string text )
	{
		w.Write( text is not null );
		if ( text is not null ) w.Write( text );
	}

	static string Str( BinaryReader r ) => r.ReadBoolean() ? r.ReadString() : null;
}
