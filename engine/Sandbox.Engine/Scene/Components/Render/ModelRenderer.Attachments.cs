namespace Sandbox;

public partial class ModelRenderer
{
	bool _createAttachments = false;

	internal readonly Dictionary<ModelAttachments.Attachment, GameObject> attachmentToGameObject = new();

	/// <summary>
	/// Get the GameObject of a specific attachment.
	/// </summary>
	public GameObject GetAttachmentObject( string name )
	{
		if ( Model is null ) return null;

		var attachment = Model.Attachments.Get( name );
		return GetAttachmentObject( attachment );
	}

	/// <summary>
	/// Get the GameObject of a specific attachment.
	/// </summary>
	public GameObject GetAttachmentObject( ModelAttachments.Attachment attachment )
	{
		if ( attachment is null ) return null;

		return attachmentToGameObject.GetValueOrDefault( attachment );
	}

	public virtual GameObject GetBoneObject( BoneCollection.Bone bone )
	{
		return default;
	}

	void BuildAttachmentHierarchy()
	{
		ClearAttachmentHierarchy();

		if ( Model is null )
			return;

		if ( CreateAttachments )
		{
			foreach ( var a in Model.Attachments.All )
			{
				attachmentToGameObject[a] = CreateAttachment( a );
			}
		}
	}

	void ClearAttachmentHierarchy()
	{
		if ( attachmentToGameObject.Count == 0 )
			return;

		foreach ( var attachment in attachmentToGameObject )
		{
			if ( !attachment.Value.IsValid() )
				continue;

			attachment.Value.Flags &= ~GameObjectFlags.Attachment;
		}

		attachmentToGameObject.Clear();
	}

	GameObject CreateAttachment( ModelAttachments.Attachment a )
	{
		Assert.NotNull( a );

		var parent = GameObject;

		var go = parent.Children.FirstOrDefault( x => a.IsNamed( x.Name ) && !x.Flags.Contains( GameObjectFlags.Bone ) );
		if ( !go.IsValid() )
		{
			go = Scene.CreateObject( true );
		}

		go.Parent = parent;
		go.Name = a.Name;
		go.Flags |= GameObjectFlags.Attachment;
		go.LocalTransform = a.WorldTransform;
		return go;
	}
}
