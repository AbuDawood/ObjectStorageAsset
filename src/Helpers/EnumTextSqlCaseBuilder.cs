using System.Globalization;
using System.Reflection;
using System.Text;

namespace Elf.ObjectStorageAsset.Helpers;

/// <summary>
/// Builds SQL CASE expressions that map enum numeric values to enum names.
/// </summary>
public static class EnumTextSqlCaseBuilder
{
    /// <summary>
    /// Builds a SQL Server CASE expression for an enum-backed column.
    /// </summary>
    public static string BuildCaseSql<TEnum>(string columnName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(columnName));
        }

        var fieldInfos = typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static);
        if (fieldInfos.Length == 0)
        {
            throw new InvalidOperationException($"Enum {typeof(TEnum).Name} has no declared values.");
        }

        var escapedColumnName = EscapeIdentifier(columnName);
        var sql = new StringBuilder();
        var seenValues = new HashSet<long>();

        sql.Append("(CASE ").Append(escapedColumnName);

        foreach (var fieldInfo in fieldInfos)
        {
            var rawValue = fieldInfo.GetRawConstantValue();
            if (rawValue is null)
            {
                continue;
            }

            var numericValue = Convert.ToInt64(rawValue, CultureInfo.InvariantCulture);
            if (!seenValues.Add(numericValue))
            {
                continue;
            }

            sql.Append(" WHEN ")
                .Append(numericValue.ToString(CultureInfo.InvariantCulture))
                .Append(" THEN N'")
                .Append(EscapeSqlString(fieldInfo.Name))
                .Append('\'');
        }

        sql.Append(" ELSE NULL END)");
        return sql.ToString();
    }

    private static string EscapeIdentifier(string columnName)
    {
        var parts = columnName.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(columnName));
        }

        var escapedParts = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.StartsWith("[", StringComparison.Ordinal) && part.EndsWith("]", StringComparison.Ordinal))
            {
                part = part[1..^1];
            }

            if (part.Contains(']'))
            {
                throw new ArgumentException($"Invalid SQL identifier segment '{parts[i]}'.", nameof(columnName));
            }

            escapedParts[i] = $"[{part}]";
        }

        return string.Join('.', escapedParts);
    }

    private static string EscapeSqlString(string input)
    {
        return input.Replace("'", "''", StringComparison.Ordinal);
    }
}
