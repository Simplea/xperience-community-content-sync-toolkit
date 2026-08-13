using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CMS.Base;

using DancingGoat;
using DancingGoat.EmailComponents;
using DancingGoat.Helpers.Generators;
using DancingGoat.Models;

using Kentico.Activities.Web.Mvc;
using Kentico.Commerce.Web.Mvc;
using Kentico.Content.Web.Mvc.Routing;
using Kentico.EmailBuilder.Web.Mvc;
using Kentico.Membership;
using Kentico.OnlineMarketing.Web.Mvc;
using Kentico.PageBuilder.Web.Mvc;
using Kentico.Web.Mvc;
using Kentico.Xperience.Mjml;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Samples.DancingGoat;

using XperienceCommunity.ContentSyncToolkit;
using XperienceCommunity.ContentSyncToolkit.Http;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddKentico(features =>
{
    features.UsePageBuilder(new PageBuilderOptions
    {
        DefaultSectionIdentifier = ComponentIdentifiers.SINGLE_COLUMN_SECTION,
        RegisterDefaultSection = false,
        ContentTypeNames = new[]
        {
            LandingPage.CONTENT_TYPE_NAME,
            ContactsPage.CONTENT_TYPE_NAME,
            ArticlePage.CONTENT_TYPE_NAME
        }
    });

    features.UseEmailBuilder();
    features.UseWebPageRouting();
    features.UseEmailMarketing();
    features.UseEmailStatisticsLogging();
    features.UseActivityTracking();
    features.UseCommerce();
});

builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

builder.Services.AddLocalization()
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (type, factory) => factory.Create(typeof(SharedResources));
    });

builder.Services.AddDancingGoatServices();
builder.Services.AddSingleton<IEmailActivityTrackingEvaluator, EmailActivityTrackingEvaluator>();

// TEMPORARY: manual two-instance test rig for the content sync toolkit foundation. Not part of the
// packaged sample. Every value comes from configuration (environment variables at launch time), so
// no secret or URL is ever hardcoded here — see docs/specs/content-inventory-foundation.md.
builder.Services.AddContentSyncToolkit();
builder.Services.Configure<ContentSyncToolkitOptions>(builder.Configuration.GetSection("ContentSyncToolkit"));

ConfigureEmailBuilder(builder.Services);
ConfigureMembershipServices(builder.Services);

if (builder.Environment.IsDevelopment())
{
    builder.Services.Configure<UrlResolveOptions>(options => options.UseSSL = false);
}

var app = builder.Build();

app.InitKentico();

app.UseStaticFiles();

app.UseCookiePolicy();

app.UseAuthentication();


app.UseKentico();

app.UseAuthorization();

app.UseStatusCodePagesWithReExecute("/error/{0}");

app.Kentico().MapRoutes();

app.MapControllerRoute(
   name: "error",
   pattern: "error/{code}",
   defaults: new { controller = "HttpErrors", action = "Error" }
);

app.MapControllerRoute(
    name: DancingGoatConstants.DEFAULT_ROUTE_NAME,
    pattern: $"{{{WebPageRoutingOptions.LANGUAGE_ROUTE_VALUE_KEY}}}/{{controller}}/{{action}}",
    constraints: new
    {
        controller = DancingGoatConstants.CONSTRAINT_FOR_NON_ROUTER_PAGE_CONTROLLERS
    }
);

app.MapControllerRoute(
    name: DancingGoatConstants.DEFAULT_ROUTE_WITHOUT_LANGUAGE_PREFIX_NAME,
    pattern: "{controller}/{action}",
    constraints: new
    {
        controller = DancingGoatConstants.CONSTRAINT_FOR_NON_ROUTER_PAGE_CONTROLLERS
    }
);

