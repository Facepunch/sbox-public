using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

public class SceneDynamicObject : SceneObject
{
	internal CDynamicSceneObject managedNative;

	/// <summary>
	/// The vertices as native holds them, kept in managed code when the process draws on a backend other than Source 2's
	/// (Borealis.Gpu draws them from here). Null on Source 2.
	/// </summary>
	internal List<Vertex> ManagedVertices { get; }

	/// <summary>What the vertices are, as last begun.</summary>
	internal Graphics.PrimitiveType ManagedPrimitiveType { get; private set; }

	/// <summary>The material set last, for a backend other than Source 2's.</summary>
	internal Material ManagedMaterial { get; private set; }

	public SceneDynamicObject( SceneWorld sceneWorld )
	{
		Assert.IsValid( sceneWorld );

		if ( RenderBackend.Default is not Source2RenderBackend ) ManagedVertices = new List<Vertex>();

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CDynamicSceneObject.Create( sceneWorld );

			if ( native.IsValid )
			{
				// Start off with infinite bounds so it will actually get rendered
				// if the user forgets/doesn't need bounds.
				native.SetBoundsInfinite();
			}
		}
	}

	public Material Material
	{
		set
		{
			managedNative.Material = value?.native ?? default;
			if ( ManagedVertices is not null ) ManagedMaterial = value;
		}
	}

	internal unsafe override void OnNativeInit( CSceneObject ptr )
	{
		base.OnNativeInit( ptr );
		managedNative = (CDynamicSceneObject)ptr;
	}

	internal override void OnNativeDestroy()
	{
		managedNative = default;
		base.OnNativeDestroy();
	}

	public void Clear()
	{
		managedNative.Reset();
		ManagedVertices?.Clear();
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void AddVertex( in Vertex v )
	{
		managedNative.AddVertex( v );
		ManagedVertices?.Add( v );
	}

	public unsafe void AddVertex( in Span<Vertex> v )
	{
		fixed ( Vertex* ptr = &MemoryMarshal.GetReference( v ) )
		{
			AddVertex( ptr, v.Length );
		}
	}

	internal unsafe void AddVertex( in Vertex* ptr, int count )
	{
		managedNative.AddVertexRange( (IntPtr)ptr, count );
		ManagedVertices?.AddRange( new ReadOnlySpan<Vertex>( ptr, count ) );
	}

	public IDisposable Write( Graphics.PrimitiveType type, int vertices, int indices )
	{
		Begin( type, vertices );
		return null;
	}

	public void Init( Graphics.PrimitiveType type ) => Begin( type, 0 );

	// Native's Begin drops the vertices and sets the primitive type
	void Begin( Graphics.PrimitiveType type, int vertices )
	{
		managedNative.Begin( (RenderPrimitiveType)type, vertices );
		if ( ManagedVertices is null ) return;
		ManagedVertices.Clear();
		ManagedPrimitiveType = type;
	}
}

