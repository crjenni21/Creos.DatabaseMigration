using Creos.DatabaseMigration.Exceptions;
using Creos.DatabaseMigration.Models;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Creos.DatabaseMigration.Helper
{
    internal class DllHelper
    {
        private readonly ILogger _logger;
        private const string BaseScriptFileName = "base";

        public DllHelper(ILogger logger)
        {
            _logger = logger;
        }

        public void GetDatabaseAssembly(ref DatabaseMigrationModel migrationModel)
        {
            try
            {
                var executingPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                _logger.LogTrace("Creos.DatabaseMigration | DllHelper | FindFileAndReturnName | {executingPath}", executingPath);

                FileInfo assembly = null;

                foreach (var file in new DirectoryInfo(executingPath).GetFiles())
                {
                    if (string.Equals(file.Name, $"{migrationModel.ScriptProjectName}.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        assembly = file;
                        break;
                    }
                }

                if (assembly == null)
                {
                    throw new DatabaseMigrationDllNotFound($"Creos.DatabaseMigration | DllHelper | GetDatabaseAssembly | {migrationModel.ScriptProjectName} not found.");
                }

                if (File.Exists(assembly.FullName))
                {
                    migrationModel.AssemblyName = assembly.Name;
                    migrationModel.Assembly = Assembly.LoadFile(assembly.FullName);
                }
                else
                {
                    throw new DatabaseMigrationDllNotFound($"Creos.DatabaseMigration | DllHelper | GetDatabaseAssembly | {migrationModel.ScriptProjectName} not found.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creos.DatabaseMigration | DllHelper | GetDatabaseAssembly | {ScriptProjectName} Not Found", migrationModel.ScriptProjectName);
                throw;
            }
        }

        public async Task<string> GetBaseSqlToExecute(DatabaseMigrationModel migrationModel)
        {
            var allowedExts = new List<string>();
            if (migrationModel.DatabaseType == DatabaseType.Postgres)
                allowedExts = LookUpValues.AllowedExtensions_Postgres;
            else if (migrationModel.DatabaseType == DatabaseType.SqlServer)
                allowedExts = LookUpValues.AllowedExtensions_SqlServer;

            var script = "";
            foreach (var ext in allowedExts)
            {
                script = await GetSqlToExecuteByFileNameShortAsync(migrationModel, $"{BaseScriptFileName}.{ext}").ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(script))
                    break;
            }
            return script;
        }

        public async Task<string> GetSqlToExecuteByFileNameShortAsync(DatabaseMigrationModel migrationModel, string fileNameShort)
        {
            var fileNamesFromAssembly = GetValidScriptFilesInAssembly(migrationModel, string.Empty);

            foreach (var fileNameFromAssembly in fileNamesFromAssembly)
            {
                var name = string.Empty;
                if (fileNameFromAssembly.EndsWith($".{fileNameShort}"))
                {
                    using var stream = migrationModel.Assembly.GetManifestResourceStream(fileNameFromAssembly);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        return await reader.ReadToEndAsync().ConfigureAwait(false);
                    }
                }
            }

            return string.Empty;
        }

        public async Task<string> GetSqlToExecuteByFileNameLongAsync(DatabaseMigrationModel migrationModel, string fileNameLong)
        {
            using var stream = migrationModel.Assembly.GetManifestResourceStream(fileNameLong);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            return string.Empty;
        }

        public List<string> GetValidScriptFilesInAssembly(DatabaseMigrationModel migrationModel, string folderName)
        {
            var allFilesFromManifest = migrationModel.Assembly.GetManifestResourceNames().Where(n => n.StartsWith($"{migrationModel.AssemblyName.Replace("dll", string.Empty, StringComparison.OrdinalIgnoreCase)}{folderName}", StringComparison.OrdinalIgnoreCase)).ToList();

            if (allFilesFromManifest?.Count == 0)
            {
                _logger.LogTrace("Creos.DatabaseMigration | DllHelper | GetValidScriptFilesInAssembly | No files found in {AssemblyName}.", migrationModel.AssemblyName);
                return new List<string>();
            }

            var allowedExts = new List<string>();
            if (migrationModel.DatabaseType == DatabaseType.Postgres)
                allowedExts = LookUpValues.AllowedExtensions_Postgres;
            else if (migrationModel.DatabaseType == DatabaseType.SqlServer)
                allowedExts = LookUpValues.AllowedExtensions_SqlServer;


            var allFilesFromManifestWithValidExtension = new List<string>();
            foreach (var file in allFilesFromManifest)
            {
                foreach (var ext in allowedExts)
                {
                    if (file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        allFilesFromManifestWithValidExtension.Add(file);
                        break;
                    }
                }
            }

            if (allFilesFromManifestWithValidExtension?.Count == 0)
            {
                _logger.LogTrace("Creos.DatabaseMigration | DllHelper | GetValidScriptFilesInAssembly | No files found in {AssemblyName} with a valid extension.", migrationModel.AssemblyName);
                return new List<string>();
            }
            return allFilesFromManifestWithValidExtension;
        }

        public decimal ExtrapolateVersionedFileName(DatabaseMigrationModel migrationModel, string fileName)
        {
            var allowedExts = new List<string>();
            if (migrationModel.DatabaseType == DatabaseType.Postgres)
                allowedExts = LookUpValues.AllowedExtensions_Postgres;
            else if (migrationModel.DatabaseType == DatabaseType.SqlServer)
                allowedExts = LookUpValues.AllowedExtensions_SqlServer;

            foreach (var ext in allowedExts)
            {
                if (fileName.EndsWith($"{BaseScriptFileName}{ext}"))
                    return 0;
            }

            var s = string.Empty;

            foreach (var ext in allowedExts)
            {
                if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    s = fileName.TrimEnd(ext);
                    break;
                }
            }

            var parts = s.Split('.');
            var version = string.Empty;
            foreach (var part in parts)
            {
                if (int.TryParse(part, out _))
                {
                    version += part.ToString() + '.';
                }
            }
            version = version.TrimEnd('.');
            if (decimal.TryParse(version, out decimal dVersion))
                return dVersion;
            else
            {
                _logger.LogWarning("Creos.DatabaseMigration | DllHelper | ExtrapolateVersionedFileName failed on filename: {fileName}", fileName);
                return 0;
            }
        }

        public string ExtrapolateRerunnableFileName(DatabaseMigrationModel migrationModel, string fileName, string folder)
        {
            var allowedExts = new List<string>();
            if (migrationModel.DatabaseType == DatabaseType.Postgres)
                allowedExts = LookUpValues.AllowedExtensions_Postgres;
            else if (migrationModel.DatabaseType == DatabaseType.SqlServer)
                allowedExts = LookUpValues.AllowedExtensions_SqlServer;

            var s = string.Empty;
            foreach (var ext in allowedExts)
            {
                if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    s = fileName.TrimEnd(ext);
                    break;
                }
            }

            var parts = s.Split('.');
            var folderFound = false;
            var fileName2 = string.Empty;
            foreach (var part in parts)
            {
                if (folderFound)
                {
                    fileName2 += part + '.';
                    continue;
                }
                if (string.Equals(part, folder, StringComparison.OrdinalIgnoreCase))
                {
                    folderFound = true;
                }
            }
            fileName2 = fileName2.TrimEnd('.');
            return fileName2;
        }
    }
}