// TEMPORARY: manual test rig for the content sync toolkit foundation — there is no admin UI yet
// (that's a later slice), so these dev-only endpoints are the only way to see the diff result.
// Remove once the sync-status admin page exists. Never mapped outside Development.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/content-sync-toolkit/local/web-pages",
        (string channelName, string languageName, ILocalContentInventoryService service, CancellationToken ct) =>
            service.GetWebPagesAsync(channelName, languageName, ct));

    app.MapGet("/dev/content-sync-toolkit/local/content-hub-items",
        (string workspaceName, string languageName, ILocalContentInventoryService service, CancellationToken ct) =>
            service.GetContentHubItemsAsync(workspaceName, languageName, ct));

    app.MapGet("/dev/content-sync-toolkit/remote/web-pages",
        (string channelName, string languageName, IContentInventoryClient client, CancellationToken ct) =>
            client.GetWebPagesAsync(channelName, languageName, ct));

    app.MapGet("/dev/content-sync-toolkit/remote/content-hub-items",
        (string workspaceName, string languageName, IContentInventoryClient client, CancellationToken ct) =>
            client.GetContentHubItemsAsync(workspaceName, languageName, ct));

    app.MapGet("/dev/content-sync-toolkit/status/web-pages",
        (string channelName, string languageName, IContentSyncStatusService service, CancellationToken ct) =>
            service.GetWebPageSyncStatusAsync(channelName, languageName, ct));

    app.MapGet("/dev/content-sync-toolkit/status/content-hub-items",
        (string workspaceName, string languageName, IContentSyncStatusService service, CancellationToken ct) =>
            service.GetContentHubSyncStatusAsync(workspaceName, languageName, ct));
}

app.Run();


static void ConfigureMembershipServices(IServiceCollection services)
{
    services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 0;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequiredUniqueChars = 0;
        // Ensures, that disabled member cannot sign in.
        options.SignIn.RequireConfirmedAccount = true;
    })
        .AddUserStore<ApplicationUserStore<ApplicationUser>>()
        .AddRoleStore<ApplicationRoleStore<ApplicationRole>>()
        .AddUserManager<UserManager<ApplicationUser>>()
        .AddRoleManager<RoleManager<ApplicationRole>>()
        .AddSignInManager<SignInManager<ApplicationUser>>();

    services.ConfigureApplicationCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.LoginPath = new PathString("/account/login");
        options.AccessDeniedPath = new PathString("/error/403");
        options.Events.OnRedirectToLogin = ctx =>
        {
            var factory = ctx.HttpContext.RequestServices.GetRequiredService<IUrlHelperFactory>();
            var urlHelper = factory.GetUrlHelper(new ActionContext(ctx.HttpContext, new RouteData(ctx.HttpContext.Request.RouteValues), new ActionDescriptor()));
            var url = urlHelper.Action("Login", "Account") + new Uri(ctx.RedirectUri).Query;

            ctx.Response.Redirect(url);

            return Task.CompletedTask;
        };
    });

    services.Configure<AdminIdentityOptions>(options =>
    {
        // The expiration time span of 8 hours is set for demo purposes only. In production environments, set expiration according to best practices.
        options.AuthenticationOptions.ExpireTimeSpan = TimeSpan.FromHours(8);

        // The forbidden passwords are set for demo purposes only. In production environments, set password options according to best practices.
        var companySpecificKeywords = new List<string> { "kentico", "dancinggoat", "admin", "coffee" };
        var specificNumberCombinations = new List<string> { "2023", "23", "2024", "24", "2025", "25" };
        options.PasswordOptions.ForbiddenPasswords = ForbiddenPasswordGenerator.Generate(companySpecificKeywords, specificNumberCombinations);
    });

    services.AddAuthorization();
}


static void ConfigureEmailBuilder(IServiceCollection services)
{
    services.Configure((EmailBuilderOptions options) =>
    {
        options.AllowedEmailContentTypeNames = ["DancingGoat.BuilderEmail"];
        options.RegisterDefaultSection = false;
        options.DefaultSectionIdentifier = DancingGoatFullWidthEmailSection.IDENTIFIER;
    });

    services.AddMjmlForEmails();
}
