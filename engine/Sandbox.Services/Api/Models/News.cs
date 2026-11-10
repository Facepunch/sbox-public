namespace Sandbox.Services;

public class NewsPostDto
{
	public Guid Id { get; set; }
	public DateTimeOffset Created { get; set; }
	public string Title { get; set; }
	public string Summary { get; set; }
	public string Url { get; set; }
	public Player Author { get; set; }
	public string Package { get; set; }
	public string Media { get; set; }
	public string Image { get; set; }
	public string ImageThumb { get; set; }
	public NewsSectionDto[] Sections { get; set; }
}

public class NewsSectionDto
{
	public Guid Id { get; set; }
	public string Title { get; set; }
	public Player Author { get; set; }
	public int SortOrder { get; set; }
	public string Contents { get; set; }
	public string Slug { get; set; }
}

