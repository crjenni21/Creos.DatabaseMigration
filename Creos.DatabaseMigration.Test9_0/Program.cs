
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Exceptions;
using Creos.DatabaseMigration.Test9_0.HostedServices;

namespace Creos.DatabaseMigration.Test9_0
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.WithExceptionDetails()
                .CreateLogger();

            builder.Services.AddResponseCompression(options =>
            {
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            });

            builder.Services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = System.IO.Compression.CompressionLevel.Fastest;
            });

            builder.Services.AddSingleton<BrotliCompressionProvider>();

            builder.Services.AddControllersWithViews();
            builder.Services.AddHostedService<DatabaseMigrationHostedService>();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
                app.UseDeveloperExceptionPage();

            app.UseResponseCompression();

            app.UseRouting();

            app.MapControllers();

            app.UseSerilogRequestLogging();

            app.Run();
        }
    }
}