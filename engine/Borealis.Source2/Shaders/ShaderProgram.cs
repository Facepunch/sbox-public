using Borealis.Gpu;
using Borealis.Source2.Compression;
using Borealis.Source2.Resources;
using System.IO;

namespace Borealis.Source2.Shaders;

/// <summary>
/// One program of a shader (<c>CVfxProgramData</c>): its combos and rules, the variables it reads, the constant buffer
/// layouts and vertex input signatures it was compiled against, and its static combos, decompressed on demand.
/// </summary>
public sealed class ShaderProgram
{
	readonly byte[] data;
	readonly int start;
	readonly ulong[] comboIds;
	readonly uint[] comboOffsets;
	readonly SharedModuleGroup[] sharedGroups;
	readonly (int Group, int Offset, int Size)[] sharedModules;
	readonly Dictionary<ulong, StaticCombo> combos = new();
	readonly Dictionary<int, ShaderBytecode> resolvedShared = new();

	/// <summary>The shader it's part of.</summary>
	public ShaderFile Shader { get; }

	/// <summary>Which program it is.</summary>
	public ShaderProgramType Type { get; }

	/// <summary>The VCS version it was written with.</summary>
	public int VcsVersion { get; }

	/// <summary>Static combos, in id order (the first varies fastest).</summary>
	public ShaderCombo[] StaticCombos { get; }

	/// <summary>Dynamic combos, in id order.</summary>
	public ShaderCombo[] DynamicCombos { get; }

	/// <summary>
	/// The rules over its static combos: the features program's are the shader's feature rules (<c>FeatureRule</c>), which
	/// a material's feature values go through (<see cref="ShaderRule.Apply"/>).
	/// </summary>
	public ShaderRule[] StaticRules { get; } = [];

	/// <summary>Every variable the program can read; <see cref="VariableBinding.Variable"/> indexes it.</summary>
	public ShaderVariable[] Variables { get; }

	/// <summary>Constant buffers the engine fills, by name.</summary>
	public ConstantBufferLayout[] ConstantBuffers { get; }

	/// <summary>Vertex input signatures (vertex shaders); a static combo names one per dynamic combo.</summary>
	public InputElement[][] InputSignatures { get; }

	/// <summary>How many static combos survived the rules and were compiled.</summary>
	public int ComboCount => comboIds.Length;

	/// <summary>The ids of the compiled static combos, ascending.</summary>
	public ReadOnlySpan<ulong> ComboIds => comboIds;

	/// <summary>How many dynamic combos each static combo has before rules (the product of their ranges).</summary>
	public int DynamicComboCount { get; }

