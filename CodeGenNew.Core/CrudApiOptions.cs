namespace CodeGenNew.Core;

/// <summary> What the project's settings change in the base classes every table's API derives from (<c>API_EssentialCrudApi</c>): the namespaces, whether the bodies are the entity or an input and output type (Dtos), whether the
/// endpoints validate, check the caller's roles and take editable child rows, and the list protocol. The essentials template is the text of the classes; this is the one place its switches are decided. </summary>
public sealed class CrudApiOptions
{
    private CrudApiOptions(ProjectSettings project)
    {
        string projectName = project.ProjectName ?? "MyApp";
        ApiNamespace = project.ApiNamespace ?? projectName + ".Api.Apis";
        EntityNamespace = project.EntityNamespace ?? projectName + ".Api.Entities";
        RepoNamespace = project.RepoNamespace ?? projectName + ".Api.Repositories";
        ContextNamespace = project.ContextNamespace ?? projectName + ".Api.Data";
        ContextType = project.ContextName ?? "AppContext";
        BaseNameActiveEntity = project.BaseNameActiveEntity ?? "BaseNameActiveEntity";
        Validation = project.ApiValidation;
        Auth = project.ApiAuth;
        // EditableChildren: a form saves the rows of some child tables together with the parent (ChildRows); written only when the project lists a child
        Children = project.EditableChildren.Length > 0;
        // Dtos: a table's API can take an input type and answer an output type (CS_DtoInput, CS_Dto) instead of its entity; the entity-typed classes stay for a table that does not
        Dtos = project.Dtos;
        Input = Dtos ? "TInput" : "TEntity";
        Output = Dtos ? "TOutput" : "TEntity";
        // the create and update handlers receive the entity's validator themselves, because the endpoint filter only sees a body of the entity type
        ValidatorParameter = Dtos && Validation ? ", [FromServices] IValidator<TEntity>? validator" : "";
        ValidatorArgument = Dtos && Validation ? ", validator" : "";
        // ListProtocol=Search: the list route takes the parameter names of the project's list protocol and answers { data, count }; the table's class says what the search box matches and what can be sorted
        ListSearch = project.ListSearchProtocol;
        FirstPage = project.PageBase;
    }

    public static CrudApiOptions Of(ProjectSettings project) => new(project);

    public string ApiNamespace { get; }
    public string EntityNamespace { get; }
    public string RepoNamespace { get; }
    public string ContextNamespace { get; }
    public string ContextType { get; }
    public string BaseNameActiveEntity { get; }
    public bool Validation { get; }
    public bool Auth { get; }
    public bool Children { get; }
    public bool Dtos { get; }

    /// <summary> The type of a create or update body: <c>TInput</c> with Dtos, else the entity. </summary>
    public string Input { get; }

    /// <summary> The type of an answer: <c>TOutput</c> with Dtos, else the entity. </summary>
    public string Output { get; }

    public string ValidatorParameter { get; }
    public string ValidatorArgument { get; }
    public bool ListSearch { get; }
    public int FirstPage { get; }

    /// <summary> The expression that turns a row into the answer's type. </summary>
    public string Map(string row) => Dtos ? $"ToOutput({row})" : row;

    /// <summary> The expression that turns a list of rows into a list of the answer's type. </summary>
    public string MapAll(string rows) => Dtos ? $"{rows}.Select(ToOutput).ToList()" : rows;
}
