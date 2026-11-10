namespace Editor.SpriteEditor;

public class SpritesheetPreview : Widget
{
	public SpritesheetRenderingWidget Rendering { get; private set; }

	public SpritesheetPreview( SpritesheetImporter parent ) : base( parent )
	{
		Name = "Spritesheet Preview";
		Layout = Layout.Column();
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.Flexible;

		Rendering = new SpritesheetRenderingWidget( parent, this );
		Layout.Add( Rendering );

		var texture = Texture.LoadFromFileSystem( parent.ImagePath, FileSystem.Mounted );
		if ( texture is not null )
		{
			Rendering.SetTexture( texture );
			parent.TryAutoDetect( texture.Width, texture.Height );
		}
	}
}
