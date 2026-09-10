using Sandbox;
using System;

namespace Editor
{
	/// <summary>
	/// A helper class to store a list of strings, which can then be navigated around, saved, restored
	/// </summary>
	public sealed class HistoryList<T>
	{
		/// <summary>
		/// The maximum history length
		/// </summary>
		public int MaxItems { get; set; } = 50;

		/// <summary>
		/// Print debug information on navigation
		/// </summary>
		public bool Debug { get; set; } = false;

		internal List<T> list = new();
		internal int position;

		/// <summary>
		/// Called when navigations successfully happened.
		/// </summary>
		public Action<T> OnNavigate;

		public T Current
		{
			get
			{
				if ( list.Count == 0 ) return default( T );
				return list[position];
			}
		}

		string _cookie;

		public string StateCookie
		{
			get => _cookie;

			set
			{
				if ( _cookie == value ) return;
				_cookie = value;
				Restore();
			}
		}

		public bool CanGoBack => position > 0;
		public bool CanGoForward => position < list.Count - 1;

		public void Clear()
		{
			list.Clear();
			position = 0;
			Save();
		}

		/// <summary>
		/// Navigate to the previous item in the list, removes any items after the current position.
		/// </summary>
		public void Add( T text )
		{
			if ( object.Equals( text, Current ) )
				return;

			// remove any items after the current position
			var numInFront = (list.Count - 1) - position;
			if ( numInFront > 0 )
			{
				list.RemoveRange( position + 1, numInFront );
			}

			list.Add( text );
			position = list.Count - 1;
			Trim();
			Save();
			PrintDebug();
		}

		/// <summary>
		/// Navigate to delta positions from the current position. For example, -1 is backwards.
		/// Returns false if nothing changed.
		/// </summary>
		public bool Navigate( int delta )
		{
			if ( list.Count == 0 )
				return false;

			var target = (position + delta).Clamp( 0, list.Count - 1 );
			if ( target == position )
				return false;

			position = target;
			OnNavigate?.Invoke( Current );
			Save();
			PrintDebug();
			return true;
		}

		void PrintDebug()
		{
			if ( !Debug )
				return;

			for ( int i = 0; i < list.Count; i++ )
			{
				Log.Info( $"{(position == i ? ">" : "")}{i}: {list[i]}" );
			}
		}

		private void Save()
		{
			if ( string.IsNullOrEmpty( StateCookie ) ) return;

			EditorCookie.Set( $"{StateCookie}.list", list );
			EditorCookie.Set( $"{StateCookie}.pos", position );
		}

		private void Restore()
		{
			if ( string.IsNullOrEmpty( StateCookie ) ) return;

			list = EditorCookie.Get( $"{StateCookie}.list", list );
			list ??= new List<T>();

			position = EditorCookie.Get( $"{StateCookie}.pos", position );
			position = position.Clamp( 0, list.Count - 1 );

			Trim();
		}

		private void Trim()
		{
			while ( list.Count > MaxItems )
			{
				list.RemoveAt( 0 );
				position--;
			}
		}
	}
}
