using System.Data;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The members of the EF Core entity class of one table (<c>CS_Entity</c>): the key and the foreign keys under a region, the navigation properties, then one property per other column with the attributes its
/// type and the project's rules call for. <see cref="Body"/> is whole lines, four spaces in, each ending in a line feed; <see cref="UsesSchema"/> says whether a member needs
/// <c>System.ComponentModel.DataAnnotations.Schema</c> ([ForeignKey], [Column], [DatabaseGenerated]), so the file's using lines can name it. </summary>
public sealed record EntityMembers(string Body, bool UsesSchema);

public static class EntityCode
{
    /// <summary> A date shown and edited as a date, whatever time of day it holds. </summary>
    private static bool IsDate(ColumnModel c) => c.SqlType is SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime;

    /// <summary> The members of the entity class of <paramref name="model"/> (a table with a primary key). </summary>
    public static EntityMembers Of(TableModel model, ProjectSettings project)
    {
        bool nameActive = model.IsNameActiveTable;
        string Id(ColumnModel c) => c.CSharpName();

        // foreign keys: single-column ones only
        var fkByColumn = new Dictionary<string, ForeignKeyModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in model.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1))
            fkByColumn[fk.ReferencingColumns[0]] = fk;

        var navigations = EntityNavigations.Of(model, fk => project.NoNavigationFor(fk.ReferencedTable, fk.ReferencedLookupShape)).ToList();
        var navByColumn = navigations.ToDictionary(n => n.Column.Name, n => n, StringComparer.OrdinalIgnoreCase);

        // the user table's password hash is never written to an answer or read from a request body
        string? hashColumn = UserTable.PlanOf(model, project)?.PasswordHash.Name;

        var body = new StringBuilder();
        void B(string s = "") => body.Append(s).Append('\n');
        bool usesSchema = false;

        // EF Core allows one [Column] per property: it carries the column's real name when the project's NamingStyle gave the property another one
        // (customer_id -> CustomerId), and the column type when one has to be said (money, a PostgreSQL timestamp or jsonb).
        string? ColumnAttribute(ColumnModel col, string? columnType)
        {
            string? name = col.DatabaseName;
            if (name is null && columnType is null)
                return null;
            usesSchema = true;
            return "    [Column(" + (name is null ? "" : $"\"{name}\"" + (columnType is null ? "" : ", ")) + (columnType is null ? "" : $"TypeName = \"{columnType}\"") + ")]";
        }

        // Omitted: the key, then the foreign keys, in column order
        B("    #region Omitted");
        bool firstOmitted = true;
        foreach (var c in model.Columns.Where(c => c.IsPrimaryKey || fkByColumn.ContainsKey(c.Name)))
        {
            if (!firstOmitted)
                B();
            firstOmitted = false;
            if (c.IsPrimaryKey)
            {
                if (model.PrimaryKeyColumns.Count == 1)
                    B("    [Key]");
                B("    [Display(Order = -1, AutoGenerateField = true)]");
            }
            else
            {
                if (navByColumn.TryGetValue(c.Name, out var nav))
                {
                    B($"    [ForeignKey(nameof({nav.Role}))]");
                    usesSchema = true;
                }
                B("    [Display(Order = -1, AutoGenerateField = false)]");
            }
            if (ColumnAttribute(c, null) is { } omittedColumnAttribute)
                B(omittedColumnAttribute);
            B($"    public {(c.IsNullable ? "" : "required ")}{c.CSharp()} {Id(c)} {{ get; set; }}");
        }
        B("    #endregion Omitted");

        // navigation properties
        if (navigations.Count > 0)
        {
            B();
            B("    // entities");
            foreach (var n in navigations)
                B($"    public {n.Type}? {n.Role} {{ get; set; }}");
        }

        // the rest of the columns
        foreach (var c in model.Columns.Where(c => !c.IsPrimaryKey && !fkByColumn.ContainsKey(c.Name) && !(nameActive && (c == model.RequiredNameColumn || c == model.IsActiveColumn))))
        {
            B();
            bool isBit = c.IsBooleanColumn;
            string words = CSharpText.Escape(Labels.Words(c.Name, isBit));
            int chars = c.CharacterLength;
            bool isText = c.SqlType is SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar;
            bool multiline = isText && chars >= 100;
            string? columnType = null;

            B($"    [Display({(multiline ? "Order = -1, " : "")}Name = \"{words}\", Description = \"{words}\")]");
            if (IsDate(c))
            {
                B("    [DisplayFormat(DataFormatString = \"{0:MM/dd/yyyy}\")]");
                B("    [DataType(DataType.Date)]");
                // PostgreSQL: Npgsql maps a DateTime to "timestamp with time zone" (UTC only) unless the column type is said: timestamp (no zone) or date.
                if (model.Dialect == SqlDialect.PostgreSql)
                    columnType = c.SqlTypeDeclaration;
            }
            else if (c.IsMoneyColumn)
            {
                B("    [DisplayFormat(ApplyFormatInEditMode = true)]");
                B("    [DataType(DataType.Currency)]");
                columnType = c.SqlType == SqlDbType.Money ? "money" : "smallmoney";
            }
            else if (c.SqlType == SqlDbType.Decimal)
            {
                int scale = c.Scale ?? 0;
                B($"    [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = \"#0{(scale > 0 ? "." + new string('0', scale) : "")}\")]");
                if (!(model.Dialect == SqlDialect.PostgreSql && c.SqlTypeDeclaration == "numeric")) // a bare PostgreSQL numeric has no precision to state
                    B($"    [Precision({c.Precision ?? 18}, {scale})]");
            }
            else if (isText)
            {
                // PostgreSQL json / jsonb arrive as a string column; the column type has to be said or Npgsql sends text.
                if (model.Dialect == SqlDialect.PostgreSql && c.SqlTypeDeclaration is "json" or "jsonb")
                    columnType = c.SqlTypeDeclaration;
                if (chars > 0)
                    B($"    [StringLength({chars})]");
                if (multiline)
                    B("    [DataType(DataType.MultilineText)]");
                else if (c.Name.ContainsIgnoreCase("Phone"))
                    B("    [DataType(DataType.PhoneNumber)]");
            }
            else if (c.IsRowVersionColumn)
            {
                B("    [Timestamp]");
            }

            if (c.IsComputed)
            {
                B("    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]");
                usesSchema = true;
            }
            else if (c.IsIdentity)
            {
                B("    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]");
                usesSchema = true;
            }
            // a create date the database fills in (DEFAULT sysdatetime()): EF Core leaves it out of the insert while it is still the type's default, so the column default applies
            else if (c.IsCreateDateColumn && !c.IsNullable && c.IsDateColumn && c.DatabaseDefaultSql is not null)
            {
                B("    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]");
                usesSchema = true;
            }

            string initializer = "";
            if (isBit && c.DatabaseDefaultSql is not null)
            {
                string d = c.DatabaseDefaultSql.Trim('(', ')', ' ');
                // SQL Server writes a bit default as ((1)) / ((0)); PostgreSQL writes true / false.
                if (d == "1" || d.EqualsIgnoreCase("true"))
                    initializer = " = true;";
                else if (d == "0" || d.EqualsIgnoreCase("false"))
                    initializer = " = false;";
            }
            if (hashColumn is not null && c.Name.EqualsIgnoreCase(hashColumn))
                B("    [System.Text.Json.Serialization.JsonIgnore]");
            if (ColumnAttribute(c, columnType) is { } columnAttribute)
                B(columnAttribute);
            bool needsRequired = !c.IsNullable && !c.IsComputed && !c.IsIdentity && !c.IsRowVersionColumn;
            // the constructor of the class says the required members are set, which the nullable analysis does not believe for a string or a byte array
            if (initializer.Length == 0)
                initializer = CSharpText.NotNullInitializer(c);
            B($"    public {(needsRequired ? "required " : "")}{c.CSharp()} {Id(c)} {{ get; set; }}{initializer}");
        }

        // ToString on a table that has a plain "Name"
        if (!nameActive && model.Columns.Any(c => c.IsStringColumn && c.Name == "Name"))
        {
            B();
            B("    public override string? ToString() => Name;");
        }

        return new EntityMembers(body.ToString(), usesSchema);
    }
}
