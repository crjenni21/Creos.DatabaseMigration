
using Creos.DatabaseMigration.Exceptions;
using Creos.DatabaseMigration.Helper;
using Creos.DatabaseMigration.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Creos.DatabaseMigration
{

    public class DatabaseMigrationHelper
    {
        private readonly ILogger _logger;
        private readonly SqlHelper _sqlHelper;
        private readonly DllHelper _ddlHelper;
        private readonly MigrationHelper _migrationHelper;

        public DatabaseMigrationHelper(ILogger logger)
        {
            _logger = logger;
            _sqlHelper = new SqlHelper(logger);
            _ddlHelper = new DllHelper(logger);
            _migrationHelper = new MigrationHelper(logger, _sqlHelper, _ddlHelper);
        }

        public async Task<DatabaseMigrationResponse> UpdateDatabaseAsync(DatabaseMigrationModel databaseMigrationModel, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Creos.DatabaseMigration | UpdateDb | Entered UpdateDB");
            if (string.IsNullOrWhiteSpace(databaseMigrationModel.ScriptProjectName))
            {
                _logger.LogError("Creos.DatabaseMigration | UpdateDb | Cannot run UpdateDB.   Either the ProductName or DllName Property must be set.");
                throw new DatabaseMigrationPropertyException("Creos.DatabaseMigration | UpdateDb | Cannot run UpdateDB.   Either the ProductName or DllName Property must be set.");
            }

            if (databaseMigrationModel.ConnectionStrings.Count == 0)
            {
                _logger.LogError("Creos.DatabaseMigration | UpdateDb | Cannot run UpdateDB.   ConnectionString List is empty.");
                throw new DatabaseMigrationPropertyException("Creos.DatabaseMigration | UpdateDb | Cannot run UpdateDB.   ConnectionString List is empty.");
            }

            if (databaseMigrationModel.ConnectionStrings.Any(x => string.IsNullOrWhiteSpace(x.CnString)))
            {
                _logger.LogError("Creos.DatabaseMigration | UpdateDb | Cannot run UpdateDB.   A connection string is blank or null.");
                throw new DatabaseMigrationPropertyException("Creos.DatabaseMigration | UpdateDb | Cannot run UpdateDB.   A connection string is blank or null.");
            }

            if (string.IsNullOrWhiteSpace(databaseMigrationModel.SchemaName))
            {
                if (databaseMigrationModel.DatabaseType == DatabaseType.Postgres)
                    databaseMigrationModel.SchemaName = "public";
                else if (databaseMigrationModel.DatabaseType == DatabaseType.SqlServer)
                    databaseMigrationModel.SchemaName = "dbo";
            }
            
            if (string.IsNullOrWhiteSpace(databaseMigrationModel.VersionTable))
            {
                _logger.LogError("Creos.DatabaseMigration | UpdateDb | Cannot run UpdateDB.   VersionTable property not set.");
                throw new DatabaseMigrationPropertyException("Creos.DatabaseMigration | UpdateDb | Cannot run UpdateDB.   VersionTable property not set.");
            }

            _ddlHelper.GetDatabaseAssembly(ref databaseMigrationModel);

            var baseSql = await _ddlHelper.GetBaseSqlToExecute(databaseMigrationModel).ConfigureAwait(false);

            ConcurrentBag<ConnectionStringStateModel> migrationResponses = new();

            await Parallel.ForEachAsync(databaseMigrationModel.ConnectionStrings.Where(x => !string.IsNullOrWhiteSpace(x.CnString)), new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = databaseMigrationModel.MaxThreads_Concurrency },
                async (connectionStringInfo, cancellationToken) =>
                {
                    await _migrationHelper.CreateDatabaseVersionTableAsync(databaseMigrationModel, connectionStringInfo, cancellationToken);

                    var deployedVersions = await _migrationHelper.GetDeployedVersionsAsync(databaseMigrationModel, connectionStringInfo, cancellationToken).ConfigureAwait(false);
                    if (deployedVersions.Count == 0)
                    {
                        if (!string.IsNullOrWhiteSpace(baseSql))
                        {
                            await _sqlHelper.RunNonQuery(databaseMigrationModel, connectionStringInfo, baseSql, 30, cancellationToken).ConfigureAwait(false);
                            await _migrationHelper.SaveVersionAsExecutedAsync(databaseMigrationModel, connectionStringInfo, (decimal)0.0, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    var migrationResponse = new ConnectionStringStateModel()
                    {
                        ConnectionStringName = connectionStringInfo.CnString,
                        CnString = connectionStringInfo.CnString,
                        Success = true
                    };

                    var executionState = await ExecuteScripts(databaseMigrationModel, "pre", connectionStringInfo, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(executionState.FailedVersion))
                    {
                        migrationResponse.FailureState = executionState;
                        migrationResponse.Success = false;
                    }
                    else
                    {
                        executionState = await ExecuteScripts(databaseMigrationModel, databaseMigrationModel.FolderNameWithAllScripts, connectionStringInfo, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(executionState.FailedVersion))
                        {
                            migrationResponse.FailureState = executionState;
                            migrationResponse.Success = false;
                        }
                        else
                        {
                            executionState = await ExecuteScripts(databaseMigrationModel, "post", connectionStringInfo, cancellationToken);
                            if (!string.IsNullOrWhiteSpace(executionState.FailedVersion))
                            {
                                migrationResponse.FailureState = executionState;
                                migrationResponse.Success = false;
                            }
                        }
                    }

                    migrationResponses.Add(migrationResponse);
                });

            return new DatabaseMigrationResponse
            {
                ConnectionStrings = migrationResponses.ToList(),
                OverallSuccess = !migrationResponses.ToList().Where(x => !x.Success).Any()
            };
        }

        private async Task<FailureStateResponse> ExecuteScripts(DatabaseMigrationModel databaseMigrationModel, string folder, ConnectionStringInfo connectionStringInfo, CancellationToken cancellationToken)
        {
            var failureState = new FailureStateResponse();

            var stopWatch = new Stopwatch();
            List<ScriptToExecute> scriptVersionsToExecute = null;

            var isRerunnableScript = new List<string> { "pre", "post" }.Contains(folder, StringComparer.OrdinalIgnoreCase);

            if (isRerunnableScript)
                scriptVersionsToExecute = _migrationHelper.GetRerunnableScripts(databaseMigrationModel, folder);
            else
                scriptVersionsToExecute = await _migrationHelper.GetMissingVersionsAsync(databaseMigrationModel, connectionStringInfo, cancellationToken).ConfigureAwait(false);

            if (!isRerunnableScript && scriptVersionsToExecute.Count == 0)
            {
                _logger.LogDebug("Creos.DatabaseMigration | UpdateDb | {ConnectionStringName} No missing scripts found.  Max Database Version deployed is {DatabaseVersion}", connectionStringInfo.ConnectionStringName, await _migrationHelper.GetMaxDbVersionAsync(databaseMigrationModel, connectionStringInfo, cancellationToken).ConfigureAwait(false));
            }

            var success = true;
            foreach (var versionToDeploy in scriptVersionsToExecute.OrderBy(x => x.ScriptVersion))
            {
                var scriptToExecute = await _ddlHelper.GetSqlToExecuteByFileNameLongAsync(databaseMigrationModel, versionToDeploy.FileNameLong).ConfigureAwait(false);
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await _sqlHelper.RunNonQuery(databaseMigrationModel, connectionStringInfo, scriptToExecute, databaseMigrationModel.Timeout, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        failureState.Exception = ex;
                        failureState.FailedVersion = versionToDeploy.DisplayName;
                    }
                }, cancellationToken);

                _logger.LogTrace("Creos.DatabaseMigration | UpdateDB | {ConnectionStringName} Executing Script Version {folder} {Version}", connectionStringInfo.ConnectionStringName, folder, versionToDeploy.DisplayName);
                stopWatch.Restart();

                var elapsedSeconds_LastLogged = 0;
                if (databaseMigrationModel.Log_ElapsedSeconds > 0)
                {
                    while (!task.IsCompleted)
                    {
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                        var elapsedSeconds = (int)(stopWatch.ElapsedMilliseconds / 1000);
                        if (elapsedSeconds > 0 && elapsedSeconds_LastLogged < elapsedSeconds && elapsedSeconds % databaseMigrationModel.Log_ElapsedSeconds == 0)
                        {
                            _logger.LogDebug("Creos.DatabaseMigration | UpdateDB | {ConnectionStringName} Waiting on Script {folder} {Version} to Execute - {elapsedSeconds} elapsed seconds", connectionStringInfo.ConnectionStringName, folder, versionToDeploy.DisplayName, elapsedSeconds);
                            elapsedSeconds_LastLogged = elapsedSeconds;
                        }
                    }
                }
                else
                {
                    task.Wait(cancellationToken);
                }
                if (!task.IsCompletedSuccessfully || !success)
                {
                    _logger.LogError("Creos.DatabaseMigration | UpdateDb | {ConnectionStringName} Script {folder} {Version} failed to execute properly on {ConnectionStringName}.  Stopping Database Migration - {elapsedSeconds} elapsed seconds: ", connectionStringInfo.ConnectionStringName, folder, versionToDeploy.DisplayName, connectionStringInfo.ConnectionStringName, Math.Round(stopWatch.ElapsedMilliseconds / 1000.0, 4));
                    success = false;
                    break;
                }
                else
                {
                    _logger.LogDebug("Creos.DatabaseMigration | UpdateDb | {ConnectionStringName} Script {folder} {Version} successfully executed on {ConnectionStringName} - {elapsedSeconds} elapsed seconds.", connectionStringInfo.ConnectionStringName, folder, versionToDeploy.DisplayName, connectionStringInfo.ConnectionStringName, Math.Round(stopWatch.ElapsedMilliseconds / 1000.0, 4));
                    if (!isRerunnableScript)
                        await _migrationHelper.SaveVersionAsExecutedAsync(databaseMigrationModel, connectionStringInfo, versionToDeploy.ScriptVersion, cancellationToken).ConfigureAwait(false);
                }
            }

            return failureState;
        }

    }
}
