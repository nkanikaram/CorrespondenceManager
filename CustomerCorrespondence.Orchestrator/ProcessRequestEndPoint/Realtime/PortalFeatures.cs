using Microsoft.AspNetCore.Authentication.JwtBearer;
using ProcessRequestEndPoint.Alerts;
using ProcessRequestEndPoint.Auth;
using ProcessRequestEndPoint.Letters;
using ProcessRequestEndPoint.Records;

namespace ProcessRequestEndPoint.Realtime
{
    public static class PortalFeatures
    {
        /// <summary>
        /// Record details, recycle bin + retention job, letter templates, alerts job, and live updates (SignalR).
        /// Settings: "Retention", "Letters", "Alerts" sections.
        /// </summary>
        public static WebApplicationBuilder AddPortalFeatures(this WebApplicationBuilder builder)
        {
            var services = builder.Services;
            var config = builder.Configuration;

            services.Configure<RetentionOptions>(config.GetSection(RetentionOptions.SectionName));
            services.Configure<LetterBrandingOptions>(config.GetSection(LetterBrandingOptions.SectionName));
            services.Configure<AlertOptions>(config.GetSection(AlertOptions.SectionName));

            services.AddSingleton<CorrespondenceDetailsRepository>();
            services.AddSingleton<RecycleBinRepository>();
            services.AddSingleton<LetterTemplateRepository>();
            services.AddSingleton<AlertRepository>();
            services.AddSingleton<ChangeFeedRepository>();
            services.AddSingleton<ProcessRequestEndPoint.Sla.SlaService>();
            services.AddHttpClient("alerts");
            services.AddSingleton<AlertNotifier>();

            services.AddSignalR();
            services.AddSingleton<PortalEvents>();
            services.AddHostedService<ChangeFeedWorker>();

            if (config.GetSection(RetentionOptions.SectionName).Get<RetentionOptions>()?.Enabled ?? true)
                services.AddHostedService<RetentionWorker>();
            if (config.GetSection(AlertOptions.SectionName).Get<AlertOptions>()?.Enabled ?? true)
                services.AddHostedService<AlertWorker>();

            // Browsers can't send headers on a WebSocket, so the hub accepts the portal token as ?access_token=.
            services.PostConfigure<JwtBearerOptions>(PortalAuthDefaults.Scheme, jwt =>
            {
                var previous = jwt.Events?.OnMessageReceived;
                jwt.Events ??= new JwtBearerEvents();
                jwt.Events.OnMessageReceived = async ctx =>
                {
                    if (previous != null) await previous(ctx);
                    var token = ctx.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(PortalHub.Path)) ctx.Token = token;
                };
            });
            return builder;
        }

        public static WebApplication MapPortalFeatures(this WebApplication app)
        {
            app.MapHub<PortalHub>(PortalHub.Path);
            return app;
        }
    }
}
