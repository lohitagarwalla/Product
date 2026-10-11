using ResourceBooking.Core.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Enums;

namespace ResourceBooking.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        bool seedDemoAdmin)
    {
        // 1. Apply migrations included in the deployed assembly before seeding.
        // MigrateAsync checks migration history; non-relational test stores skip this step.
        if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
        }

        // 2. Seed Default Roles
        string[] roles = [Roles.Admin, Roles.Employee, Roles.Customer, Roles.Owner];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }
}
