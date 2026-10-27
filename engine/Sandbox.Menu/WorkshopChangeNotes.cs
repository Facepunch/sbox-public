using System;

namespace Sandbox;

/// <summary>
/// One of a workshop item's change notes - what its creator said when they updated it
/// </summary>
/// <param name="Time">When the update was</param>
/// <param name="Text">What they said, as plain text - can be empty</param>
public readonly record struct WorkshopChangeNote( DateTimeOffset Time, string Text );

/// <summary>
/// A page of a workshop item's change notes, newest first. From <see cref="MenuUtility.FetchWorkshopChangeNotesAsync"/>.
/// </summary>
/// <param name="Notes">This page's notes</param>
/// <param name="Total">How many it has altogether</param>
public sealed record WorkshopChangeNotes( IReadOnlyList<WorkshopChangeNote> Notes, int Total );
