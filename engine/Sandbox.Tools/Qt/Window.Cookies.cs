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

			var state = EditorCookie.GetString( $"Window.{StateCookie}.State", null );
			var geo = EditorCookie.GetString( $"Window.{StateCookie}.Geometry", null );

			if ( geo != null ) RestoreGeometry( geo );
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

			if ( IsMinimized )
				return;

			var state = SaveState();
			var geo = SaveGeometry();

			EditorCookie.SetString( $"Window.{StateCookie}.State", state );
			EditorCookie.SetString( $"Window.{StateCookie}.Geometry", geo );
		}
	}
}