	internal ShaderProgram( ShaderFile shader, ShaderProgramType type, ByteReader reader, int start, int vcs, int programCount )
	{
		Shader = shader;
		Type = type;
		VcsVersion = vcs;
		data = reader.Data;
		this.start = start;

		reader.Skip( 16 * (type == ShaderProgramType.Features ? programCount : 1) );
		reader.Skip( 16 );
		reader.Int();

		StaticCombos = ReadCombos( reader, vcs );
		StaticRules = ReadRules( reader );
		DynamicCombos = ReadCombos( reader, vcs );
		SkipRules( reader );

		Variables = new ShaderVariable[reader.Count( 1000 )];
		for ( int i = 0; i < Variables.Length; i++ ) Variables[i] = ReadVariable( reader, vcs );

		var processors = reader.Count( 1000 );
		reader.Skip( processors * (4 + 16 + 4 + 256) );

		ConstantBuffers = new ConstantBufferLayout[reader.Count( 128 )];
		for ( int i = 0; i < ConstantBuffers.Length; i++ )
		{
			var name = reader.Fixed( 64 );
			var size = (int)(reader.UInt() & 0x7FFF);
			reader.Int();
			var members = new (string, int, int, int, int)[reader.Count( 4096 )];
			// Offsets are stored in 4-byte units
			for ( int j = 0; j < members.Length; j++ ) members[j] = (reader.Fixed( 64 ), reader.Int() * 4, reader.Int(), reader.Int(), reader.Int());
			reader.UInt();
			ConstantBuffers[i] = new ConstantBufferLayout { Name = name, Size = size, Members = members };
		}

		InputSignatures = [];
		if ( type is ShaderProgramType.Vertex or ShaderProgramType.Features )
		{
			InputSignatures = new InputElement[reader.Count( 500 )][];
			for ( int i = 0; i < InputSignatures.Length; i++ )
			{
				var elements = new InputElement[reader.Count( 64 )];
				for ( int j = 0; j < elements.Length; j++ ) elements[j] = new InputElement( reader.Terminated(), reader.Terminated(), reader.Terminated(), reader.Int() );
				InputSignatures[i] = elements;
			}
		}

		var count = reader.Count( 10_000_000 );
		var groups = new List<SharedModuleGroup>();
		var modules = new List<(int, int, int)>();
		if ( type != ShaderProgramType.Features && vcs >= 67 )
		{
			var moduleCount = reader.UInt();
			if ( moduleCount > 1_000_000 ) throw new InvalidDataException( $"{shader.Name}: {moduleCount} shared modules" );
			while ( modules.Count < moduleCount )
			{
				var groupCount = (int)reader.UInt();
				var rawSize = (int)reader.UInt();
				var packedSize = (int)reader.UInt();
				if ( groupCount is <= 0 or > 16 ) throw new InvalidDataException( $"{shader.Name}: shared module group of {groupCount}" );

				var offset = 0;
				for ( int j = 0; j < groupCount; j++ )
				{
					var size = (int)reader.UInt();
					modules.Add( (groups.Count, offset, size) );
					offset += size;
				}

				if ( offset != rawSize ) throw new InvalidDataException( $"{shader.Name}: shared module sizes don't add up" );
				groups.Add( new SharedModuleGroup( reader.Position, packedSize, rawSize ) );
				reader.Skip( packedSize );
			}
		}

		sharedGroups = [.. groups];
		sharedModules = [.. modules];

		comboIds = new ulong[count];
		for ( int i = 0; i < count; i++ ) comboIds[i] = reader.ULong();
		comboOffsets = new uint[count == 0 ? 0 : count + 1];
		for ( int i = 0; i < comboOffsets.Length; i++ ) comboOffsets[i] = reader.UInt();

		var dynamicCount = 1L;
		foreach ( var combo in DynamicCombos ) dynamicCount *= combo.Count;
		DynamicComboCount = (int)dynamicCount;
	}

	/// <summary>
	/// A program for a stage compiled from a module (<see cref="IShaderModules"/>): its variables and constant buffers, no
	/// combos and no compiled static combos.
	/// </summary>
	internal ShaderProgram( ShaderFile shader, ShaderProgramType type, ShaderVariable[] variables, ConstantBufferLayout[] constantBuffers )
	{
		Shader = shader;
		Type = type;
		data = [];
		comboIds = [];
		comboOffsets = [];
		sharedGroups = [];
		sharedModules = [];
		StaticCombos = [];
		DynamicCombos = [];
		Variables = variables;
		ConstantBuffers = constantBuffers;
		InputSignatures = [];
		DynamicComboCount = 1;
	}

	/// <summary>
	/// A program a shader with only a module declares (<see cref="ShaderFile.FromModule"/>): its combos, no variables and no
	/// compiled static combos. Strides are set here, in order.
	/// </summary>
	internal ShaderProgram( ShaderFile shader, ShaderProgramType type, ShaderCombo[] staticCombos, ShaderCombo[] dynamicCombos )
		: this( shader, type, Array.Empty<ShaderVariable>(), Array.Empty<ConstantBufferLayout>() )
	{
		StaticCombos = WithStrides( staticCombos );
		DynamicCombos = WithStrides( dynamicCombos );
		var count = 1L;
		foreach ( var combo in DynamicCombos ) count *= combo.Count;
		DynamicComboCount = (int)count;
	}

	static ShaderCombo[] WithStrides( ShaderCombo[] combos )
	{
		ulong stride = 1;
		foreach ( var combo in combos )
		{
			combo.Stride = stride;
			stride *= (ulong)combo.Count;
		}

		return combos;
	}

