using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

partial class Session
{
	internal string CookiePrefix => $"moviemaker.{(Player.Resource as MovieResource)?.ResourceId.ToString() ?? Player.Id.ToString()}";

	public T GetCookie<T>( string key, T fallback )
	{
		return ProjectCookie.Get( $"{CookiePrefix}.{key}", fallback );
	}

	public void SetCookie<T>( string key, T value )
	{
		ProjectCookie.Set( $"{CookiePrefix}.{key}", value );
	}

	public class CookieHelper( Session session )
	{
		public EditModeType EditMode
		{
			get => MovieMaker.EditMode.Get( session.GetCookie( nameof(EditMode), "" ) );
			set => session.SetCookie( nameof(EditMode), value.Name );
		}

		public bool IsLooping
		{
			get => session.GetCookie( nameof(IsLooping), true );
			set => session.SetCookie( nameof(IsLooping), value );
		}

		public float TimeScale
		{
			get => session.GetCookie( nameof(TimeScale), 1f );
			set => session.SetCookie( nameof(TimeScale), value );
		}

		public bool SyncPlayback
		{
			get => session.GetCookie( nameof( SyncPlayback ), true );
			set => session.SetCookie( nameof( SyncPlayback ), value );
		}

		public int FrameRate
		{
			get => session.GetCookie( nameof( FrameRate ), 10 );
			set => session.SetCookie( nameof( FrameRate ), value );
		}

		public bool FrameSnap
		{
			get => session.GetCookie( nameof(FrameSnap), true );
			set => session.SetCookie( nameof(FrameSnap), value );
		}

		public bool ObjectSnap
		{
			get => session.GetCookie( nameof(ObjectSnap), true );
			set => session.SetCookie( nameof(ObjectSnap), value );
		}

		public MovieTime TimeOffset
		{
			get => MovieTime.FromTicks( session.GetCookie( nameof(TimeOffset), 0 ) );
			set => session.SetCookie( nameof(TimeOffset), value.Ticks );
		}

		public float PixelsPerSecond
		{
			get => session.GetCookie( nameof( PixelsPerSecond ), 100f );
			set => session.SetCookie( nameof( PixelsPerSecond ), value );
		}

		public float ScrollPosition
		{
			get => session.GetCookie( nameof(ScrollPosition), 0f );
			set => session.SetCookie( nameof(ScrollPosition), value );
		}

		public bool IsAdditive
		{
			get => session.GetCookie( nameof(IsAdditive), BlendOptions.Default.IsAdditive );
			set => session.SetCookie( nameof(IsAdditive), value );
		}

		public string ExportPath
		{
			get => session.GetCookie( nameof(ExportPath), System.IO.Path.Combine(Sandbox.Project.Current.GetRootPath(), $"{session.FileName}.mp4") );
			set => session.SetCookie( nameof(ExportPath), value );
		}

		public string ImageSequencePath
		{
			get => session.GetCookie( nameof(ImageSequencePath), System.IO.Path.Combine( Sandbox.Project.Current.GetRootPath(), $"{session.FileName}.png" ) );
			set => session.SetCookie( nameof(ImageSequencePath), value );
		}
	}

	private CookieHelper? _cookieHelper;
	public CookieHelper Cookies => _cookieHelper ??= new CookieHelper( this );

	public void RestoreFromCookies()
	{
		if ( IsEditorScene )
		{
			_isLooping = Cookies.IsLooping;
			_timeScale = Cookies.TimeScale;
		}

		_frameRate = Cookies.FrameRate;
		_frameSnap = Cookies.FrameSnap;
		_objectSnap = Cookies.ObjectSnap;

		var timeline = Editor.TimelinePanel?.Timeline;

		timeline?.SetView( Cookies.TimeOffset, Cookies.PixelsPerSecond );

		SetEditMode( Cookies.EditMode );
	}
}
