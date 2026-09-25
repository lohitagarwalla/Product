using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Enums;

namespace ResourceBooking.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        // 1. Apply any pending database migrations automatically on startup
        if (context.Database.IsRelational() && (await context.Database.GetPendingMigrationsAsync()).Any())
        {
            await context.Database.MigrateAsync();
        }

        // 2. Seed Default Roles
        string[] roles = ["Admin", "Employee"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // 3. Seed Default Admin User
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
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }

        // 4. Seed Initial Resources
        if (!await context.Resources.AnyAsync())
        {
            var initialResources = new List<Resource>
            {
                new()
                {
                    Name = "Executive Boardroom A",
                    Description = "Equipped with 4K AV display, conferencing mic, and seating for 16.",
                    Type = ResourceType.ConferenceRoom,
                    Capacity = 16,
                    Location = "Building 1 - Floor 3",
                    IsActive = true
                },
                new()
                {
                    Name = "Dell XPS Workstation #04",
                    Description = "High-performance laptop for design and build engineering workloads.",
                    Type = ResourceType.Hardware,
                    Capacity = 1,
                    Location = "IT Helpdesk - Rack B",
                    IsActive = true
                },
                new()
                {
                    Name = "Company Fleet SUV",
                    Description = "Hybrid vehicle available for client visits and business travel.",
                    Type = ResourceType.Vehicle,
                    Capacity = 5,
                    Location = "P2 Parking Bay 12",
                    IsActive = true
                }
            };

            await context.Resources.AddRangeAsync(initialResources);
            await context.SaveChangesAsync();
        }
    }
}