	static ShaderCombo[] ReadCombos( ByteReader reader, int vcs )
	{
		var combos = new ShaderCombo[reader.Count( 1000 )];
		ulong stride = 1;
		for ( int i = 0; i < combos.Length; i++ )
		{
			var name = reader.Fixed( 64 );
			var group = reader.Fixed( 64 );
			reader.Int();
			var specialization = vcs >= 67 && reader.Int() != 0;
			var min = (int)reader.UInt();
			var max = (int)reader.UInt();
			var source = reader.Int();
			var feature = (short)reader.Int();
			if ( vcs >= 66 )
			{
				reader.Int();
				reader.Int();
			}

			var strings = new string[reader.Count( 1000 )];
			for ( int j = 0; j < strings.Length; j++ ) strings[j] = reader.Terminated();

			combos[i] = new ShaderCombo
			{
				Name = name,
				Group = group,
				Specialization = specialization,
				Min = min,
				Max = max,
				Source = source,
				FeatureIndex = feature,
				Strings = strings,
				Stride = stride,
			};

			// FixupComboArray: each combo's stride is the product of the ranges before it
			stride *= (ulong)combos[i].Count;
		}

		return combos;
	}

	static void SkipRules( ByteReader reader )
	{
		var count = reader.Count( 1000 );
		reader.Skip( count * (4 + 4 + 16 + 16 * 4 * 3 + 256) );
	}

	// CVfxRule as written: the rule and its type, each argument's type (a byte), index and value, the rule's extra data
	// (AllowN's allowed count and argument count, Requires' required count and argument count), and its error text
	static ShaderRule[] ReadRules( ByteReader reader )
	{
		var rules = new ShaderRule[reader.Count( 1000 )];
		for ( int i = 0; i < rules.Length; i++ )
		{
			var rule = (ShaderRuleKind)reader.Int();
			var type = (ShaderRuleType)reader.Int();
			var argTypes = reader.Bytes( 16 );
			var indices = new int[16];
			var values = new int[16];
			var extra = new int[16];
			for ( int a = 0; a < 16; a++ ) indices[a] = reader.Int();
			for ( int a = 0; a < 16; a++ ) values[a] = reader.Int();
			for ( int a = 0; a < 16; a++ ) extra[a] = reader.Int();
			reader.Skip( 256 );
			rules[i] = new ShaderRule( rule, type, Array.ConvertAll( argTypes, t => (ShaderRuleType)t ), indices, values, extra );
		}

		return rules;
	}

	static ShaderVariable ReadVariable( ByteReader reader, int vcs )
	{
		if ( vcs >= 68 )
		{
			var activeIf = (int)reader.UInt();
			if ( activeIf > 256 ) throw new InvalidDataException( $"ActiveIf of {activeIf} bytes" );
			reader.Skip( activeIf );
		}

		var name = reader.Fixed( 64 );
		reader.Fixed( 64 );
		reader.Int();
		reader.Float();
		var attribute = reader.Fixed( 64 );
		var source = (VariableSource)reader.Int();
		byte[] expression = null;
		if ( source is VariableSource.Expression or VariableSource.MaterialAndExpression ) expression = reader.Bytes( (int)reader.UInt() );

		var sourceIndex = reader.Int();
		var type = (VariableType)reader.Int();
		var register = (RegisterType)reader.Int();
		reader.UInt();
		var elements = reader.Int();
		var renderState = reader.Int();
		var defaultTexture = reader.Fixed( 64 );
		var intDefault = new[] { reader.Int(), reader.Int(), reader.Int(), reader.Int() };
		reader.Skip( 32 );
		var floatDefault = new[] { reader.Float(), reader.Float(), reader.Float(), reader.Float() };
		reader.Skip( 32 );
		reader.Skip( 4 + 4 + 16 + 4 + 4 + 32 + 32 );

		return new ShaderVariable
		{
			Name = name,
			Attribute = attribute,
			Source = source,
			SourceIndex = sourceIndex,
			Type = type,
			Register = register,
			RegisterElements = elements,
			RenderStateId = renderState,
			DefaultTexture = defaultTexture,
			IntDefault = intDefault,
			FloatDefault = floatDefault,
			Expression = expression,
		};
	}

