using Basalt.Source2.Resources;
using System.IO;

namespace Basalt.Source2.Shaders;

/// <summary>
/// A compiled s&amp;box shader (<c>.shader_c</c>): its modes, features and programs, read the way
/// <c>CVfx::CreateFromResourceFile</c> reads them. Static combos are decompressed on demand
/// (<see cref="ShaderProgram.LoadCombo"/>). See docs/managed/basalt-gpu.md.
/// </summary>
public sealed class ShaderFile
{
	/// <summary>Oldest VCS version read (<c>VFX_VCS_VERSION_MIN_SUPPORTED</c>).</summary>
	public const int MinVcsVersion = 65;

	/// <summary>Newest VCS version read (<c>VFX_VCS_VERSION</c>).</summary>
	public const int MaxVcsVersion = 68;

	/// <summary>The shader ABI epoch these bindings follow (<c>VFX_ABI_CURRENT_VERSION</c>).</summary>
	public const int AbiVersion = 2;

	/// <summary>Oldest resource version the engine loads (<c>VFX_SHADER_RESOURCE_VERSION</c>).</summary>
	public const int ResourceVersion = 4;

	/// <summary>A name for messages: the path it was loaded from, if any.</summary>
	public string Name { get; }

	/// <summary>The VCS version it was written with.</summary>
	public int VcsVersion { get; }

	/// <summary>The shader's <c>HEADER</c> version.</summary>
	public int Version { get; }

	/// <summary>Its modes.</summary>
	public IReadOnlyList<ShaderMode> Modes { get; }

	/// <summary>Programs by <see cref="ShaderProgramType"/>; null where it has none.</summary>
	public ShaderProgram[] Programs { get; } = new ShaderProgram[9];

	/// <summary>The features program, whose static combos are the material's <c>F_*</c> features.</summary>
	public ShaderProgram Features => Programs[(int)ShaderProgramType.Features];

	/// <summary>A program, or null.</summary>
	public ShaderProgram this[ShaderProgramType type] => Programs[(int)type];

	ShaderFile( string name, byte[] data )
	{
		Name = name;
		var resource = ResourceFile.Read( data );
		if ( resource.ResourceVersion < ResourceVersion ) throw new InvalidDataException( $"{name}: shader resource version {resource.ResourceVersion}, the engine needs {ResourceVersion}" );

		// The header and programs are in SPRV; resourcecompiler's files had them in DATA
		var (block, size) = resource.Has( "SPRV" ) && resource.Find( "SPRV" ).Size > 0 ? resource.Find( "SPRV" ) : resource.Find( "DATA" );
		var header = new ByteReader( data, block, block + size );
		var offsets = new (int Offset, int Size)[9];
		for ( int i = 0; i < offsets.Length; i++ ) offsets[i] = (header.Int(), header.Int());

		var programCount = 7;
		Modes = [];
		for ( int type = 0; type < offsets.Length; type++ )
		{
			if ( offsets[type].Offset == 0 ) continue;

			var start = block + offsets[type].Offset;
			var reader = new ByteReader( data, start );
			if ( reader.Fixed( 4 ) is var magic && magic != "vcs2" ) throw new InvalidDataException( $"{name}: program {(ShaderProgramType)type} isn't VCS ({magic})" );

			var vcs = reader.Int();
			if ( vcs < MinVcsVersion || vcs > MaxVcsVersion ) throw new InvalidDataException( $"{name}: VCS version {vcs}, can read {MinVcsVersion}..{MaxVcsVersion}" );
			var abi = reader.Int();
			if ( abi != AbiVersion ) throw new InvalidDataException( $"{name}: shader ABI epoch {abi}, expected {AbiVersion}; it needs recompiling" );
			VcsVersion = vcs;

			var serialized = reader.UInt();
			if ( (serialized & 1) != 0 ) programCount = Math.Max( programCount, 8 );
			if ( (serialized & 2) != 0 ) programCount = Math.Max( programCount, 9 );

			if ( type == (int)ShaderProgramType.Features )
			{
				Version = reader.Int();
				reader.Skip( reader.Int() + 1 );
				reader.Int();
				for ( int i = 0; i < programCount; i++ ) reader.Int();

				var modes = new ShaderMode[reader.Count( 64 )];
				for ( int i = 0; i < modes.Length; i++ )
				{
					var modeName = reader.Fixed( 64 );
					var fallback = reader.Fixed( 64 );
					var settings = new (string, int)[reader.Count( 64 )];
					for ( int j = 0; j < settings.Length; j++ ) settings[j] = (reader.Fixed( 64 ), reader.Int());
					modes[i] = new ShaderMode { Name = modeName, Fallback = fallback, Settings = settings };
				}

				Modes = modes;
			}

			Programs[type] = new ShaderProgram( this, (ShaderProgramType)type, reader, start, vcs, programCount );
		}
	}

