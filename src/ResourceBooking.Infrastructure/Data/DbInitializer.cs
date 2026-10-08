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
        // 1. Apply any pending database migrations automatically on startup
        if (context.Database.IsRelational() && (await context.Database.GetPendingMigrationsAsync()).Any())
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

        // 3. Seed the demo admin only in Development
        if (seedDemoAdmin)
        {
            var adminEmail = "admin@company.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FirstName = "System",
                    LastName = "Administrator",
                    Department = "IT Ops",
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(adminUser, "Admin123!");
                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, Roles.Admin);
                }
            }
        }
    }
}
