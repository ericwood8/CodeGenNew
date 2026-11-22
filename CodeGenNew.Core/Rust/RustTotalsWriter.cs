using CodeGenNew.Core.Sql;
using System.Globalization;
using System.Text;
using CodeGenNew.Core.Totals;

namespace CodeGenNew.Core.Rust;

/// <summary> The Rust (axum, sqlx) counterpart of <see cref="DocumentTotalsWriter"/> (<c>DocumentTotals</c>): the header's route file gets the work, the rows' reads and the write of the totals, run inside the transaction of
/// the save of the header, of a line or of an allowance row; those three tables' routes call <c>insert_with_totals</c>, <c>update_with_totals</c> and <c>delete_with_totals</c> instead of the plain repository functions.
/// <c>GET document-totals-check</c> lists the headers whose stored totals differ from their rows. With a <c>Decimal</c> column the work is done in <c>Decimal</c> (half away from zero), otherwise in <c>f64</c>. </summary>
public static class RustTotalsWriter
{
    /// <summary> A table as Rust: its struct and module, its quoted name and its select list. </summary>
    public sealed record Tbl(string Struct, string Module, string Table, string SelectList);

    /// <summary> A field of a table's struct: its name, its base type (<c>f64</c>, <c>Decimal</c>, ...) and whether it is an <c>Option</c>. </summary>
    public sealed record Field(string Name, string BaseType, bool Nullable);

    /// <summary> <c>Parts</c> are the totals the server writes: the part name, the field, the quoted column and the scale. </summary>
    public sealed record Names(Tbl Header, Field Key, string KeyColumn, Field? Prepaid, Field? Currency, Tbl Lines, string LineKeyColumn, Field LineKey, Field LineAmount, Field? Tax, Tbl? Extra, string? ExtraDocumentColumn,
        Field? ExtraDocument, Field? ExtraFlag, Field? ExtraAmount, IReadOnlyList<(string Part, Field Field, string Column, int Scale)> Parts, IReadOnlyList<DocumentTotalsRound> Rounds, string Route, IReadOnlyList<DocumentTotalsPlaces>? Places = null, Field? ExtraTax = null);

    private static Tbl TableOf(RustTable x) => new(x.Struct, x.Module, x.Table, x.SelectList);

    private static Field FieldOf(RustTable x, ColumnModel c) => new(x.FieldOf(c).Name, x.FieldOf(c).BaseType, x.FieldOf(c).IsOption);

    /// <summary> The names of one header, its lines and its allowance table. </summary>
    public static Names NamesFor(DocumentTotalsShape shape, ProjectSettings project, DialectInfo dialect)
    {
        var header = RustTable.Of(shape.Header, project);
        var lines = RustTable.Of(shape.Lines, project);
        var extra = shape.Allowance is null ? null : RustTable.Of(shape.Allowance.Table, project);
        var document = shape.Allowance?.Document;
        return new Names(TableOf(header), FieldOf(header, shape.Key), dialect.Quote(shape.Key.DbName), shape.Columns.Prepaid is null ? null : FieldOf(header, shape.Columns.Prepaid),
            shape.Currency is null ? null : FieldOf(header, shape.Currency), TableOf(lines), dialect.Quote(shape.LineKey.DbName), FieldOf(lines, shape.LineKey), FieldOf(lines, shape.LineAmount),
            shape.Tax is null ? null : FieldOf(lines, shape.Tax.Tax), extra is null ? null : TableOf(extra), document is null ? null : dialect.Quote(document.DbName), document is null ? null : FieldOf(extra!, document),
            shape.Allowance is null ? null : FieldOf(extra!, shape.Allowance.Flag), shape.Allowance is null ? null : FieldOf(extra!, shape.Allowance.Amount),
            shape.Columns.Owned().Select(p => (p.Part, FieldOf(header, p.Column), dialect.Quote(p.Column.DbName), p.Column.Scale ?? DocumentTotalsWords.DefaultScale)).ToList(), shape.Rounds, header.Route, shape.Places,
            shape.AllowanceTax is null ? null : FieldOf(extra!, shape.AllowanceTax));
    }

