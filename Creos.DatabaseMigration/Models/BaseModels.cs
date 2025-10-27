
using Creos.DatabaseMigration.Helper;
using Creos.DatabaseMigration.Models;
using System.Reflection;

namespace Creos.DatabaseMigration.Models
{
    public enum DatabaseType
    {
        SqlServer,
        Postgres
    }

    internal static class LookUpValues
    {
        public readonly static List<string> AllowedExtensions_Postgres = new() { ".psql" };
        public readonly static List<string> AllowedExtensions_SqlServer = new() { ".sql" };
    }
}

namespace Creos.DatabaseMigration
{
    public class DatabaseMigrationModel
    {
        internal Assembly Assembly { get; set; }
        public string VersionTable { get; set; } = "dbversion";
        private string _scriptProjectName = string.Empty;
        public string ScriptProjectName
        {
            get
            {
                return _scriptProjectName.TrimEnd(".dll");
            }
            set
            {
                _scriptProjectName = value;
            }
        }
        public List<ConnectionStringInfo> ConnectionStrings { get; set; }
        public string SchemaName { get; set; }
        public int Log_ElapsedSeconds { get; set; } = 15;
        public int Timeout { get; set; } = 30;
        public int MaxThreads_Concurrency { get; set; } = 1;
        internal string AssemblyName { get; set; }
        public string FolderNameWithAllScripts { get; set; } = "SqlFiles";
        public DatabaseType DatabaseType { get; set; } = DatabaseType.Postgres;
    }

    public class ConnectionStringInfo
    {
        public ConnectionStringInfo(string cnString)
        {
            this.CnString = cnString;
        }

        public ConnectionStringInfo(string connectionStringName, string cnString)
        {
            this.ConnectionStringName = connectionStringName;
            this.CnString = cnString;
        }

        private string _connectionStringName;
        public string ConnectionStringName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_connectionStringName))
                    return _connectionStringName;
                return GetDatabaseName();
            }
            set
            {
                _connectionStringName = value;
            }
        }
        public string CnString { get; set; }

        private string GetDatabaseName()
        {
            if (string.IsNullOrWhiteSpace(CnString)) return string.Empty;
            var cnStringParts = CnString.Split(';');
            foreach (var cnStringPart in cnStringParts)
            {
                if (cnStringPart.Contains('='))
                {
                    var kv = cnStringPart.Split('=');
                    if (kv.Length == 2)
                    {
                        if (string.Equals(kv[0], "database", StringComparison.OrdinalIgnoreCase))
                        {
                            return kv[1];
                        }
                        if (string.Equals(kv[0], "Initial Catalog", StringComparison.OrdinalIgnoreCase))
                        {
                            return kv[1];
                        }
                    }
                }
            }
            return string.Empty;
        }
    }

    internal class ScriptToExecute
    {
        public decimal ScriptVersion { get; set; }
        public string FileName { get; set; }
        public string FileNameLong { get; set; }
        public string DisplayName { get; set; }

    }

    public class DatabaseMigrationResponse
    {
        public bool OverallSuccess { get; set; }
        public List<ConnectionStringStateModel> ConnectionStrings { get; set; }
    }

    public class ConnectionStringStateModel
    {
        public string ConnectionStringName { get; set; }
        public string CnString { get; set; }
        public bool Success { get; set; }
        public FailureStateResponse FailureState { get; set; }
    }

    public class FailureStateResponse
    {
        public string FailedVersion { get; set; }
        public Exception Exception { get; set; }

    }
}