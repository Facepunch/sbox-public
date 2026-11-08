using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

public class SceneDynamicObject : SceneObject
{
	internal CDynamicSceneObject managedNative;

	public SceneDynamicObject( SceneWorld sceneWorld )
	{
		Assert.IsValid( sceneWorld );

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
		set => managedNative.Material = value?.native ?? default;
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

	public void Clear() => managedNative.Reset();

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void AddVertex( in Vertex v ) => managedNative.AddVertex( v );

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
	}

	public IDisposable Write( Graphics.PrimitiveType type, int vertices, int indices )
	{
		ThrowIfUndefined( type );
		managedNative.Begin( (RenderPrimitiveType)type, vertices );
		return null;
	}

	public void Init( Graphics.PrimitiveType type )
	{
		ThrowIfUndefined( type );
		managedNative.Begin( (RenderPrimitiveType)type, 0 );
	}

	internal static void ThrowIfUndefined( Graphics.PrimitiveType type )
	{
		if ( !Enum.IsDefined( type ) )
			throw new ArgumentOutOfRangeException( nameof( type ), $"Unknown primitive type {(int)type}" );
	}
}

