namespace Gateway.Areas.Admin.Models.Users
{
    public class UserListViewModel
    {
        public string? SearchTerm { get; set; }

        public List<UserListItemViewModel> Users { get; set; } = new();
    }
}
