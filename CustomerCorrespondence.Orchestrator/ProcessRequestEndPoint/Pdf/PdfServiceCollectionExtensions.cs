namespace ProcessRequestEndPoint.Pdf
{
    public static class PdfServiceCollectionExtensions
    {
        /// <summary>
        /// Registers correspondence PDF generation: payload decoding, rendering, Blob Storage, the tracking
        /// repository and (when PdfGeneration:Enabled) the background worker. Without Blob settings the portal's
        /// PDF endpoints answer 503 and the worker isn't started.
        /// </summary>
        public static WebApplicationBuilder AddCorrespondencePdfs(this WebApplicationBuilder builder)
        {
            var services = builder.Services;
            var section = builder.Configuration.GetSection(PdfGenerationOptions.SectionName);
            services.Configure<PdfGenerationOptions>(section);
            var options = section.Get<PdfGenerationOptions>() ?? new PdfGenerationOptions();

            services.AddSingleton<CorrespondencePdfRepository>();
            services.AddScoped<CorrespondencePayloadDecoder>();

            if (!options.StorageConfigured) return builder;

            services.AddSingleton<PdfBlobStorage>();
            services.AddScoped<CorrespondencePdfService>();
            if (options.Enabled) services.AddHostedService<CorrespondencePdfWorker>();
            return builder;
        }
    }
}
