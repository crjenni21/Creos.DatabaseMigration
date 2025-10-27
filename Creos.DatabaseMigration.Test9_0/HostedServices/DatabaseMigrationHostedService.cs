
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Creos.DatabaseMigration.Test9_0.HostedServices
{
    internal class DatabaseMigrationHostedService : IHostedService
    {
        private readonly ILogger<DatabaseMigrationHostedService> _logger;
        private readonly IConfiguration _configuration;
        public DatabaseMigrationHostedService(ILogger<DatabaseMigrationHostedService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {

            var migrationHelper = new DatabaseMigrationHelper(_logger);

            var migrationResults = await migrationHelper.UpdateDatabaseAsync(new DatabaseMigrationModel
            {
                ConnectionStrings = new List<ConnectionStringInfo> { new(_configuration.GetSection("ConnectionStrings:TestCreos").Value) },
                ScriptProjectName = "Creos.database.test",
                FolderNameWithAllScripts = "VersionScripts",
                MaxThreads_Concurrency = 2,
                Timeout = 45,
                DatabaseType = Models.DatabaseType.SqlServer
            }, cancellationToken);

            _logger.LogDebug("Overall Success: {success}", migrationResults.OverallSuccess);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
