namespace Editor.RectEditor;

public class Settings
{
	// Rect Editor Settings
	[Hide] public string ReferenceMaterial { get; set; } = null;
	[Hide] public bool ShowNormalizedValues { get; set; } = false;
	public int GridSize { get; set; } = 16;

	// Fast Texture Tool Settings
	[Hide] public bool IsFastTextureTool { get; set; }

	[ShowIf( nameof( IsFastTextureTool ), true ), InlineEditor, WideMode]
	public FastTextureSettings FastTextureSettings { get; set; } = new();
}
