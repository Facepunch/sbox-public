using Basalt;
using System;
using System.Collections.Generic;
using System.Threading;

namespace BasaltTests.Headless;

/// <summary>
/// A texture that's only its description.
/// </summary>
internal sealed class HeadlessTexture : GpuTexture
{
	static int nextIndex;

	public HeadlessTexture( in TextureDesc desc, string name )
	{
		Name = name;
		Width = desc.Width;
		Height = desc.Height;
		Depth = Math.Max( desc.Depth, 1 );
		Mips = Math.Max( desc.Mips, 1 );
		Format = desc.Format;
		Samples = Math.Max( desc.Samples, 1 );
		IsCube = desc.Cube;
		RenderTarget = desc.RenderTarget;
		Storage = desc.Storage;
		BindlessIndex = Interlocked.Increment( ref nextIndex );
	}

	public string Name { get; }
	public int Depth { get; }
	public bool RenderTarget { get; }
	public bool Storage { get; }
	public bool Disposed { get; private set; }

	public override int Width { get; }
	public override int Height { get; }
	public override int Mips { get; }
	public override TextureFormat Format { get; }
	public override int Samples { get; }
	public override bool IsCube { get; }
	public override bool IsArray => false;
	public override bool IsValid => !Disposed;
	public override int BindlessIndex { get; }
	public override Vector4 SheetData => default;

	public override int ViewIndex( bool srgb, TextureViewDimension dimension ) => BindlessIndex;
	public override void KeepResident() { }
	public override void Dispose() => Disposed = true;
	public override string ToString() => Name;
}

/// <summary>
/// A <see cref="GpuBuffer"/>'s device side, which holds nothing.
/// </summary>
internal sealed class HeadlessBuffer( int elementCount, int elementSize, GpuBuffer.UsageFlags usage, string name ) : IDisposable
{
	public string Name { get; } = name;
	public int ElementCount { get; } = elementCount;
	public int ElementSize { get; } = elementSize;
	public GpuBuffer.UsageFlags Usage { get; } = usage;
	public bool Disposed { get; private set; }

	/// <summary>
	/// What uploads put in it, made by the first.
	/// </summary>
	public byte[] Contents { get; private set; }

	public void Upload( ReadOnlySpan<byte> data, int offsetBytes )
	{
		Contents ??= new byte[ElementCount * ElementSize];
		data.CopyTo( Contents.AsSpan( offsetBytes ) );
	}

	public void Dispose() => Disposed = true;
	public override string ToString() => Name;
}

/// <summary>
/// Shader parameters as a plain dictionary of what was set.
/// </summary>
internal sealed class HeadlessParameters : ShaderParameters
{
	readonly Dictionary<uint, object> values = new();
	readonly Dictionary<uint, int> combos = new();

	/// <summary>
	/// Where values this doesn't have come from: a segment context's are its frame's.
	/// </summary>
	public HeadlessParameters Parent { get; set; }

	/// <summary>
	/// What <paramref name="name"/> was set to, here or in a parent, or null.
	/// </summary>
	public object Get( StringToken name )
	{
		lock ( values )
		{
			if ( values.TryGetValue( name.Value, out var value ) ) return value;
		}

		return Parent?.Get( name );
	}

	/// <summary>
	/// What combo <paramref name="name"/> was set to, here or in a parent, or null.
	/// </summary>
	public int? GetCombo( StringToken name )
	{
		lock ( values )
		{
			if ( combos.TryGetValue( name.Value, out var value ) ) return value;
		}

		return Parent?.GetCombo( name );
	}

	void Store( StringToken name, object value )
	{
		lock ( values ) values[name.Value] = value;
	}

	public override void Set( StringToken name, int value ) => Store( name, value );
	public override void Set( StringToken name, uint value ) => Store( name, value );
	public override void Set( StringToken name, float value ) => Store( name, value );
	public override void Set( StringToken name, bool value ) => Store( name, value );
	public override void Set( StringToken name, Vector2 value ) => Store( name, value );
	public override void Set( StringToken name, Vector3 value ) => Store( name, value );
	public override void Set( StringToken name, Vector4 value ) => Store( name, value );
	public override void Set( StringToken name, Vector2Int value ) => Store( name, value );
	public override void Set( StringToken name, Matrix value ) => Store( name, value );
	public override void Set( StringToken name, GpuTexture value, int mip = -1 ) => Store( name, value );
	public override void Set( StringToken name, GpuBuffer value ) => Store( name, value );
	public override void SetData<T>( StringToken name, in T value ) => Store( name, value );

	public override void SetCombo( StringToken name, int value )
	{
		lock ( values ) combos[name.Value] = value;
	}

	public override bool GetBool( StringToken name, bool fallback = false ) => Get( name ) is bool value ? value : fallback;

	public override void MergeTo( ShaderParameters target )
	{
		var into = (HeadlessParameters)target;
		if ( ReferenceEquals( into, this ) ) return;

		KeyValuePair<uint, object>[] copiedValues;
		KeyValuePair<uint, int>[] copiedCombos;
		lock ( values )
		{
			copiedValues = [.. values];
			copiedCombos = [.. combos];
		}

		lock ( into.values )
		{
			foreach ( var (key, value) in copiedValues ) into.values[key] = value;
			foreach ( var (key, value) in copiedCombos ) into.combos[key] = value;
		}
	}

	public override void Clear()
	{
		lock ( values )
		{
			values.Clear();
			combos.Clear();
		}
	}
}

/// <summary>
/// A material that's a name, a shader and the flags a test gives it.
/// </summary>
internal sealed class HeadlessMaterial( string name, string shader ) : GpuMaterial
{
	public string Shader { get; } = shader;
	public bool Disposed { get; private set; }

	public override string Name { get; } = name;
	public override bool IsTranslucent => Translucent;
	public override bool IsAlphaTest => AlphaTest;
	public override bool ReadsFrameBuffer => FrameBuffer;
	public override bool ShadowFastPath => FastPath;
	public override bool NeedsLightProbe => LightProbe;
	public override ulong SimilarityKey => (ulong)StringComparer.OrdinalIgnoreCase.GetHashCode( Shader ?? Name ) & 0xFFFFFFFF;

	public bool Translucent { get; init; }
	public bool AlphaTest { get; init; }
	public bool FrameBuffer { get; init; }
	public bool FastPath { get; init; }
	public bool LightProbe { get; init; } = true;

	public override void Dispose() => Disposed = true;
}

/// <summary>
/// A model's geometry, which is only a name: draws of it are recorded, not drawn.
/// </summary>
internal sealed class HeadlessMesh( string name ) : GpuMesh
{
	public override string Name { get; } = name;
}
