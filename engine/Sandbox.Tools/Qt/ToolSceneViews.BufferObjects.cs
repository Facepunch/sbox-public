using Sandbox.Rendering;

namespace Editor;

internal static partial class ToolSceneViews
{
	/// <summary>
	/// Native buffer objects in a tool scene's worlds (<c>CreateSceneObjectFromBuffers</c>: the material editor's preview
	/// sphere, cube and plane, ModelDoc's mesh previews), which have draws of their own and no model, so a renderer that draws
	/// models has nothing to draw. Each gets a model of its draws, once (<c>RenderTools.CreateBufferObjectModel</c>), its
	/// draws binding the object's buffers, which the world renderer reads as any run-time model. Native makes a new object
	/// when it changes one (a new material or shape), so one model per object is enough; it goes with its object.
	/// </summary>
	internal static class BufferObjectModels
	{
		// Every plain native object asked about, with its model (null for none): asked once
		static readonly Dictionary<SceneObject, Model> made = new();
		static readonly List<SceneObject> gone = new();

		/// <summary>Models for <paramref name="world"/>'s buffer objects that have none yet.</summary>
		public static void Give( SceneWorld world )
		{
			lock ( world.InternalSceneObjects )
			{
				foreach ( var sceneObject in world.InternalSceneObjects )
				{
					if ( sceneObject.GetType() != typeof( SceneObject ) || sceneObject.native.IsNull || made.ContainsKey( sceneObject ) ) continue;

					var native = NativeEngine.RenderTools.CreateBufferObjectModel( sceneObject );
					var model = native.IsNull ? null : Model.FromNative( native, procedural: true );
					made[sceneObject] = model;
					if ( model is null ) continue;

					sceneObject.BufferObjectModel = model;
					if ( world.Render is not null ) sceneObject.NotifyChanged( SceneObjectChange.Model );
				}
			}

			Forget();
		}

		/// <summary>The models of objects that have gone, whose buffers go with them.</summary>
		static void Forget()
		{
			foreach ( var (sceneObject, _) in made )
			{
				if ( !sceneObject.IsValid() ) gone.Add( sceneObject );
			}

			foreach ( var sceneObject in gone )
			{
				made.Remove( sceneObject, out var model );
				sceneObject.BufferObjectModel = null;
				model?.Destroy();
			}

			gone.Clear();
		}
	}
}
