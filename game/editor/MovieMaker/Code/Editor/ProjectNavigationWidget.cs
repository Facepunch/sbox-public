
using System.IO;
using Sandbox.Diagnostics;
using Sandbox.MovieMaker;
using Sandbox.UI;

namespace Editor.MovieMaker;

#nullable enable

public sealed class ProjectNavigationWidget : Widget
{
	public TrackListWidget TrackList { get; }
	public Session Session { get; }

	public bool IsActive { get; }

	protected override Vector2 SizeHint() => 32f;

	public ProjectNavigationWidget( TrackListWidget trackList, Session session, bool isActive )
		: base( trackList )
	{
		TrackList = trackList;
		Session = session;
		IsActive = isActive;

		FixedHeight = 32f;

		Cursor = IsActive ? CursorShape.Arrow : CursorShape.Finger;

		var location = session.Resource switch
		{
			MovieResource res => $"<p><code>{Path.GetFileName( res.ResourcePath )}</code></p>",
			_ => ""
		};

		var description = IsActive ? "Currently open movie clip." : "Parent movie clip containing the currently open one.";

		ToolTip = $"<p>{description}</p>{location}";
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( IsActive ) return;

		while ( Session.Editor.Session != Session && Session.Editor.Session?.Parent is not null )
		{
			Session.Editor.ExitSequence();
		}
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = false;

		var color = IsActive ? Theme.SurfaceLightBackground : Paint.HasMouseOver ? Theme.SelectedBackground : Theme.SurfaceBackground;

		PaintExtensions.PaintFilmStrip( new Rect( 0f, 1f, LocalRect.Width, 30f ), color );

		Paint.ClearBrush();
		Paint.SetPen( Theme.TextControl.Darken( IsActive ? 0f : Paint.HasMouseOver ? 0.1f : 0.25f ) );

		Paint.DrawText( LocalRect.Shrink( 12f, 4f ), Session.Title );

		if ( Session.Context is { } context )
		{
			Paint.SetPen( Paint.Pen.WithAlpha( 0.8f ) );
			Paint.SetFont( null, 7f );
			Paint.DrawText( LocalRect.Shrink( 4f, 4f ), context.TimeRange.Start.ToString(), TextFlag.LeftCenter );
			Paint.DrawText( LocalRect.Shrink( 4f, 4f ), context.TimeRange.End.ToString(), TextFlag.RightCenter );
		}
	}
}
