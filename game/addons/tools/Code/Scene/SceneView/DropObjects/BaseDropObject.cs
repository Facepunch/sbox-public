using System.Threading;

namespace Editor;

public abstract class BaseDropObject
{
	CancellationTokenSource DragCancelSource = new CancellationTokenSource();
	public bool Dropped { get; set; }
	public bool Deleted { get; set; }
	public string PackageStatus { get; set; }
	public Vector3 PivotPosition { get; set; }
	public BBox Bounds { get; set; } = BBox.FromPositionAndSize( 0, 16 );
	public Rotation Rotation { get; set; } = Rotation.Identity;
	public Vector3 Scale { get; set; } = Vector3.One;
	public GameObject GameObject { get; set; }

	protected SceneTraceResult trace;
	protected Transform traceTransform;

	public bool IsInitialized { get; private set; }

	/// <summary>
	/// Download/load asset
	/// </summary>
	protected abstract Task Initialize( string dragData, CancellationToken token );

	/// <summary>
	/// Position and update the preview. If Dropped is true, finalize action and update
	/// </summary>
	public virtual void OnUpdate() { }

	/// <summary>
	/// Position and update the preview. If Dropped is true, finalize action and update
	/// </summary>
	public virtual Task OnDrop() => Task.CompletedTask;

	/// <summary>
	/// Clean up after yourself
	/// </summary>
	public virtual void OnDestroy() { }

	public async Task StartInitialize( string dragData )
	{
		try
		{
			await Initialize( dragData, DragCancelSource.Token );
			IsInitialized = true;
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Error when dropping {this} - {e.Message}" );
			Delete();
		}
	}

	public void Tick()
	{
		try
		{
			OnUpdate();

			if ( Deleted ) return;
			if ( !IsInitialized ) return;

			if ( Dropped )
			{
				_ = OnDrop();
				Delete();
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Error when updating {this} - {e.Message}" );
			Delete();
		}
	}

	public virtual void UpdateDrag( SceneTraceResult tr, Gizmo.SceneSettings settings )
	{
		trace = tr;
		traceTransform = TraceTransform( settings );
	}

	public void Delete()
	{
		DragCancelSource.Cancel();
		Deleted = true;

		OnDestroy();
	}

	protected async Task WaitForLoad()
	{
		if ( IsInitialized ) return;
		var token = DragCancelSource.Token;

		while ( !IsInitialized )
		{
			token.ThrowIfCancellationRequested();
			await Task.Yield();
		}
	}

	/// <summary>
	/// If the asset is a url, we'll download it. If it's a path, we'll try to return it.
	/// </summary>
	protected async Task<Asset> InstallAsset( string urlPath, CancellationToken token )
	{
		if ( !Uri.TryCreate( urlPath, UriKind.Absolute, out var uri ) || uri.IsFile || uri.Scheme != "https" )
		{
			return AssetSystem.FindByPath( urlPath );
		}

		PackageStatus = "Fetching Package";

		var package = await Package.Fetch( uri.ToString(), false );
		if ( package is not null )
		{
			var b = Bounds;
			b.Mins = package.GetMeta<Vector3>( "RenderMins" );
			b.Maxs = package.GetMeta<Vector3>( "RenderMaxs" );
			Bounds = b;

			var boffset = b.ClosestPoint( Vector3.Down * 10000 );
			PivotPosition = boffset;
		}

		PackageStatus = "Downloading - 0%";
		var a = await AssetSystem.InstallAsync( uri.ToString(), true, f => PackageStatus = $"Downloading - {f * 100.0f:n0}%", token );
		PackageStatus = null;

		return a;
	}

	private Transform TraceTransform( Gizmo.SceneSettings settings )
	{
		var rot = Rotation.LookAt( trace.Normal, Vector3.Up ) * Rotation.From( 90, 0, 0 ) * Rotation;
		var pos = trace.EndPosition;
		if ( EditorPreferences.BoundsPlacement )
		{
			pos += trace.Normal * PivotPosition.Length;
		}

		var isCtrlPressed = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl );
		var snap = settings.SnapToGrid;
		if ( snap && isCtrlPressed ) snap = false;
		else if ( !snap && isCtrlPressed ) snap = true;

		if ( snap )
		{
			var spacing = settings.GridSpacing;
			var localPos = rot.Inverse * pos;
			localPos = localPos.SnapToGrid( spacing, true, true, false );
			pos = rot * localPos;
		}

		return new Transform( pos, rot, Scale );
	}

	/// <summary>
	/// Checks whether a local file path matches a registered DropObject extension.
	/// Does not handle cloud package URLs, only use this for quick validation only (like on hover).
	/// </summary>
	public static bool CanCreateDropFor( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return false;

		var dropObjs = EditorTypeLibrary.GetTypesWithAttribute<DropObjectAttribute>();

		foreach ( var obj in dropObjs )
		{
			if ( obj.Attribute.Extensions.Any( path.EndsWith ) )
				return true;
		}

		return false;
	}

	public static async Task<BaseDropObject> CreateDropFor( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return null;

		string type = "unknown";

		if ( Uri.TryCreate( text, UriKind.Absolute, out var url ) && !url.IsFile && url.Scheme == "https" )
		{
			var package = await Package.FetchAsync( text, false );
			if ( package is not null )
			{
				type = package.TypeName;
			}
		}

		var dropObjs = EditorTypeLibrary.GetTypesWithAttribute<DropObjectAttribute>();

		foreach ( var obj in dropObjs )
		{
			var attribute = obj.Attribute;
			if ( (!string.IsNullOrEmpty( attribute.Type ) && attribute.Type == type) || attribute.Extensions.Any( text.EndsWith ) )
			{
				return EditorTypeLibrary.Create<BaseDropObject>( obj.Type.TargetType );
			}
		}

		return null;
	}
}
