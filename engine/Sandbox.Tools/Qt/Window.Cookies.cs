using Sandbox;

namespace Editor
{
	public partial class Window
	{
		string _stateCookie;

		/// <summary>
		/// A unique identifier for this window, to store the window state across sessions using the <see cref="Cookie">Cookie</see> library.
		/// </summary>
		public string StateCookie
		{
			get => _stateCookie;
			set
			{
				if ( _stateCookie == value ) return;
				_stateCookie = value;

				RestoreFromStateCookie();
			}
		}

		/// <summary>
		/// Called whenever the window should restore its state via the <see cref="EditorCookie">EditorCookie</see> library,
		/// that was previously saved in <see cref="SaveToStateCookie"/>.<br/>
		/// You should use <see cref="StateCookie"/> in the cookie name.
		/// </summary>
		public virtual void RestoreFromStateCookie()
		{
			if ( string.IsNullOrWhiteSpace( StateCookie ) )
				return;

			// We restore geometry from a plain rect we save ourselves - Qt's saved
			// geometry blob rescales unpredictably across monitors with different DPI
			if ( !EditorCookie.TryGet( $"Window.{StateCookie}.Rect", out Rect rect ) || !_widget.tryRestoreGeometry( rect ) )
			{
				Center();
			}

			if ( EditorCookie.Get( $"Window.{StateCookie}.Maximized", false ) )
				SetMaximized( true );

			var state = EditorCookie.GetString( $"Window.{StateCookie}.State", null );
			if ( state != null ) RestoreState( state );
		}

		/// <summary>
		/// Called whenever the window should save its state via the <see cref="EditorCookie">EditorCookie</see> library,
		/// to be later restored in <see cref="RestoreFromStateCookie"/>. This is useful to carry data across game sessions.<br/>
		/// You should use <see cref="StateCookie"/> in the cookie name.
		/// </summary>
		[Event( "app.exit" )]
		public virtual void SaveToStateCookie()
		{
			if ( string.IsNullOrWhiteSpace( StateCookie ) )
				return;

			if ( !this.IsValid() )
				return;

			var state = SaveState();

			EditorCookie.SetString( $"Window.{StateCookie}.State", state );
			EditorCookie.Set( $"Window.{StateCookie}.Maximized", IsMaximized );

			// Only when plain windowed, so the rect always holds the last normal geometry
			if ( !IsMinimized && !IsMaximized )
			{
				EditorCookie.Set( $"Window.{StateCookie}.Rect", _widget.windowGeometry().Rect );
			}
		}
	}
}
