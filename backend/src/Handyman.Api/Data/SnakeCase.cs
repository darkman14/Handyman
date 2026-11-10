using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Handyman.Api.Data;

/// <summary>Table, column, key and index names in snake_case, as in workflow.md section 3.</summary>
internal static partial class SnakeCase
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(Convert(entity.GetTableName()!));
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            foreach (var property in entity.GetProperties())
                property.SetColumnName(Convert(property.GetColumnName(table)!));
            foreach (var key in entity.GetKeys())
                key.SetName(Convert(key.GetName()!));
            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(Convert(fk.GetConstraintName()!));
            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(Convert(index.GetDatabaseName()!));
        }
    }

    private static string Convert(string name) => Humps().Replace(name, "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex Humps();
}
