namespace CodeGenNew.Core;

/// <summary> A view as the database keeps it: its name and the whole create statement, in the dialect it was read from. </summary>
public sealed record ViewModel(string SchemaName, string Name, string Definition);

/// <summary> A trigger of a table: its name and the statements that create it (for PostgreSQL the trigger function first), in the dialect it was read from. A disabled trigger is created and then switched off. </summary>
public sealed record TriggerModel(string Name, string Definition, bool IsDisabled = false);
