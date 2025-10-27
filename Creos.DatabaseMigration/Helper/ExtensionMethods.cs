
namespace Creos.DatabaseMigration.Helper
{
    internal static class ExtensionMethods
    {
        public static string TrimEnd(this string source, string value)
        {
            if (source == null) return string.Empty;
            if (!source.EndsWith(value, StringComparison.OrdinalIgnoreCase))
                return source;

            source = source.ToLower();
            value = value.ToLower();

            return source.Remove(source.LastIndexOf(value));
        }
    }
}
