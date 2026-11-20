using System.Runtime.InteropServices;

namespace Sandbox;

public partial class DebugOverlaySystem
{
	/// <summary>
	/// A line of the engine's native debug visualizers (<c>DebugOverlayLine_t</c>).
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	struct NativeDebugLine
	{
		public Vector3 Start;
		public Vector3 End;
		public Color32 Color;
		public int NoDepthTest;
	}

	/// <summary>
	/// A label of the engine's native debug visualizers (<c>DebugOverlayText_t</c>).
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	struct NativeDebugText
	{
		public Vector3 Position;
		public Color32 Color;
		public IntPtr Text;
	}

	static NativeDebugLine[] nativeLines = new NativeDebugLine[256];
	static NativeDebugText[] nativeTexts = new NativeDebugText[64];

	/// <summary>
	/// Draw what the engine's native debug visualizers hold this frame (the animation system's IK and foot lock debug,
	/// <c>ik_debug</c> and friends): into the game's scene while one plays, else into the editor's scenes. Native views drew
	/// them before, and nothing else does.
	/// </summary>
	unsafe void DrawNativeVisualizers()
	{
		if ( Game.ActiveScene is { } active ? Scene != active : !Scene.IsEditor ) return;

		int lineCount, textCount;
		while ( true )
		{
			fixed ( NativeDebugLine* pLines = nativeLines )
			fixed ( NativeDebugText* pTexts = nativeTexts )
			{
				int texts = 0;
				lineCount = NativeEngine.RenderTools.GetDebugVisualizers( (IntPtr)pLines, nativeLines.Length, (IntPtr)pTexts, nativeTexts.Length, (IntPtr)(&texts) );
				textCount = texts;
			}

			// Gathering ages the lists, so a second call in a frame sees the same ones
			if ( lineCount <= nativeLines.Length && textCount <= nativeTexts.Length ) break;
			if ( lineCount > nativeLines.Length ) nativeLines = new NativeDebugLine[lineCount * 2];
			if ( textCount > nativeTexts.Length ) nativeTexts = new NativeDebugText[textCount * 2];
		}

		for ( int i = 0; i < lineCount; i++ )
		{
			ref var line = ref nativeLines[i];
			Line( line.Start, line.End, line.Color, overlay: line.NoDepthTest != 0 );
		}

		for ( int i = 0; i < textCount; i++ )
		{
			ref var text = ref nativeTexts[i];
			var label = Marshal.PtrToStringUTF8( text.Text );
			if ( string.IsNullOrEmpty( label ) ) continue;

			Text( text.Position, label, 14, TextFlag.Center, text.Color );
		}
	}
}
