namespace Groundwork.Models;

public record CreateProjectRequest(string Name, string? Description);
public record UpdateProjectRequest(string? Name, string? Description);

public record AuthRequest(string Key);
