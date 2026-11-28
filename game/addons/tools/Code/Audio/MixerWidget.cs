using Sandbox.Audio;
using System;

namespace Editor.Audio;

public class MixerWidget : Widget
{
	private Mixer mixer;
	SerializedObject serialized;

	AudioMeterWidget meterWidget;

	public MixerWidget( Mixer main )
	{
		mixer = main;
		HorizontalSizeMode = SizeMode.CanShrink;
		serialized = mixer.GetSerialized();

		Layout = Layout.Column();
		BuildUI();
	}

	[EditorEvent.Hotload]
	public void BuildUI()
	{
		Layout.Clear( true );

		Layout.Margin = 0;

		{
			Layout.AddSpacingCell( 24 );
		}

		Layout.AddSpacingCell( 16 );

		{
			var row = Layout.AddRow();
			row.Spacing = 8;
			row.Margin = 8;
			row.Add( new Button( "SOLO" ) );
			row.Add( new Button( "MUTE" ) );
		}

		Layout.AddSpacingCell( 16 );

		// Big volumey wasted space
		{
			var center = Layout.AddRow();
			center.AddStretchCell();
			center.Add( new VolumeTicksWidget() );
			meterWidget = center.Add( new AudioMeterWidget() );
			center.Add( new VolumeSliderWidget( serialized.GetProperty( "Volume" ) ) );
			center.AddStretchCell();
		}

		// Processor List
		{
			Layout.AddSpacingCell( 8 );
			Layout.Add( new ProcessorListWidget( mixer ) );
		}

		Layout.AddStretchCell();
	}

	protected override Vector2 SizeHint() => new Vector2( 120, 200 );

	protected override void OnPaint()
	{
		base.OnPaint();

		bool selected = EditorUtility.InspectorObject == mixer;

		Paint.SetBrushAndPen( Theme.SurfaceBackground );
		Paint.DrawRect( LocalRect );

		// Header
		{
			var headerRect = LocalRect;
			headerRect.Height = 22;

			Paint.SetBrushAndPen( Theme.ControlBackground.WithAlpha( 0.5f ) );

			if ( selected )
				Paint.SetBrushAndPen( Theme.Blue.WithAlpha( 0.5f ) );

			Paint.DrawRect( headerRect );

			Paint.Pen = Theme.TextControl;

			Paint.DrawText( headerRect.Shrink( 8, 0 ), "Mixer Name", TextFlag.LeftCenter );

			var voices = mixer.Meter.Current.VoiceCount;
			Paint.Pen = Theme.TextControl.WithAlpha( voices == 0 ? 0.2f : 0.5f );
			Paint.DrawText( headerRect.Shrink( 8, 0 ), $"{voices:n0}", TextFlag.RightCenter );
		}

		if ( EditorUtility.InspectorObject == mixer )
		{
			Paint.SetBrushAndPen( Color.Transparent, Theme.Blue.WithAlpha( 0.1f ), 2 );
			Paint.DrawRect( LocalRect );
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		EditorUtility.InspectorObject = mixer;
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		meterWidget.UpdateValues( mixer.Meter );

		SetContentHash( contentHash ??= ContentHash, 0.1f );
	}

	// Made once: passing the method itself would make a delegate each call
	Func<int> contentHash;

	int ContentHash()
	{
		return HashCode.Combine( EditorUtility.InspectorObject == mixer, mixer.Meter.Current.VoiceCount );
	}
}
