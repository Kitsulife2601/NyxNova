namespace NovaBrowser.App.Models;

public sealed class Bookmark
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Bookmark";
    public string Url { get; set; } = "";
    public string Folder { get; set; } = "Bookmarks";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
