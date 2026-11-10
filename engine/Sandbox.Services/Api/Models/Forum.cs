namespace Sandbox.Services;

/// <summary>
/// How a forum list is ordered.
/// </summary>
public enum ForumSort
{
	/// <summary>The site's display order, categories first.</summary>
	Order,
	/// <summary>Most recently posted-in first.</summary>
	LastPost,
}

/// <summary>
/// How a thread list is ordered.
/// </summary>
public enum ThreadSort
{
	/// <summary>Most recent reply first.</summary>
	LastPost,
	/// <summary>Newest thread first.</summary>
	Created,
	/// <summary>Most viewed first.</summary>
	Views,
	/// <summary>Most replies first.</summary>
	Replies,
}

/// <summary>
/// Narrows a thread list by its solved state.
/// </summary>
public enum ThreadFilter
{
	All,
	Open,
	Solved,
}

/// <summary>
/// How a post list is ordered.
/// </summary>
public enum PostSort
{
	/// <summary>Reading order: the opening post first.</summary>
	Oldest,
	/// <summary>Latest reply first.</summary>
	Newest,
}

/// <summary>
/// A forum as the public API sees it.
/// </summary>
public class ForumDto
{
	public int Id { get; set; }
	public string Slug { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }
	public string Icon { get; set; }
	public string Url { get; set; }

	/// <summary>Slug of the parent forum, null at the top level.</summary>
	public string Parent { get; set; }

	/// <summary>A heading that groups forums; it holds no threads itself.</summary>
	public bool Category { get; set; }
	public bool Locked { get; set; }

	/// <summary>Only ever true for admins; hidden forums aren't returned to anyone else.</summary>
	public bool Hidden { get; set; }

	public int Threads { get; set; }
	public int Posts { get; set; }
	public DateTimeOffset? LastPost { get; set; }
}

/// <summary>
/// A thread as the public API sees it.
/// </summary>
public class ForumThreadDto
{
	public long Id { get; set; }
	public string Forum { get; set; }
	public string Title { get; set; }
	public string Url { get; set; }
	public Player Author { get; set; }
	public DateTimeOffset Created { get; set; }

	/// <summary>When the last post was made.</summary>
	public DateTimeOffset Updated { get; set; }
	public Player LastPostBy { get; set; }

	/// <summary>Post count, including the opening post.</summary>
	public int Posts { get; set; }
	public long Views { get; set; }
	public bool Sticky { get; set; }
	public bool Closed { get; set; }
	public bool Solved { get; set; }

	/// <summary>Only ever true for admins; hidden threads aren't returned to anyone else.</summary>
	public bool Hidden { get; set; }
}

/// <summary>
/// A post as the public API sees it.
/// </summary>
public class ForumPostDto
{
	public long Id { get; set; }
	public long Thread { get; set; }
	public string Url { get; set; }

	/// <summary>Null when the post is hidden and the caller isn't an admin.</summary>
	public Player Author { get; set; }
	public DateTimeOffset Created { get; set; }
	public DateTimeOffset Edited { get; set; }

	/// <summary>Post body as stored. Null when the post is hidden and the caller isn't an admin.</summary>
	public string Content { get; set; }

	/// <summary>Deleted by a moderator. Kept in the list so post numbering matches the site.</summary>
	public bool Hidden { get; set; }
}
