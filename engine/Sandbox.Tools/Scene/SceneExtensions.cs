using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using System;
using System.Text.Json.Nodes;

namespace Sandbox;

public static partial class SceneExtensions
{
	/// <summary>
	/// We should make this globally reachanle at some point. Should be able to draw icons using bitmaps etc too.
	/// </summary>
	public static Editor.Menu CreateContextMenu( this Scene scene, Widget parent = null )
	{
		var menu = new Editor.Menu( parent );

		menu.AddOption( "Save", "save", action: () => scene.Editor?.Save( false ) ).Enabled = (scene.Editor?.HasUnsavedChanges ?? false) && scene.Source is not null;
		menu.AddOption( "Save Scene As..", action: () => scene.Editor?.Save( true ) );

		return menu;

	}


	/// <summary>
	/// Copy the target <see cref="Component"/> to the clipboard.
	/// </summary>
	/// <param name="component"></param>
	public static void CopyToClipboard( this Component component )
	{
		using var scope = SceneEditorSession.Scope();

		var result = component.Serialize();
		if ( result is null ) return;
		EditorUtility.Clipboard.Copy( result.ToString() );
	}


	/// <summary>
	/// Paste component values from clipboard to the target <see cref="Component"/>.
	/// </summary>
	/// <param name="target"></param>
	public static void PasteValues( this Component target )
	{
		var text = EditorUtility.Clipboard.Paste();
		using var scope = SceneEditorSession.Scope();

		try
		{
			if ( JsonNode.Parse( text ) is not JsonObject pastedJso )
				return;

			using ( SceneEditorSession.Active.UndoScope( "Paste Component Values" ).WithComponentChanges( target ).Push() )
			{
				pastedJso.AsObject().Remove( "__guid" );
				target.DeserializeImmediately( pastedJso );
			}
		}
		catch
		{
			// Do nothing.
		}
	}

	/// <summary>
	/// Return true if this object should be shown in the GameObject list
	/// </summary>
	public static bool ShouldShowInHierarchy( this GameObject target )
	{
		if ( target is null ) return false;
		if ( target.Flags.Contains( GameObjectFlags.Hidden ) ) return false;
		return true;
	}

	/// <summary>
	/// Paste a <see cref="Component"/> as a new component on the target <see cref="GameObject"/>.
	/// </summary>
	/// <param name="target"></param>
	public static void PasteComponent( this GameObject target )
	{
		var text = EditorUtility.Clipboard.Paste();

		using var scope = SceneEditorSession.Scope();

		try
		{
			if ( JsonNode.Parse( text ) is not JsonObject pastedJso )
				return;

			var componentType = TypeLibrary.GetType<Component>( (string)pastedJso["__type"] );
			if ( componentType is null )
			{
				Log.Warning( $"TypeLibrary couldn't find {nameof( Component )} type {pastedJso["__type"]}" );
				return;
			}

			using ( SceneEditorSession.Active.UndoScope( $"Paste {componentType.Name} As New" ).WithComponentCreations().Push() )
			{
				SceneUtility.MakeIdGuidsUnique( pastedJso );

				var cmp = target.Components.Create( componentType );
				cmp.DeserializeImmediately( pastedJso );
			}
		}
		catch
		{
			// Do nothing.
		}
	}


	public static void PaintComponentIcon( this TypeDescription td, Rect rect, float opacity = 1 )
	{
		Paint.SetPen( Theme.Green.WithAlpha( opacity ) );
		Paint.DrawIcon( rect, td.Icon, rect.Height, TextFlag.Center );
	}


	public static void EnableEditorRigidBody( this Scene scene, Rigidbody body, bool enabled )
	{
		var system = scene.GetSystem<ScenePhysicsSystem>();
		if ( system is null )
			return;

		if ( enabled )
		{
			system.AddRigidBody( body );
		}
		else
		{
			system.RemoveRigidBody( body );
		}
	}

	public static void DisableEditorRigidBodies( this Scene scene )
	{
		var system = scene.GetSystem<ScenePhysicsSystem>();
		if ( system is null )
			return;

		system.RemoveRigidBodies();
	}

	public static void EnableEditorPhysics( this Scene scene, bool enabled )
	{
		var system = scene.GetSystem<ScenePhysicsSystem>();
		if ( system is null )
			return;

		system.Enabled = enabled;
	}

	public static void SetTargetTransform( this Rigidbody body, Transform? tx )
	{
		body.TargetTransform = tx;
	}
}
