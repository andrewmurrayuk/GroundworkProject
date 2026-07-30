using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Groundwork.Pages;

[AllowAnonymous]
public class LoginModel : PageModel
{
    public void OnGet() { }
}
