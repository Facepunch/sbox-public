namespace Editor.AnimGraph;

/// <summary>
/// Marks a node that compiles against the transition it lives in, reading the compiler's source
/// state and blend duration. The compiler only emits each node once, so one of these can't be
/// shared between transitions - it would silently bind every use to the first transition compiled.
/// </summary>
internal interface ITransitionScopedNode;
