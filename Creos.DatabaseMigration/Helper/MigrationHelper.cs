
using Creos.DatabaseMigration.Exceptions;
using Creos.DatabaseMigration.Models;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Creos.DatabaseMigration.Helper
{

    internal class MigrationHelper
    {
        private readonly ILogger _logger;
        private readonly SqlHelper _sqlHelper;
        private readonly DllHelper _ddlHelper;
        public MigrationHelper(ILogger logger, SqlHelper sqlHelper, DllHelper ddlHelper)
        {
            _logger = logger;
            _sqlHelper = sqlHelper;
            _ddlHelper = ddlHelper;
        }

        public async Task<decimal> GetMaxDbVersionAsync(DatabaseMigrationModel migrationModel, ConnectionStringInfo connectionStringInfo, CancellationToken cancellationToken)
        {
            var sql = string.Empty;
            try
            {
                sql = $"select max(dv.dbversionid) from {migrationModel.SchemaName}.{migrationModel.VersionTable} dv;";
                using var dt = await _sqlHelper.GetDataTableAsync(migrationModel, connectionStringInfo, sql, 30, cancellationToken).ConfigureAwait(false);

                if (dt == null || dt.Rows.Count != 1)
                    return -2;
                else
                {
                    var s = dt.Rows[0][0].ToString();
                    if (decimal.TryParse(s, out decimal result))
                        return result;
                    else
                        return -1;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creos.DatabaseMigration | GetMaxDbVersionAsync | {ConnectionStringName} DatabaseMigration failed on {sql}", connectionStringInfo.ConnectionStringName, sql);
                throw;
            }
        }

        public async Task CreateDatabaseVersionTableAsync(DatabaseMigrationModel migrationModel, ConnectionStringInfo connectionStringInfo, CancellationToken cancellationToken)
        {
            var sql = string.Empty;
            try
            {
                if (migrationModel.DatabaseType == DatabaseType.Postgres)
                    sql = $@"create schema if not exists {migrationModel.SchemaName};";
                else if (migrationModel.DatabaseType == DatabaseType.SqlServer)
                    sql = $@"if not exists (select 1 from sys.schemas where name = N'{migrationModel.SchemaName}') begin exec('create schema [{migrationModel.SchemaName}]'); end ;";
                
                await _sqlHelper.RunNonQuery(migrationModel, connectionStringInfo, sql, 15, cancellationToken).ConfigureAwait(false);


                if (migrationModel.DatabaseType == DatabaseType.Postgres)
                    sql = @$"
                        create table if not exists {migrationModel.SchemaName}.{migrationModel.VersionTable} (
                            dbversionid numeric not null,
                            createutcdatetime timestamp not null,
                            constraint dbversion_pkey primary key (dbversionid));";
                else if (migrationModel.DatabaseType == DatabaseType.SqlServer)
                    sql = @$"
                        if not exists (
		                        select 1 
		                        from sys.tables t 
		                        inner join sys.schemas s on t.schema_id = s.schema_id
			                        and s.name = N'{migrationModel.SchemaName}'
		                        where t.name = N'{migrationModel.VersionTable}')
	                        create table {migrationModel.SchemaName}.{migrationModel.VersionTable} (
                                dbversionid numeric(8,5) not null,
                                createutcdatetime datetime not null,
                                constraint PK__{migrationModel.VersionTable} primary key (dbversionid)
                            );";


                await _sqlHelper.RunNonQuery(migrationModel, connectionStringInfo, sql, 15, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creos.DatabaseMigration | CreateDatabaseVersionTable | DatabaseMigration failed on {ConnectionStringName} {sql}", connectionStringInfo.ConnectionStringName, sql);
                throw;
            }

        }

        public async Task<List<ScriptToExecute>> GetMissingVersionsAsync(DatabaseMigrationModel migrationModel, ConnectionStringInfo connectionStringInfo, CancellationToken cancellationToken)
        {
            try
            {
                var scriptsToExecute = new List<ScriptToExecute>();

                var deployedVersions = await GetDeployedVersionsAsync(migrationModel, connectionStringInfo, cancellationToken).ConfigureAwait(false);
                var maxDeployedVersion = deployedVersions.OrderByDescending(x => x).FirstOrDefault();

                var versionsInAssembly = new List<decimal>();
                foreach (var scriptFile in _ddlHelper.GetValidScriptFilesInAssembly(migrationModel, migrationModel.FolderNameWithAllScripts))
                {
                    var scriptToExecute = new ScriptToExecute()
                    {
                        FileNameLong = scriptFile,
                        FileName = scriptFile,
                    };

                    var d = _ddlHelper.ExtrapolateVersionedFileName(migrationModel, scriptFile);
                    if (d > 0)
                    {
                        if (versionsInAssembly.Contains(d))
                        {
                            _logger.LogError("Creos.DatabaseMigration | Duplicate Versions Found: {DuplicateVersion}", d);
                            throw new DatabaseMigrationDuplicateVersionFound($"Creos.DatabaseMigration | Duplicate Versions Found: {d}");
                        }
                        if (d > maxDeployedVersion)
                        {
                            scriptToExecute.ScriptVersion = d;
                            scriptToExecute.DisplayName = d.ToString();
                            scriptsToExecute.Add(scriptToExecute);
                        }
                    }
                }

                return scriptsToExecute;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creos.DatabaseMigration | GetMissingVersionsAsync | DatabaseMigration failed for {ConnectionStringName}", connectionStringInfo.ConnectionStringName);
                throw;
            }
        }

        public List<ScriptToExecute> GetRerunnableScripts(DatabaseMigrationModel migrationModel, string PreOrPost)
        {
            var scriptsToExecute = new List<ScriptToExecute>();
            foreach (var scriptFile in _ddlHelper.GetValidScriptFilesInAssembly(migrationModel, PreOrPost))
            {
                var scriptToExecute = new ScriptToExecute()
                {
                    FileName = _ddlHelper.ExtrapolateRerunnableFileName(migrationModel, scriptFile, PreOrPost),
                    FileNameLong = scriptFile,
                    ScriptVersion = 0
                };
                scriptsToExecute.Add(scriptToExecute);
            }

            int i = 0;
            foreach (var scriptToExecute in scriptsToExecute.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
            {
                scriptToExecute.ScriptVersion = i;
                scriptToExecute.DisplayName = scriptToExecute.FileName;
                i++;
            }

            return scriptsToExecute;
        }

        public async Task<List<decimal>> GetDeployedVersionsAsync(DatabaseMigrationModel migrationModel, ConnectionStringInfo connectionStringInfo, CancellationToken cancellationToken)
        {
            var sql = string.Empty;
            try
            {
                sql = $"select * from {migrationModel.SchemaName}.{migrationModel.VersionTable} order by dbversionid desc;";
                using var dt = await _sqlHelper.GetDataTableAsync(migrationModel, connectionStringInfo, sql, 30, cancellationToken).ConfigureAwait(false);

                var versions = new List<decimal>();
                foreach (DataRow dr in dt.Rows)
                {
                    if (decimal.TryParse(dr[0].ToString(), out decimal version))
                        versions.Add(version);
                }
                return versions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creos.DatabaseMigration | GetDeployedVersionsAsync | DatabaseMigration failed {ConnectionStringName} on {sql}", connectionStringInfo.ConnectionStringName, sql);
                throw;
            }
        }

        public async Task SaveVersionAsExecutedAsync(DatabaseMigrationModel migrationModel, ConnectionStringInfo connectionStringInfo, decimal version, CancellationToken cancellationToken)
        {
            var sql = string.Empty;
            try
            {
                if (migrationModel.DatabaseType == DatabaseType.Postgres)
                    sql = $"insert into {migrationModel.SchemaName}.{migrationModel.VersionTable} (dbversionid, createutcdatetime) values ({version}, now() at time zone 'utc');";
                else if (migrationModel.DatabaseType == DatabaseType.SqlServer)
                    sql = $"insert into {migrationModel.SchemaName}.{migrationModel.VersionTable} (dbversionid, createutcdatetime) values ({version}, SYSUTCDATETIME());";
                await _sqlHelper.RunNonQuery(migrationModel, connectionStringInfo, sql, 30, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creos.DatabaseMigration | GetMissingVersionsAsync | DatabaseMigration failed {ConnectionStringName} on {sql}", connectionStringInfo.ConnectionStringName, sql);
                throw;
            }
        }
    }
}
