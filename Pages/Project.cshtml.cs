using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Groundwork.Pages;

public class ProjectModel : PageModel
{
    public Guid Id { get; private set; }
    public void OnGet(Guid id) => Id = id;
}
