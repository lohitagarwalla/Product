using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Repositories;
using ResourceBooking.Infrastructure.Services;
using ResourceBooking.Web.Filters;
using ResourceBooking.Web.Middleware;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Native .NET 10 OpenAPI Support
builder.Services.AddOpenApi();

// 2. Add Output Caching Service (used to cache the http response)
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder => builder.Expire(TimeSpan.FromSeconds(30)));
    options.AddPolicy("ResourceCatalogCache", policy =>
        policy.Expire(TimeSpan.FromMinutes(5))
              .Tag("resources"));
});

// can use AddMemoryCache() for caching of db queries

// 1. Get Connection String
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// 2. Register DbContext with SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 3. Register ASP.NET Core Identity with Roles
// AddIdentity<>() also configures and uses cookies for athenticate/authorize by default. but in this app we are using jwt for authentication. so commenting out this section. and instead will use AddIdentityCore
//builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
//{
//    options.Password.RequireDigit = true;
//    options.Password.RequireLowercase = true;
//    options.Password.RequireUppercase = true;
//    options.Password.RequireNonAlphanumeric = false;
//    options.Password.RequiredLength = 8;
//    options.User.RequireUniqueEmail = true;
//})
//.AddEntityFrameworkStores<ApplicationDbContext>()
//.AddDefaultTokenProviders();

// 3. inentity core for managing user and roles and passwords
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Register Generic & Custom Repositories
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IBookingRepository, BookingRepository>();

// Register Domain Services
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ITodoService, TodoService>();

// 1. Add Custom Account Service
builder.Services.AddScoped<IAccountService, AccountService>();

// (we are not building mvc, so not using cookies for authentication.)
// 2. Configure Cookie Authentication for MVC
//builder.Services.ConfigureApplicationCookie(options =>
//{
//    options.Cookie.HttpOnly = true;
//    options.ExpireTimeSpan = TimeSpan.FromHours(8);
//    options.LoginPath = "/Account/Login";
//    options.AccessDeniedPath = "/Account/AccessDenied";
//    options.SlidingExpiration = true;
//});

// 3. Configure JWT Bearer Authentication (for Web API / Bearer Token endpoints)
var jwtSecret = builder.Configuration["JwtSettings:Secret"]
    ?? "SuperSecretSecurityKeyThatIsAtLeast32BytesLongForHS256!";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            JwtBearerDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.SaveToken = true;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "ResourceBookingApi",
            ValidAudience = builder.Configuration["JwtSettings:Audience"] ?? "ResourceBookingClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };
    });

// 4. Configure Custom Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminOnly", policy =>
        policy.RequireRole("Admin"));

    options.AddPolicy("RequireITDepartment", policy =>
        policy.RequireClaim("Department", "IT Ops", "IT Infrastructure"));
});

// Add services to the container.
// (we are not building views for now. only learning web api (backend))
//builder.Services.AddControllersWithViews();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<LogExecutionTimeFilter>();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// 2. Add Exception Middleware as the VERY FIRST component in the HTTP pipeline
app.UseMiddleware<GlobalExceptionMiddleware>();

//// Configure the HTTP request pipeline.
//if (!app.Environment.IsDevelopment())
//{
//    app.UseExceptionHandler("/Home/Error");
//    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
//    app.UseHsts();
//}

// 3. Configure Development Environment OpenAPI & Scalar UI Routes
if (app.Environment.IsDevelopment())
{
    // Generates JSON schema at /openapi/v1.json
    app.MapOpenApi();

    // Renders interactive Scalar UI at /scalar/v1
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Resource Booking Engine API")
               .WithTheme(ScalarTheme.Purple)
               .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Axios);
    });
}

// Seed roles, admin user, and initial catalog data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        await DbInitializer.SeedAsync(context, userManager, roleManager);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"), branch =>
    branch.UseStatusCodePagesWithReExecute("/Documentation"));

app.UseStaticFiles();
app.UseRouting();

app.UseCors("ReactApp");

app.UseAuthentication();
app.UseAuthorization();

app.UseOutputCache(); // Add here

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Welcome}/{action=Documentation}");

app.Run();

