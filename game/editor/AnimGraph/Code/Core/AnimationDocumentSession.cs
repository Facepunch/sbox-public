using Sandbox.Helpers;

namespace Editor.AnimGraph;

internal interface IAnimationDocument
{
	string PreviewModel { get; set; }
	string Snapshot();
	void Restore( string snapshot );
	bool Save();
}

internal sealed class AnimationDocumentSession : AssetSystem.IEventListener
{
	private static readonly Dictionary<Asset, AnimationDocumentSession> Sessions = new();
	private readonly Asset _asset;
	private readonly Func<string, IAnimationDocument> _load;
	private readonly UndoSystem _undo = new();
	private string _committed;
	private string _saved;
	private int _users;

	public IAnimationDocument Document { get; private set; }
	public bool ReadOnly => Document is null || AssetSystem.IsCloudInstalled( _asset.Package );
	public bool CanUndo => !ReadOnly && _undo.Back.Count > 0;
	public bool CanRedo => !ReadOnly && _undo.Forward.Count > 0;
	public event Action<object> Changed;

	private AnimationDocumentSession( Asset asset, Func<string, IAnimationDocument> load )
	{
		_asset = asset;
		_load = load;
		Document = asset.HasSourceFile ? load( asset.GetSourceFile( true ) ) : null;
		_saved = _committed = Document?.Snapshot();
		_undo.Initialize();
		EditorEvent.Register( this );
	}

	public static AnimationDocumentSession Acquire( Asset asset, Func<string, IAnimationDocument> load )
	{
		if ( !Sessions.TryGetValue( asset, out var session ) )
			Sessions[asset] = session = new( asset, load );

		session._users++;
		return session;
	}

	public void Release()
	{
		if ( --_users == 0 )
		{
			Sessions.Remove( _asset );
			EditorEvent.Unregister( this );
		}
	}

	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( asset != _asset || _committed != _saved || !asset.HasSourceFile || Document?.Snapshot() != _saved )
			return;

		var document = _load( asset.GetSourceFile( true ) );
		if ( document is null )
			return;

		var snapshot = document.Snapshot();
		if ( snapshot == _saved )
			return;

		Document = document;
		_saved = _committed = snapshot;
		_undo.Initialize();
		NotifyChanged( this );
	}

	public void Commit( string name, object source )
	{
		if ( ReadOnly )
			return;

		var before = _committed;
		var after = Document.Snapshot();
		if ( before == after )
			return;

		_committed = after;
		_undo.Insert( name, () => Restore( before ), () => Restore( after ) );
		NotifyChanged( source );
	}

	private void Restore( string snapshot )
	{
		Document.Restore( snapshot );
		_committed = Document.Snapshot();
	}

	private void NotifyChanged( object source = null )
	{
		_asset.HasUnsavedChanges = !ReadOnly && _committed != _saved;
		Changed?.Invoke( source );
	}

	public bool Save( object source )
	{
		if ( ReadOnly || !Document.Save() )
			return false;

		_saved = _committed = Document.Snapshot();
		NotifyChanged( source );
		return true;
	}

	public void Reset()
	{
		if ( ReadOnly )
			return;

		Restore( _saved );
		_undo.Initialize();
		NotifyChanged();
	}

	public void Undo()
	{
		if ( !CanUndo )
			return;

		_undo.Undo();
		NotifyChanged();
	}

	public void Redo()
	{
		if ( !CanRedo )
			return;

		_undo.Redo();
		NotifyChanged();
	}
}
