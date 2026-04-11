namespace Editor;

public enum RepeatActionKind
{
    None,
    Translate,
    Rotate,
    Scale,
    Shear
}

/// <summary>
/// Stores the last transform action so it can be replayed.
/// </summary>
public static class RepeatActionTool
{
    public static RepeatActionKind Kind { get; private set; } = RepeatActionKind.None;
    public static Vector3 TranslateDelta { get; private set; }
    public static Rotation RotationDelta { get; private set; }
    public static Vector3 RotationCenter { get; private set; }
    public static Rotation RotationBasis { get; private set; }
    public static Vector3 ScaleDelta { get; private set; }
    public static Vector3 ShearAxis { get; private set; }
    public static Vector3 constraintAxis { get; private set; }
    public static float ShearAmount { get; private set; }
    public static bool WasDuplication { get; private set; }

    public static bool HasAction => Kind != RepeatActionKind.None;
    public static bool IsReplaying { get; private set; } 
    
    static RepeatActionKind _pendingKind;
    static Vector3 _pendingTranslate;
    static Rotation _pendingRotate;
    static Vector3 _pendingRotationCenter;
    static Rotation _pendingRotationBasis;
    static Vector3 _pendingScale;
    static Vector3 _pendingShearAxis;
    static Vector3 _pendingConstraintAxis;
    static float _pendingShearAmount;
    static bool _pendingDuplication;
    static SceneEditorSession _registeredSession;

    public static void BeginCapture( bool isDuplication )
    {
        _pendingKind = RepeatActionKind.None;
        _pendingDuplication = isDuplication;
    }

    public static void RecordTranslate( Vector3 delta )
    {
        if ( IsReplaying ) return;
        _pendingKind = RepeatActionKind.Translate;
        _pendingTranslate = delta;
    }

    public static void RecordRotate( Rotation delta, Vector3 center, Rotation basis )
    {
        if ( IsReplaying ) return;
        _pendingKind = RepeatActionKind.Rotate;
        _pendingRotate = delta;
        _pendingRotationCenter = center;
        _pendingRotationBasis = basis;
        Log.Info($"Recorded rotation: {delta} around {center}");
    }

    public static void RecordScale( Vector3 delta )
    {
        if ( IsReplaying ) return;
        _pendingKind = RepeatActionKind.Scale;
        _pendingScale = delta;
    }

    public static void RecordShear( Vector3 axis, Vector3 constraintAxis, float amount )
    {
        if ( IsReplaying ) return;
        _pendingKind = RepeatActionKind.Shear;
        _pendingShearAxis = axis;
        _pendingConstraintAxis = constraintAxis;
        _pendingShearAmount = amount;
    }

    public static void Commit()
    {
        if ( _pendingKind == RepeatActionKind.None )
        {
            Log.Info($"RepeatActionTool: No action to commit");
            return;
        }

        Kind = _pendingKind;
        TranslateDelta = _pendingTranslate;
        RotationDelta = _pendingRotate;
        RotationCenter = _pendingRotationCenter;
        RotationBasis = _pendingRotationBasis;
        ScaleDelta = _pendingScale;
        ShearAxis = _pendingShearAxis;
        constraintAxis = _pendingConstraintAxis;
        ShearAmount = _pendingShearAmount;
        WasDuplication = _pendingDuplication;

        RegisterUndoListener();
    }

    public static void Clear()
    {
        Log.Info($"RepeatActionTool: Clearing action");
        Kind = RepeatActionKind.None;
        WasDuplication = false;
    }

    static void RegisterUndoListener()
    {
        var session = SceneEditorSession.Active;
        if ( session != null && session != _registeredSession )
        {
            session.UndoSystem.OnUndo += ( _ ) => Clear();
            _registeredSession = session;
        }
    }

    [Menu("Editor", "Scene/Repeat Last Action")]
    [Shortcut("editor.repeat-action", "CTRL+ALT+G")]
    public static void Execute()
    {
        if (!HasAction) 
        {
            Log.Info($"RepeatActionTool: No action to execute");
            return;
        }
        if ( SceneEditorSession.Active is null ) return;

        IsReplaying = true;
        try
        {
            var tools = SceneViewWidget.Current?.Tools;
            var subTool = tools?.CurrentSubTool ?? tools?.CurrentTool;

            if ( subTool is MeshEditor.SelectionTool meshTool )
            {
                meshTool.ExecuteRepeatAction();
            }
            else
            {
                // This is ugly.
                ExecuteForGameObjects();
            }
        }
        finally
        {
            IsReplaying = false;
        }
    }

    static void ExecuteForGameObjects()
    {
        using var scope = SceneEditorSession.Scope();

        var gos = EditorScene.Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) ).ToArray();
        if ( gos.Length == 0 ) return;

        var scopeBuilder = SceneEditorSession.Active.UndoScope( "Repeat Last Action" );

        if ( WasDuplication )
        {
            scopeBuilder = scopeBuilder.WithGameObjectCreations();
        }
        else
        {
            scopeBuilder = scopeBuilder.WithGameObjectChanges( gos, GameObjectUndoFlags.All );
        }

        using var undoScope = scopeBuilder.Push();

        if ( WasDuplication )
        {
            SceneEditorMenus.DuplicateInternal();
            gos = EditorScene.Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) ).ToArray();
        }

        switch ( Kind )
        {
            case RepeatActionKind.Translate:
                foreach ( var go in gos )
                {
                    go.WorldPosition += TranslateDelta;
                    go.DispatchEdited( nameof( GameObject.LocalPosition ) );
                }
                break;
            case RepeatActionKind.Rotate:
                var center = RotationCenter;
                foreach ( var go in gos )
                {
                    var position = go.WorldPosition - center;
                    position *= RotationDelta;
                    position += center;
                    go.WorldPosition = position;
                    go.WorldRotation = RotationDelta * go.WorldRotation;
                    go.DispatchEdited( nameof( GameObject.LocalPosition ) );
                    go.DispatchEdited( nameof( GameObject.LocalRotation ) );
                }
                break;
            case RepeatActionKind.Scale:
                foreach ( var go in gos )
                {
                    go.WorldScale += ScaleDelta;
                    go.DispatchEdited( nameof( GameObject.LocalScale ) );
                }
                break;
        }
    }
}