using Microsoft.AspNetCore.Mvc;

namespace Gateway.Areas.Admin.Models.Users
{
    public class UserListViewModel : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
