using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>A reusable gizmo batch with retained GPU geometry.</summary>
internal sealed class VertexSceneObject : SceneCustomObject
{
	internal readonly record struct RenderState( Graphics.PrimitiveType Type, Material Material, Texture Texture,
		bool IgnoreDepth, bool CullBackfaces, float LineThickness, Color Color );

	public List<Vertex> Vertices { get; } = new();
	public RenderState State { get; set; }
	public Graphics.PrimitiveType PrimitiveType => State.Type;
	RenderState _appliedState;
	GpuBuffer<Vertex> _buffer;
	Vertex[] _uploaded = [];
	int _count;
	internal int UploadCount { get; private set; }
	internal int LastUsedFrame { get; set; }
	bool _visible = true;

	public VertexSceneObject( SceneWorld world ) : base( world )
	{
		Bounds = BBox.FromPositionAndSize( 0, float.MaxValue );
		Batchable = false;
	}

	public void Write()
	{
		var vertices = CollectionsMarshal.AsSpan( Vertices );
		SetVisible( !vertices.IsEmpty );
		if ( vertices.IsEmpty || !Graphics.IsAvailable )
		{
			_count = 0;
			Vertices.Clear();
			return;
		}

		var recreate = !_buffer.IsValid() || _buffer.ElementCount < vertices.Length;
		var changed = recreate || _count != vertices.Length
			|| !MemoryMarshal.AsBytes( vertices ).SequenceEqual( MemoryMarshal.AsBytes( _uploaded.AsSpan( 0, vertices.Length ) ) );
		if ( recreate )
		{
			_buffer?.Dispose();
			var capacity = checked(vertices.Length + Math.Min( vertices.Length / 4, 1024 ));
			_buffer = new GpuBuffer<Vertex>( capacity, GpuBuffer.UsageFlags.Vertex );
			_uploaded = new Vertex[capacity];
		}
		if ( changed )
		{
			vertices.CopyTo( _uploaded );
			_buffer.SetData( vertices );
			UploadCount++;
		}

		if ( recreate || _appliedState != State )
		{
			ColorTint = State.Color;
			RenderLayer = State.IgnoreDepth ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.OverlayWithDepth;
			var opaque = !State.IgnoreDepth && State.Type != Graphics.PrimitiveType.Points;
			Flags.IsOpaque = opaque;
			Flags.IsTranslucent = !opaque;
			Flags.CastShadows = false;
			Attributes.Set( "LineThickness", State.LineThickness );
			Attributes.Set( "TextureColor", State.Texture ?? Texture.White );
			Attributes.SetCombo( "D_NO_ZTEST", State.IgnoreDepth ? 1 : 0 );
			Attributes.SetCombo( "D_NO_CULLING", State.CullBackfaces ? 0 : 1 );
			Attributes.SetCombo( "D_SNAP_TO_SCREEN_PIXELS", 0 );
			Attributes.SetCombo( "D_SHADED", 0 );
			Attributes.SetCombo( "D_DEPTH_BIAS", 1 );
			_appliedState = State;
		}
		_count = vertices.Length;
		Vertices.Clear();
	}

	internal void SetVisible( bool visible )
	{
		if ( _visible == visible ) return;
		_visible = visible;
		RenderingEnabled = visible;
	}

	public override void RenderSceneObject()
	{
		if ( _count > 0 && _buffer.IsValid() )
			Graphics.Draw( _buffer, State.Material, vertexCount: _count, attributes: Attributes, primitiveType: State.Type );
	}

	internal override void OnNativeDestroy()
	{
		_buffer?.Dispose();
		_buffer = null;
		base.OnNativeDestroy();
	}
}
