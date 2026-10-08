using ResourceBooking.Core.Constants;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Repositories;
using ResourceBooking.Infrastructure.Services;
using ResourceBooking.Infrastructure.Storage;
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
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IAddressService, AddressService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddSingleton<IImageStorage, LocalImageStorage>();
builder.Services.AddScoped<ImageCleanupService>();
builder.Services.AddHostedService<ImageCleanupWorker>();
builder.Services.AddOptions<ImageStorageOptions>()
    .Bind(builder.Configuration.GetSection("ImageStorage"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Image storage root is required.")
    .Validate(o => o.MaxFileSizeBytes > 0 && o.MaxFileSizeBytes <= 20 * 1024 * 1024,
        "Image size must be between 1 byte and 20 MB.")
    .Validate(o => o.MaxDimension > 0 && o.MaxDimension <= 10000 && o.MaxPixels > 0 && o.MaxPixels <= 25000000,
        "Image dimension limits are invalid.")
    .Validate(o => o.MaxImagesPerProduct > 0 && o.MaxImagesPerProduct <= 100 && o.DeletedProductRetentionDays >= 0,
        "Image count or retention settings are invalid.")
    .ValidateOnStart();

// 1. Add Custom Account Service
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AuthRequestFilter>();
builder.Services.AddOptions<AuthSessionOptions>()
    .Bind(builder.Configuration.GetSection(AuthSessionOptions.SectionName))
    .Validate(o => o.AccessTokenMinutes is > 0 and <= 60, "Access-token lifetime must be 1-60 minutes.")
    .Validate(o => o.RefreshSessionDays is > 0 and <= 90, "Refresh-session lifetime must be 1-90 days.")
    .Validate(o => o.AllowedOrigins.Length > 0 && o.AllowedOrigins.All(origin =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || uri.Scheme == "http") &&
        origin == uri.GetLeftPart(UriPartial.Authority)), "Allowed origins must be exact HTTP(S) origins without paths or trailing slashes.")
    .ValidateOnStart();

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
        policy.RequireRole(Roles.Admin));

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

var authOrigins = builder.Configuration.GetSection("AuthSession:AllowedOrigins").Get<string[]>()
    ?? new AuthSessionOptions().AllowedOrigins;
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactApp", policy =>
    {
        policy.WithOrigins(authOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
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

// Seed roles and initial catalog data, plus the demo admin in Development
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        await DbInitializer.SeedAsync(
            context,
            userManager,
            roleManager,
            seedDemoAdmin: app.Environment.IsDevelopment());
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

app.UseOutputCache();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Welcome}/{action=Documentation}");

app.Run();