	/// <summary>Whether static combo <paramref name="id"/> was compiled (rules skip some).</summary>
	public bool HasCombo( ulong id ) => Array.BinarySearch( comboIds, id ) >= 0;

	/// <summary>
	/// Decompress and read static combo <paramref name="id"/>; cached. Throws if rules skipped it.
	/// </summary>
	public StaticCombo LoadCombo( ulong id )
	{
		lock ( combos )
		{
			if ( combos.TryGetValue( id, out var cached ) ) return cached;
		}

		var index = Array.BinarySearch( comboIds, id );
		if ( index < 0 ) throw new KeyNotFoundException( $"{Shader.Name}: {Type} has no static combo {id}" );

		var blob = Decompress( data, start + (int)comboOffsets[index], Type == ShaderProgramType.Features );
		var combo = new StaticCombo( this, id, blob );
		lock ( combos )
		{
			if ( combos.TryGetValue( id, out var cached ) ) return cached;
			combos.Add( id, combo );
		}
		return combo;
	}

	/// <summary>
	/// Static combo <paramref name="id"/>'s variables and render states without its shaders (each <see cref="DynamicCombo.Shader"/>
	/// null): the shared modules they reference aren't decompressed. The loaded combo when there is one; not cached otherwise.
	/// Throws if rules skipped it.
	/// </summary>
	public StaticCombo LoadComboStates( ulong id )
	{
		lock ( combos )
		{
			if ( combos.TryGetValue( id, out var cached ) ) return cached;
		}

		var index = Array.BinarySearch( comboIds, id );
		if ( index < 0 ) throw new KeyNotFoundException( $"{Shader.Name}: {Type} has no static combo {id}" );
		return new StaticCombo( this, id, Decompress( data, start + (int)comboOffsets[index], Type == ShaderProgramType.Features ), shaders: false );
	}

	/// <summary>
	/// <c>CVfxStaticComboData::Decompress</c>: <c>-compression, size, compressed size</c>, then the data.
	/// </summary>
	static byte[] Decompress( byte[] data, int offset, bool features )
	{
		var reader = new ByteReader( data, offset );
		var token = reader.Int();
		int compression, size, packed;
		if ( token < 0 )
		{
			compression = -token;
			size = reader.Int();
			packed = reader.Int();
		}
		else
		{
			if ( !features ) throw new InvalidDataException( "LZMA-compressed static combo (VCS too old)" );
			compression = 1;
			packed = size = token;
		}

		var source = reader.Span( packed );
		return compression switch
		{
			1 => source.ToArray(),
			2 => Zstd.Decompress( source, size ),
			3 => Zstd.Decompress( source, size, ZstdDictionary.Vfx ),
			4 => Lz4.Decompress( source, size ),
			_ => throw new InvalidDataException( $"Static combo compression {compression}" ),
		};
	}

	/// <summary>
	/// A shared module (VCS 67+) with <paramref name="constants"/> applied: the shader a reference stands for.
	/// </summary>
	internal ShaderBytecode SharedModule( int index, SpecializationConstant[] constants )
	{
		if ( (uint)index >= (uint)sharedModules.Length ) throw new InvalidDataException( $"{Shader.Name}: shared module {index} of {sharedModules.Length}" );

		ShaderBytecode module;
		lock ( resolvedShared )
		{
			if ( !resolvedShared.TryGetValue( index, out module ) )
			{
				var (group, offset, size) = sharedModules[index];
				var bytes = sharedGroups[group].Decompress( data ).AsSpan( offset, size ).ToArray();
				module = ShaderBytecode.Read( this, bytes );
				resolvedShared[index] = module;
			}
		}

		return constants.Length == 0 ? module : module.WithConstants( constants );
	}

	/// <summary>
	/// Up to 16 shared modules compressed together; decompressed once.
	/// </summary>
	sealed class SharedModuleGroup( int offset, int packedSize, int rawSize )
	{
		byte[] raw;

		public byte[] Decompress( byte[] data )
		{
			lock ( this )
			{
				return raw ??= Zstd.Decompress( data.AsSpan( offset, packedSize ), rawSize, ZstdDictionary.Vfx );
			}
		}
	}

	/// <inheritdoc/>
	public override string ToString() => $"{Shader.Name} {Type}";
}