    private static bool Decimals(Names n) => new[] { n.LineAmount.BaseType, n.Tax?.BaseType, n.ExtraAmount?.BaseType, n.Prepaid?.BaseType }.Concat(n.Parts.Select(p => p.Field.BaseType)).Contains("Decimal");

    private static string Num(string expression, Field field, bool decimals)
    {
        if (decimals)
            return field.Nullable ? $"{expression}.map(|v| {RustRateDocumentWriter.ToDecimal("v", field.BaseType)}).unwrap_or_default()" : RustRateDocumentWriter.ToDecimal(expression, field.BaseType);
        return field.Nullable ? $"{expression}.unwrap_or_default() as f64" : $"{expression} as f64";
    }

    private static string Out(string value, Field field, bool decimals)
    {
        string converted = decimals ? RustRateDocumentWriter.FromDecimal(value, field.BaseType) : $"{value} as {field.BaseType}";
        return field.Nullable ? $"Some({converted})" : converted;
    }

    private static string Literal(decimal value, bool decimals) => decimals ? $"rust_decimal::Decimal::from_str_exact(\"{value.ToString(CultureInfo.InvariantCulture)}\").unwrap_or_default()" : value.ToString(CultureInfo.InvariantCulture) + "_f64";

    private static int ScaleOf(Names n, string part) => n.Parts.FirstOrDefault(p => p.Part == part).Scale is var scale && scale > 0 ? scale : DocumentTotalsWords.DefaultScale;

