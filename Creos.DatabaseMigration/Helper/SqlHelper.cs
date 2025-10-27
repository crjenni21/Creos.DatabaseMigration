


using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data;

namespace Creos.DatabaseMigration.Helper
{

    internal class SqlHelper
    {
        private readonly ILogger _logger;
        public SqlHelper(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<DataTable> GetDataTableAsync(DatabaseMigrationModel migrationModel, ConnectionStringInfo connectionStringInfo, string sql, int timeout, CancellationToken cancellationToken)
        {
            try
            {
                using var dataTable = new DataTable();

                if (migrationModel.DatabaseType == Models.DatabaseType.Postgres)
                {
                    using var connection = new NpgsqlConnection(connectionStringInfo.CnString);
                    using NpgsqlCommand cmd = new(sql, connection);
                    cmd.CommandTimeout = timeout;
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    using var registration = cancellationToken.Register(() => cmd.Cancel());
                    await using var dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    dataTable.Load(dataReader);
                    await connection.CloseAsync().ConfigureAwait(false);
                }
                else if (migrationModel.DatabaseType == Models.DatabaseType.SqlServer)
                {
                    using var connection = new SqlConnection(connectionStringInfo.CnString);
                    using SqlCommand cmd = new(sql, connection);
                    cmd.CommandTimeout = timeout;
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    using var registration = cancellationToken.Register(() => cmd.Cancel());
                    await using var dataReader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    dataTable.Load(dataReader);
                    await connection.CloseAsync().ConfigureAwait(false);
                }

                return dataTable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creos.DatabaseMigration | {ConnectionStringName} {sql}", connectionStringInfo.ConnectionStringName, sql);
                throw;
            }
        }

        public async Task RunNonQuery(DatabaseMigrationModel migrationModel, ConnectionStringInfo connectionStringInfo, string sql, int timeout, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sql)) return;

                if (migrationModel.DatabaseType == Models.DatabaseType.Postgres)
                {
                    using var connection = new NpgsqlConnection(connectionStringInfo.CnString);
                    using NpgsqlCommand cmd = new(sql, connection);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    cmd.Transaction = transaction;
                    cmd.CommandTimeout = timeout;
                    try
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        throw;
                    }
                }
                else if (migrationModel.DatabaseType == Models.DatabaseType.SqlServer)
                {
                    using var connection = new SqlConnection(connectionStringInfo.CnString);
                    using SqlCommand cmd = new(sql, connection);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    using SqlTransaction transaction = connection.BeginTransaction();
                    cmd.Transaction = transaction;
                    cmd.CommandTimeout = timeout;
                    try
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        throw;
                    }
                }


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creos.DatabaseMigration | {ConnectionStringName} {sql}", connectionStringInfo.ConnectionStringName, sql);
                throw;
            }
        }

    }
}
