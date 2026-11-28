using Sandbox.Audio;
using System;

namespace Editor.Audio;

public class ProcessorListWidget : Widget
{
	private Mixer mixer;

	public ProcessorListWidget( Mixer main )
	{
		mixer = main;
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.CanGrow;

		Layout = Layout.Column();
		BuildUI();
	}

	[EditorEvent.Hotload]
	public void BuildUI()
	{
		Layout.Clear( true );

		var row = Layout.AddColumn();

		foreach ( var p in mixer.GetProcessors() )
		{
			row.Add( new ProcessorWidget( p ) );
		}
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		SetContentHash( contentHash ??= ContentHash, 0.3f );
	}

	// Made once: passing the method itself would make a delegate each call
	Func<int> contentHash;

	int ContentHash()
	{
		var processors = mixer.GetProcessors();
		HashCode hash = default;

		for ( int i = 0; i < processors.Length; i++ )
		{
			hash.Add( processors[i] );
		}

		return hash.ToHashCode();
	}

}

public class ProcessorWidget : Widget
{
	private AudioProcessor processor;

	public ProcessorWidget( AudioProcessor p )
	{
		this.processor = p;

		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	protected override Vector2 SizeHint() => 18;

	protected override void OnPaint()
	{
		bool selected = EditorUtility.InspectorObject == processor;

		Paint.SetBrushAndPen( Theme.ControlBackground.WithAlpha( selected ? 0.8f : 0.5f ) );
		if ( selected ) Paint.SetBrushAndPen( Theme.Blue.WithAlpha( 0.5f ) );
		Paint.DrawRect( LocalRect );

		Paint.Pen = Theme.TextControl;
		Paint.DrawText( LocalRect.Shrink( 8, 0 ), $"{processor.GetType().Name}", TextFlag.LeftCenter );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		EditorUtility.InspectorObject = processor;
		e.Accepted = true;
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		SetContentHash( contentHash ??= ContentHash, 0.1f );
	}

	// Made once: passing the method itself would make a delegate each call
	Func<int> contentHash;

	int ContentHash()
	{
		return HashCode.Combine( EditorUtility.InspectorObject == processor );
	}
}