	/// <summary>Read a shader from its <c>.shader_c</c> bytes.</summary>
	public static ShaderFile Read( byte[] data, string name = null ) => new( name ?? "shader", data );

	/// <summary>Read a <c>.shader_c</c> from disk.</summary>
	public static ShaderFile Load( string path ) => new( path, File.ReadAllBytes( path ) );

	/// <summary>A mode by name (case-insensitive), or null.</summary>
	public ShaderMode Mode( string name )
	{
		foreach ( var mode in Modes )
		{
			if ( string.Equals( mode.Name, name, StringComparison.OrdinalIgnoreCase ) ) return mode;
		}

		return null;
	}

	/// <summary>The index of a feature (<c>F_*</c>), or -1.</summary>
	public int FeatureIndex( string name )
	{
		var features = Features?.StaticCombos ?? [];
		for ( int i = 0; i < features.Length; i++ )
		{
			if ( string.Equals( features[i].Name, name, StringComparison.OrdinalIgnoreCase ) ) return i;
		}

		return -1;
	}

	/// <summary>
	/// The static combo id of <paramref name="program"/> for a mode and the material's feature values, as
	/// <c>CMaterialLayer::ComputeStaticComboValuesForPerLayerProgramData</c> computes it: mode-set combos from the mode,
	/// feature combos from <paramref name="features"/> (by feature index; missing ones are 0), system combos from
	/// <paramref name="system"/> (by name; <c>S_SHADER_QUALITY</c> and <c>S_SUPPORTS_CLIP_DISTANCE</c> default to 1).
	/// Values are clamped to each combo's range.
	/// </summary>
	public ulong StaticComboId( ShaderProgram program, ShaderMode mode, ReadOnlySpan<int> features, Func<string, int?> system = null )
	{
		ulong id = 0;
		foreach ( var combo in program.StaticCombos )
		{
			int value;
			if ( combo.Source == 0 && combo.FeatureIndex < 0 )
			{
				value = 0;
				foreach ( var (name, setting) in mode?.Settings ?? [] )
				{
					if ( string.Equals( name, combo.Name, StringComparison.OrdinalIgnoreCase ) ) value = setting;
				}
			}
			else if ( combo.Source == 0 )
			{
				value = combo.FeatureIndex < features.Length ? features[combo.FeatureIndex] : 0;
			}
			else
			{
				value = system?.Invoke( combo.Name ) ?? SystemDefault( combo.Name );
			}

			value = Math.Clamp( value, combo.Min, combo.Max );
			id += (ulong)(value - combo.Min) * combo.Stride;
		}

		return id;
	}

	static int SystemDefault( string combo ) => combo switch
	{
		"S_SHADER_QUALITY" => 1,
		"S_SUPPORTS_CLIP_DISTANCE" => 1,
		_ => 0,
	};

	/// <summary>
	/// The dynamic combo id of <paramref name="program"/>: each <c>D_*</c> combo's value from <paramref name="values"/>
	/// (render attributes, by name), or the feature it follows when it has one, else 0.
	/// </summary>
	public static ulong DynamicComboId( ShaderProgram program, Func<string, int?> values, ReadOnlySpan<int> features = default )
	{
		ulong id = 0;
		foreach ( var combo in program.DynamicCombos )
		{
			var value = values?.Invoke( combo.Name ) ?? (combo.FeatureIndex >= 0 && combo.FeatureIndex < features.Length ? features[combo.FeatureIndex] : 0);
			value = Math.Clamp( value, combo.Min, combo.Max );
			id += (ulong)(value - combo.Min) * combo.Stride;
		}

		return id;
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
