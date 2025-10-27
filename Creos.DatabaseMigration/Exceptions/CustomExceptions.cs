
namespace Creos.DatabaseMigration.Exceptions
{
    [Serializable]
    internal class DatabaseMigrationPropertyException : Exception
    {
        public DatabaseMigrationPropertyException() { }
        public DatabaseMigrationPropertyException(string s) : base(s) { }
    }

    [Serializable]
    internal class DatabaseMigrationDllNotFound : Exception
    {
        public DatabaseMigrationDllNotFound() { }
        public DatabaseMigrationDllNotFound(string s) : base(s) { }
    }

    [Serializable]
    internal class DatabaseMigrationDuplicateVersionFound : Exception
    {
        public DatabaseMigrationDuplicateVersionFound() { }
        public DatabaseMigrationDuplicateVersionFound(string s) : base(s) { }
    }
}
