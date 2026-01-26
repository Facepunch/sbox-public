using Sandbox.Internal;
using System;
using System.Collections.Immutable;
using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;

namespace Sandbox;

public static partial class SandboxToolExtensions
{
	/// <summary>
	/// Render this camera to the target widget. Once you do this the target widget becomes "externally painted", so you
	/// won't be able to paint on it anymore with Qt's Paint stuff. 
	/// </summary>
	public static bool RenderToPixmap( this SceneCamera camera, Pixmap targetPixmap, bool async = false )
	{
		if ( camera.World == null )
			return false;

		if ( targetPixmap == null || targetPixmap.Width <= 1 || targetPixmap.Height <= 1 )
			return false;

		camera.OnPreRender( targetPixmap.Size );

		using Bitmap bitmap = new Bitmap( targetPixmap.Width, targetPixmap.Height );
		camera.RenderToBitmap( bitmap );
		targetPixmap.UpdateFromPixels( bitmap );

		return true;
	}

	/// <summary>
	/// Render this camera to the target widget. Once you do this the target widget becomes "externally painted", so you
	/// won't be able to paint on it anymore with Qt's Paint stuff. 
	/// </summary>
	public static bool RenderToPixmap( this CameraComponent camera, Pixmap targetPixmap, bool async = false )
	{
		if ( targetPixmap == null || targetPixmap.Width <= 1 || targetPixmap.Height <= 1 )
			return false;

		if ( !camera.IsValid() )
			return false;

		using ( camera.Scene.Push() )
		{
			camera.Scene.PreCameraRender();
			camera.InitializeRendering();
			camera.SceneCamera.OnPreRender( targetPixmap.Size );

			using Bitmap bitmap = new Bitmap( targetPixmap.Width, targetPixmap.Height );
			camera.RenderToBitmap( bitmap );
			targetPixmap.UpdateFromPixels( bitmap );
			return true;
		}
	}

	/// <summary>
	/// Render this camera to the target widget. Once you do this the target widget becomes "externally painted", so you
	/// won't be able to paint on it anymore with Qt's Paint stuff. 
	/// </summary>
	public static bool RenderToPixmap( this Scene scene, Pixmap targetPixmap, bool async = false )
	{
		return RenderToPixmap( scene.Camera, targetPixmap, async );
	}


	/// <summary>
	/// Render this camera to the target widget. Once you do this the target widget becomes "externally painted", so you
	/// won't be able to paint on it anymore with Qt's Paint stuff. 
	/// </summary>
	public static unsafe bool RenderToVideo( this SceneCamera camera, VideoWriter videoWriter, TimeSpan? time = default )
	{
		if ( camera.World == null )
			return false;

		if ( videoWriter == null || videoWriter.Width <= 1 || videoWriter.Height <= 1 )
			return false;

		camera.OnPreRender( new Vector2( videoWriter.Width, videoWriter.Height ) );

		using Bitmap bitmap = new Bitmap( videoWriter.Width, videoWriter.Height );
		camera.RenderToBitmap( bitmap );

		videoWriter.AddFrame( bitmap, time );
		return true;
	}

	/// <summary>
	/// Render this camera to the target widget. Once you do this the target widget becomes "externally painted", so you
	/// won't be able to paint on it anymore with Qt's Paint stuff. 
	/// </summary>
	public static async Task<bool> RenderToVideoAsync( this SceneCamera camera, VideoWriter videoWriter, TimeSpan? time = default )
	{
		if ( camera.World == null )
			return false;

		if ( videoWriter == null || videoWriter.Width <= 1 || videoWriter.Height <= 1 )
			return false;

		camera.OnPreRender( new Vector2( videoWriter.Width, videoWriter.Height ) );

		using Bitmap bitmap = new Bitmap( videoWriter.Width, videoWriter.Height );
		camera.RenderToBitmap( bitmap );

		return await Task.Run( () => videoWriter.AddFrame( bitmap, time ) );
	}


	/// <summary>
	/// Shortcut for EditorTypeLibrary.GetSerializedObject( x )
	/// </summary>
	public static SerializedObject GetSerialized( this object self )
	{
		try
		{
			return EditorTypeLibrary.GetSerializedObject( self );
		}
		catch
		{
			return new ReflectionSerializedObject( self );
		}
	}

	/// <summary>
	/// Describes the path to a <see cref="SerializedProperty"/> from either a <see cref="GameObject"/>
	/// or <see cref="Component"/>.
	/// </summary>
	public sealed class PropertyPath
	{
		private string _fullName;

		/// <summary>
		/// Full path to reach the original property, starting from a property on a <see cref="GameObject"/> or
		/// <see cref="Component"/>.
		/// </summary>
		public IReadOnlyList<SerializedProperty> Properties { get; }

		/// <summary>
		/// Names of each property in <see cref="Properties"/>, separated by <c>'.'</c>s.
		/// </summary>
		public string FullName => _fullName ??= string.Join( ".", Properties.Select( x => x.Name ) );

		/// <summary>
		/// <see cref="GameObject"/>(s) or <see cref="Component"/>(s) that contain the original property.
		/// </summary>
		public IEnumerable<object> Targets => Properties[0].Parent.Targets;

		internal PropertyPath( IEnumerable<SerializedProperty> properties )
		{
			Properties = properties.ToImmutableList();
		}

		/// <summary>
		/// Returns <see cref="FullName"/>.
		/// </summary>
		public override string ToString() => FullName;
	}

	/// <summary>
	/// Tries to find the path from a <see cref="GameObject"/> or <see cref="Component"/> to this property.
	/// Returns <see langword="null"/> if not found.
	/// </summary>
	public static PropertyPath FindPathInScene( this SerializedProperty prop )
	{
		var path = new List<SerializedProperty>();

		while ( prop?.Parent is not null )
		{
			path.Add( prop );

			if ( prop.Parent.Targets.Any( x => x is Component or GameObject ) )
			{
				path.Reverse();
				return new PropertyPath( path );
			}

			prop = prop.Parent?.ParentProperty;
		}

		return null;
	}

	/// <summary>
	/// Tries to find the <see cref="GameObject"/> that contains the given property.
	/// Returns <see langword="null"/> if not found.
	/// </summary>
	public static GameObject GetContainingGameObject( this SerializedProperty prop )
	{
		if ( prop.FindPathInScene() is not { } path )
		{
			return null;
		}

		if ( path.Targets.OfType<GameObject>().FirstOrDefault() is { } go )
		{
			return go;
		}

		if ( path.Targets.OfType<Component>().FirstOrDefault() is { } component )
		{
			return component.GameObject;
		}

		return null;
	}

	/// <summary>
	/// Create a feasible title from the current selection
	/// </summary>
	public static string ConstructTitle( this SelectionSystem sys )
	{
		if ( sys.Count == 0 ) return "Nothing";
		if ( sys.Count > 1 ) return $"{sys.Count} Objects";

		if ( sys.First() is GameObject go )
			return go.Name;

		return sys.First().ToString();
	}
}
