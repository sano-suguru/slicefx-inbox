using Inbox.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Auth: sessionStorage-backed workspace-token holder + DelegatingHandler.
// Token is issued by POST /api/workspaces and entered at runtime via /login —
// never baked into the build artifact.
builder.Services.AddScoped<ISessionStorage, SessionStorage>();
builder.Services.AddSingleton<RefreshTokenHolder>();
builder.Services.AddTransient<RefreshTokenHandler>();

// Named HttpClient — same-origin (SPA served at /, API at /api/...).
// RefreshTokenHandler injects X-Workspace-Token when the holder has a token.
builder.Services
    .AddHttpClient(nameof(SliceApiClient),
        c => c.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<RefreshTokenHandler>();

// Typed client scoped per component — built from the named client factory.
builder.Services.AddScoped(sp =>
    new SliceApiClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SliceApiClient))));

await builder.Build().RunAsync();