    /// <summary> The helpers a line or an allowance row's route file adds: it saves and deletes inside a transaction and then writes the totals of the document it left and of the one it joined. </summary>
    public static string Feeder(Names n, Tbl table, Field foreign, string key, string keyType, string keyRef, string headerModule)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string recalc = $"crate::routes::{headerModule}::recalculate_totals", locking = $"crate::routes::{headerModule}::lock_totals";
        string collect(string row, string indent) => foreign.Nullable
            ? $"{indent}if let Some(document) = {row}.{foreign.Name} {{\n{indent}    keys.push(document);\n{indent}}}"
            : $"{indent}keys.push({row}.{foreign.Name});";
        string lockAll() => $"    keys.sort();\n    keys.dedup();\n    for document in &keys {{\n        {locking}(&mut tx, *document).await?;\n    }}";
        string recalcAll() => $"    for document in &keys {{\n        {recalc}(&mut tx, *document).await?;\n    }}";
        L("/// DocumentTotals: the row is saved in a transaction that also writes the totals of its document. The header row is updated before the row is written, in key order, so that saves of one document wait for each other.");
        L($"async fn insert_with_totals(pool: &Pool, row: &{table.Struct}) -> Result<{table.Struct}, sqlx::Error> {{");
        L("    let mut tx = pool.begin().await?;");
        L("    let mut keys = Vec::new();");
        L(collect("row", "    "));
        L(lockAll());
        L("    let saved = repo::insert_in(&mut tx, row).await?;");
        L(recalcAll());
        L("    tx.commit().await?;");
        L("    Ok(saved)");
        L("}");
        L();
        L($"async fn update_with_totals(pool: &Pool, id: {keyType}, row: &{table.Struct}) -> Result<Option<{table.Struct}>, sqlx::Error> {{");
        L("    let mut tx = pool.begin().await?;");
        L($"    let previous = repo::get_by_id_in(&mut tx, {keyRef}).await?;");
        L("    let mut keys = Vec::new();");
        L(collect("row", "    "));
        L("    if let Some(previous) = &previous {");
        L(collect("previous", "        "));
        L("    }");
        L(lockAll());
        L($"    let saved = repo::update_in(&mut tx, {keyRef}, row).await?;");
        L(recalcAll());
        L("    tx.commit().await?;");
        L("    Ok(saved)");
        L("}");
        L();
        L($"async fn delete_with_totals(pool: &Pool, id: {keyType}) -> Result<u64, sqlx::Error> {{");
        L("    let mut tx = pool.begin().await?;");
        L($"    let previous = repo::get_by_id_in(&mut tx, {keyRef}).await?;");
        L("    let mut keys = Vec::new();");
        L("    if let Some(previous) = &previous {");
        L(collect("previous", "        "));
        L("    }");
        L(lockAll());
        L($"    let removed = repo::delete_in(&mut tx, {keyRef}).await?;");
        L(recalcAll());
        L("    tx.commit().await?;");
        L("    Ok(removed)");
        L("}");
        L();
        return o.ToString();
    }

    /// <summary> What the header's route file adds: the work, the write of the totals, the saves of the header that write them, the check route. </summary>
    public static string Header(Names n, string userArgument = "", string? filterArguments = null)
    {
        bool decimals = Decimals(n);
        string domain = decimals ? "rust_decimal::Decimal" : "f64";
        string zero = decimals ? "rust_decimal::Decimal::ZERO" : "0.0";
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string money(string value, int scale) => decimals ? $"{value}.round_dp_with_strategy(totals_places(currency, {scale}), rust_decimal::RoundingStrategy::MidpointAwayFromZero)" : $"(({value}) * 10f64.powi(totals_places(currency, {scale}) as i32)).round() / 10f64.powi(totals_places(currency, {scale}) as i32)";
        L("/// DocumentTotals: the totals of a document, worked out from its rows.");
        L("struct WorkedTotals {");
        foreach (string field in new[] { "lines", "allowances", "charges", "exclusive", "tax", "inclusive", "rounding", "payable" })
            L($"    {field}: {domain},");
        L("}");
        L();
        L($"fn totals_step(currency: Option<&str>) -> Option<{domain}> {{");
        var (specificRounds, any) = DocumentTotalsCurrencyRules.Split(n.Rounds);
        foreach (var r in specificRounds)
            L($"    if currency.is_some_and(|c| c.trim().eq_ignore_ascii_case(\"{r.Currency}\")) {{\n        return Some({Literal(r.Increment, decimals)});\n    }}");
        L(any is null ? "    let _ = currency;\n    None" : $"    let _ = currency;\n    Some({Literal(any.Increment, decimals)})");
        L("}");
        L();
        L("/// The decimal places of an amount in the currency: the places of the currency, at most the scale of the column that holds the amount.");
        L("fn totals_places(currency: Option<&str>, scale: u32) -> u32 {");
        var (specificPlaces, anyPlaces) = DocumentTotalsCurrencyRules.Split(n.Places);
        foreach (var r in specificPlaces)
            L($"    if currency.is_some_and(|c| c.trim().eq_ignore_ascii_case(\"{r.Currency}\")) {{\n        return scale.min({r.Places});\n    }}");
        L(anyPlaces is null ? "    let _ = currency;\n    scale" : $"    let _ = currency;\n    scale.min({anyPlaces.Places})");
        L("}");
        L();
        L($"fn work_out_totals(lines: {domain}, allowances: {domain}, charges: {domain}, tax: {domain}, prepaid: {domain}, currency: Option<&str>) -> WorkedTotals {{");
        L($"    let lines = {money("lines", ScaleOf(n, DocumentTotalsWords.PartLines))};");
        L($"    let allowances = {money("allowances", ScaleOf(n, DocumentTotalsWords.PartAllowances))};");
        L($"    let charges = {money("charges", ScaleOf(n, DocumentTotalsWords.PartCharges))};");
        L($"    let exclusive = {money("(lines - allowances + charges)", ScaleOf(n, DocumentTotalsWords.PartTaxExclusive))};");
        L($"    let tax = {money("tax", ScaleOf(n, DocumentTotalsWords.PartTax))};");
        L($"    let inclusive = {money("(exclusive + tax)", ScaleOf(n, DocumentTotalsWords.PartTaxInclusive))};");
        L("    let due = inclusive - prepaid;");
        string stepped = decimals
            ? $"(due / step).round_dp_with_strategy(0, rust_decimal::RoundingStrategy::MidpointAwayFromZero) * step - due"
            : "(due / step).round() * step - due";
        L($"    let rounding = match totals_step(currency) {{\n        Some(step) => {money("(" + stepped + ")", ScaleOf(n, DocumentTotalsWords.PartRounding))},\n        None => {zero},\n    }};");
        L($"    let payable = {money("(due + rounding)", ScaleOf(n, DocumentTotalsWords.PartPayable))};");
        L("    WorkedTotals { lines, allowances, charges, exclusive, tax, inclusive, rounding, payable }");
        L("}");
        L();
        string key = n.Key.Name;
        L("/// The rows of one document read, the totals worked out; None when the document is not there.");
        L($"async fn worked_out_totals(tx: &mut sqlx::Transaction<'_, Db>, key: {n.Key.BaseType}) -> Result<Option<WorkedTotals>, sqlx::Error> {{");
        if (decimals)
            L("    #[allow(unused_imports)]\n    use rust_decimal::prelude::ToPrimitive as _;");
        L($"    let mut builder = sqlx::QueryBuilder::<Db>::new({RustTable.Raw($"SELECT {n.Header.SelectList} FROM {n.Header.Table} WHERE {n.KeyColumn} = ")});");
        L("    builder.push_bind(key);");
        L($"    let Some(header) = builder.build_query_as::<{n.Header.Struct}>().fetch_optional(&mut **tx).await? else {{\n        return Ok(None);\n    }};");
        L($"    let mut builder = sqlx::QueryBuilder::<Db>::new({RustTable.Raw($"SELECT {n.Lines.SelectList} FROM {n.Lines.Table} WHERE {n.LineKeyColumn} = ")});");
        L("    builder.push_bind(key);");
        L($"    let lines = builder.build_query_as::<{n.Lines.Struct}>().fetch_all(&mut **tx).await?;");
        L($"    let line_total = lines.iter().fold({zero}, |sum, r| sum + {Num("r." + n.LineAmount.Name, n.LineAmount, decimals)});");
        string mutable = n.ExtraTax is not null && n.Extra is not null ? "mut " : "";
        if (n.Tax is not null)
            L($"    let {mutable}tax_total = lines.iter().fold({zero}, |sum, r| sum + {Num("r." + n.Tax.Name, n.Tax, decimals)});");
        else
            L($"    let {mutable}tax_total = {zero};");
        if (n.Extra is not null)
        {
            L($"    let mut builder = sqlx::QueryBuilder::<Db>::new({RustTable.Raw($"SELECT {n.Extra.SelectList} FROM {n.Extra.Table} WHERE {n.ExtraDocumentColumn} = ")});");
            L("    builder.push_bind(key);");
            L($"    let extras = builder.build_query_as::<{n.Extra.Struct}>().fetch_all(&mut **tx).await?;");
            L($"    let allowance_total = extras.iter().filter(|r| !r.{n.ExtraFlag!.Name}).fold({zero}, |sum, r| sum + {Num("r." + n.ExtraAmount!.Name, n.ExtraAmount, decimals)});");
            L($"    let charge_total = extras.iter().filter(|r| r.{n.ExtraFlag.Name}).fold({zero}, |sum, r| sum + {Num("r." + n.ExtraAmount.Name, n.ExtraAmount, decimals)});");
        }
        else
            L($"    let (allowance_total, charge_total) = ({zero}, {zero});");
        if (mutable.Length > 0)
        {
            L("    for r in &extras {");
            L($"        let one = {Num("r." + n.ExtraTax!.Name, n.ExtraTax, decimals)};");
            L($"        tax_total = if r.{n.ExtraFlag!.Name} {{ tax_total + one }} else {{ tax_total - one }};");
            L("    }");
        }
        string prepaid = n.Prepaid is null ? zero : Num("header." + n.Prepaid.Name, n.Prepaid, decimals);
        string currency = n.Currency is null ? "None" : n.Currency.Nullable ? $"header.{n.Currency.Name}.as_deref()" : $"Some(header.{n.Currency.Name}.as_str())";
        L($"    Ok(Some(work_out_totals(line_total, allowance_total, charge_total, tax_total, {prepaid}, {currency})))");
        L("}");
        L();
        L("/// Updates the header row of a document before a row of it is written: a line's foreign key check holds a shared lock on the header (InnoDB), and two saves that each held one and then asked for the update lock would deadlock.");
        L($"pub(crate) async fn lock_totals(tx: &mut sqlx::Transaction<'_, Db>, key: {n.Key.BaseType}) -> Result<u64, sqlx::Error> {{");
        L($"    let mut lock = sqlx::QueryBuilder::<Db>::new({RustTable.Raw($"UPDATE {n.Header.Table} SET {n.Parts[0].Column} = {n.Parts[0].Column} WHERE {n.KeyColumn} = ")});");
        L("    lock.push_bind(key);");
        L("    Ok(lock.build().execute(&mut **tx).await?.rows_affected())");
        L("}");
        L();
        L("/// Writes the totals of one document into its header. The header row is updated first, so a second save of the same document waits for the first to commit before it reads the rows.");
        L($"pub(crate) async fn recalculate_totals(tx: &mut sqlx::Transaction<'_, Db>, key: {n.Key.BaseType}) -> Result<(), sqlx::Error> {{");
        if (decimals)
            L("    #[allow(unused_imports)]\n    use rust_decimal::prelude::ToPrimitive as _;");
        var owned = n.Parts.ToList();
        L($"    let mut lock = sqlx::QueryBuilder::<Db>::new({RustTable.Raw($"UPDATE {n.Header.Table} SET {owned[0].Column} = {owned[0].Column} WHERE {n.KeyColumn} = ")});");
        L("    lock.push_bind(key);");
        L("    if lock.build().execute(&mut **tx).await?.rows_affected() == 0 {\n        return Ok(());\n    }");
        L("    let Some(worked) = worked_out_totals(tx, key).await? else {\n        return Ok(());\n    };");
        L($"    let mut write = sqlx::QueryBuilder::<Db>::new({RustTable.Raw($"UPDATE {n.Header.Table} SET ")});");
        for (int i = 0; i < owned.Count; i++)
        {
            var (part, field, column, _) = owned[i];
            string value = part switch
            {
                DocumentTotalsWords.PartLines => "worked.lines",
                DocumentTotalsWords.PartAllowances => "worked.allowances",
                DocumentTotalsWords.PartCharges => "worked.charges",
                DocumentTotalsWords.PartTaxExclusive => "worked.exclusive",
                DocumentTotalsWords.PartTax => "worked.tax",
                DocumentTotalsWords.PartTaxInclusive => "worked.inclusive",
                DocumentTotalsWords.PartRounding => "worked.rounding",
                _ => "worked.payable",
            };
            L($"    write.push({RustTable.Raw((i == 0 ? "" : ", ") + column + " = ")}).push_bind({Out(value, field, decimals)});");
        }
        L($"    write.push({RustTable.Raw($" WHERE {n.KeyColumn} = ")}).push_bind(key);");
        L("    write.build().execute(&mut **tx).await?;");
        L("    Ok(())");
        L("}");
        L();
        L("/// DocumentTotals: the header is saved in a transaction that also writes its totals, and the answer is the row as stored.");
        L($"async fn insert_with_totals(pool: &Pool, row: &{n.Header.Struct}) -> Result<{n.Header.Struct}, sqlx::Error> {{");
        L("    let mut tx = pool.begin().await?;");
        L("    let saved = repo::insert_in(&mut tx, row).await?;");
        L($"    recalculate_totals(&mut tx, saved.{key}{(n.Key.BaseType == "String" ? ".clone()" : "")}).await?;");
        L($"    let saved = repo::get_by_id_in(&mut tx, saved.{key}).await?.ok_or(sqlx::Error::RowNotFound)?;");
        L("    tx.commit().await?;");
        L("    Ok(saved)");
        L("}");
        L();
        L($"async fn update_with_totals(pool: &Pool, id: {n.Key.BaseType}, row: &{n.Header.Struct}) -> Result<Option<{n.Header.Struct}>, sqlx::Error> {{");
        L("    let mut tx = pool.begin().await?;");
        L("    if repo::update_in(&mut tx, id, row).await?.is_none() {\n        return Ok(None);\n    }");
        L("    recalculate_totals(&mut tx, id).await?;");
        L("    let saved = repo::get_by_id_in(&mut tx, id).await?;");
        L("    tx.commit().await?;");
        L("    Ok(saved)");
        L("}");
        L();
        L("#[derive(serde::Deserialize)]");
        L("struct DocumentTotalsCheckQuery {");
        L($"    id: Option<{n.Key.BaseType}>,");
        L("}");
        L();
        L("/// The headers whose stored totals differ from their rows (one header when id is given).");
        L($"async fn document_totals_check(State(pool): State<Pool>{userArgument}, Query(query): Query<DocumentTotalsCheckQuery>) -> ApiResult<Json<serde_json::Value>> {{");
        if (decimals)
            L("    #[allow(unused_imports)]\n    use rust_decimal::prelude::ToPrimitive as _;");
        if (filterArguments is null)
            L($"    let headers = match query.id {{\n        Some(id) => repo::get_by_id(&pool, id).await?.into_iter().collect::<Vec<_>>(),\n        None => repo::get_all(&pool).await?.into_iter().take({DocumentTotalsWords.CheckLimit}).collect::<Vec<_>>(),\n    }};");
        else
            L($"    let rows = repo::get_all_scoped(&pool{filterArguments}).await?;\n    let headers = rows.into_iter().filter(|h| query.id.is_none_or(|id| h.{n.Key.Name} == id)).take({DocumentTotalsWords.CheckLimit}).collect::<Vec<_>>();");
        L("    let mut tx = pool.begin().await?;");
        L("    let mut differing = Vec::new();");
        L("    for header in &headers {");
        L($"        let Some(worked) = worked_out_totals(&mut tx, header.{key}{(n.Key.BaseType == "String" ? ".clone()" : "")}).await? else {{\n            continue;\n        }};");
        L("        let mut parts = Vec::new();");
        foreach (var (part, field, _, scale) in owned)
        {
            string expected = part switch
            {
                DocumentTotalsWords.PartLines => "worked.lines",
                DocumentTotalsWords.PartAllowances => "worked.allowances",
                DocumentTotalsWords.PartCharges => "worked.charges",
                DocumentTotalsWords.PartTaxExclusive => "worked.exclusive",
                DocumentTotalsWords.PartTax => "worked.tax",
                DocumentTotalsWords.PartTaxInclusive => "worked.inclusive",
                DocumentTotalsWords.PartRounding => "worked.rounding",
                _ => "worked.payable",
            };
            string stored = Num("header." + field.Name, field, decimals);
            string difference = decimals ? $"(({stored}) - {expected}).abs().to_f64().unwrap_or_default()" : $"(({stored}) - {expected}).abs()";
            string toNumber = decimals ? "to_f64().unwrap_or_default()" : "";
            L($"        if {difference} >= 0.5 / 10f64.powi({scale}) {{\n            parts.push(serde_json::json!({{ \"part\": \"{part}\", \"stored\": {(decimals ? $"({stored}).to_f64().unwrap_or_default()" : stored)}, \"expected\": {(decimals ? $"{expected}.to_f64().unwrap_or_default()" : expected)} }}));\n        }}");
            _ = toNumber;
        }
        L($"        if !parts.is_empty() {{\n            differing.push(serde_json::json!({{ \"id\": header.{key}, \"parts\": parts }}));\n        }}");
        L("    }");
        L("    tx.rollback().await?;");
        L("    Ok(Json(serde_json::json!({ \"checked\": headers.len(), \"differing\": differing.len(), \"headers\": differing })))");
        L("}");
        L();
        return o.ToString();
    }

    public static string RouteLine(string route) => $"        .route(\"{route}/{DocumentTotalsWords.CheckRoute}\", get(document_totals_check))\n";

    /// <summary> The repository functions that run inside a transaction, for the header, the lines and the allowance rows of <c>DocumentTotals</c>. </summary>
    public static bool InTransaction(TableModel table, ProjectSettings project) => DocumentTotalsPlan.IsListed(table.TableName, project) || DocumentTotalsPlan.Feeds(table.TableName, project);
}
