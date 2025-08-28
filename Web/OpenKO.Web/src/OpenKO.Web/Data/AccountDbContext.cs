using Microsoft.EntityFrameworkCore;
using OpenKO.Web.Data.Models.Account;

namespace OpenKO.Web.Data;

public class AccountDbContext : DbContext
{
    public DbSet<TblUser> TblUsers { get; set; }

    public AccountDbContext(DbContextOptions options)
        : base(options) { }
}
