using Sandbox;

namespace Editor.ActionGraphs;

#nullable enable

public sealed class WarningFrame : Widget
{
	public Color Color { get; set; } = Color.Parse( "#FA9131" )!.Value;

	public string? Text
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;
			Update();
		}
	}

	public WarningFrame()
	{
		TransparentForMouseEvents = true;
	}

	protected override void OnPaint()
	{
		if ( string.IsNullOrEmpty( Text ) ) return;

		Paint.SetPen( Color, 8f );
		Paint.DrawRect( LocalRect );

		Paint.SetDefaultFont( 10f );
		var textRect = LocalRect.Contain( Paint.MeasureText( Text ) + new Vector2( 16f, 8f ), TextFlag.RightBottom );

		Paint.ClearPen();
		Paint.SetBrush( Color );
		Paint.DrawRect( textRect.Shrink( 0f, 0f, 4f, 4f ) );
		Paint.ClearBrush();
		Paint.SetPen( Color.Black );
		Paint.DrawText( textRect, Text );
	}
}
