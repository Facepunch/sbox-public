using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sandbox;

partial class PhysicsWorld
{
	internal Scene Scene { get; set; }
	private DebugOverlaySystem DebugOverlay => Scene?.DebugOverlay;

	/// <summary>
	/// A SceneWorld where debug SceneObjects exist.
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public SceneWorld DebugSceneWorld
	{
		get => native.GetDebugScene();
		set => native.SetDebugScene( value );
	}

	/// <summary>
	/// Draws the physics debug overlays enabled via the physics_debug_draw convars,
	/// updating the SceneObjects in the <see cref="DebugSceneWorld"/>. Call once per tick or frame.
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public unsafe void DebugDraw()
	{
		if ( onDebugDrawFunctionPointer == DelegateFunctionPointer.Null )
			onDebugDrawFunctionPointer = DelegateFunctionPointer.Get<DebugDrawDelegate_t>( OnDebugDrawInternal );

		native.Draw( onDebugDrawFunctionPointer );
	}

	DelegateFunctionPointer onDebugDrawFunctionPointer;

	internal unsafe struct DebugDrawArgs
	{
		public DrawStringArgs* String;
		public DrawSegmentArgs* Segment;
		public DrawPointArgs* Point;
		public DrawBoundsArgs* Bounds;
		public DrawTransformArgs* Transform;
	}

	internal unsafe struct DrawStringArgs
	{
		public Vector3 Position;
		public sbyte* String;
		public Color Color;
	}

	internal unsafe struct DrawSegmentArgs
	{
		public Vector3 P1;
		public Vector3 P2;
		public Color Color;
	}

	internal unsafe struct DrawPointArgs
	{
		public Vector3 Position;
		public float Size;
		public Color Color;
	}

	internal unsafe struct DrawBoundsArgs
	{
		public BBox Box;
		public Color Color;
	}

	internal unsafe struct DrawTransformArgs
	{
		public Vector3 Position;
		public Rotation Rotation;
	}

	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	unsafe delegate void DebugDrawDelegate_t( DebugDrawArgs* args );

	private unsafe void OnDebugDrawInternal( DebugDrawArgs* args )
	{
		if ( args == null )
			return;

		if ( args->String != null ) OnDrawStringInternal( args->String );
		else if ( args->Segment != null ) OnDrawSegmentInternal( args->Segment );
		else if ( args->Point != null ) OnDrawPointInternal( args->Point );
		else if ( args->Bounds != null ) OnDrawBoundsInternal( args->Bounds );
		else if ( args->Transform != null ) OnDrawTransformInternal( args->Transform );
	}

	private unsafe void OnDrawStringInternal( DrawStringArgs* args )
	{
		if ( args == null )
			return;

		if ( DebugOverlay == null )
			return;

		var position = args->Position;
		var color = args->Color;
		var text = Marshal.PtrToStringAnsi( (IntPtr)args->String );
		var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", 600 ) { Outline = new TextRendering.Outline { Color = Color.Black, Size = 3, Enabled = true } };

		DebugOverlay.ScreenText( position, scope );
	}

	private unsafe void OnDrawSegmentInternal( DrawSegmentArgs* args )
	{
		if ( args == null )
			return;

		if ( DebugOverlay == null )
			return;

		var p1 = args->P1;
		var p2 = args->P2;
		var color = args->Color;

		DebugOverlay.Line( p1, p2, color );
	}

	private unsafe void OnDrawPointInternal( DrawPointArgs* args )
	{
		if ( args == null )
			return;

		if ( DebugOverlay == null )
			return;

		var position = args->Position;
		var size = args->Size;
		var color = args->Color;

		DebugOverlay.Point( position, size * 0.1f, color, 0, true );
	}

	private unsafe void OnDrawBoundsInternal( DrawBoundsArgs* args )
	{
		if ( args == null )
			return;

		if ( DebugOverlay == null )
			return;

		var box = args->Box;
		var color = args->Color;

		DebugOverlay.Box( box, color );
	}

	private unsafe void OnDrawTransformInternal( DrawTransformArgs* args )
	{
		if ( args == null )
			return;

		if ( DebugOverlay == null )
			return;

		var position = args->Position;
		var rotation = args->Rotation;

		const float axisLength = 5.0f;

		DebugOverlay.Line( position, position + rotation.Forward * axisLength, Color.Red, overlay: true );
		DebugOverlay.Line( position, position + rotation.Right * axisLength, Color.Green, overlay: true );
		DebugOverlay.Line( position, position + rotation.Up * axisLength, Color.Blue, overlay: true );
	}
}
