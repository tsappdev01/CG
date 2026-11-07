using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>The list of documents people are asked for, and whether they have to supply one.
///
/// It replaces a static array in My Workspace and a run of hardcoded Required/Optional pills in the
/// declaration -- two lists that could disagree with each other and neither changeable without a
/// redeploy. Both now render from this table.
///
/// Laid out like Job Titles and the other Control Panel lists: the row reads as text, Status is the
/// toggle, and everything else is behind Edit. It began as editable inputs sitting in the table,
/// which saved a click and looked like nothing else in the application.
///
/// No search box or pager, unlike Job Titles. That list runs to 461 rows; this one is the four
/// built-in documents plus whatever has been added, and a filter bar over five rows is furniture.</summary>
public partial class DocumentCenter
{
    private List<DocumentType>? _types;

    /// <summary>How many documents exist of each type, for the "On file" column and to explain why
    /// Remove is refused. Only added types can be counted on the type id -- the built-ins'
    /// documents are identified by Kind and carry none, so they are counted separately.</summary>
    private Dictionary<int, int> _uploadCounts = [];

    private bool _saving;

    /// <summary>The row being edited, or a blank one with Id 0 when adding. The form fields are
    /// held separately so cancelling leaves the list untouched -- binding straight to the row
    /// would rewrite the table as the person typed, and leave the edits behind on Cancel.</summary>
    private DocumentType? _editing;
    private string _formName = string.Empty;
    private string? _formDescription;
    private bool _formMandatory;

    private DocumentType? _pendingJustify;
    private DocumentType? _pendingDelete;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();

        _types = await db.DocumentTypes
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync();

        var addedCounts = await db.MemberDocuments
            .Where(d => d.DocumentTypeId != null)
            .GroupBy(d => d.DocumentTypeId!.Value)
            .Select(g => new { TypeId = g.Key, Count = g.Count() })
            .ToListAsync();

        _uploadCounts = addedCounts.ToDictionary(c => c.TypeId, c => c.Count);

        // The built-ins' documents predate this table and are keyed by Kind, so their count has to
        // come the other way round. Without this the three best-known rows would all read "none".
        var builtInCounts = await db.MemberDocuments
            .Where(d => d.DocumentTypeId == null)
            .GroupBy(d => d.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync();

        foreach (var type in _types.Where(t => t.BuiltInKind is not null))
        {
            var match = builtInCounts.FirstOrDefault(c => c.Kind == type.BuiltInKind);
            if (match is not null) _uploadCounts[type.Id] = match.Count;
        }
    }

    private string UploadCount(int typeId) =>
        _uploadCounts.TryGetValue(typeId, out var count) && count > 0 ? count.ToString() : "0";

    // ---------- add and edit ----------

    private void StartAdd()
    {
        _editing = new DocumentType();
        _formName = string.Empty;
        _formDescription = null;
        _formMandatory = false;
    }

    private void Edit(DocumentType type)
    {
        _editing = type;
        _formName = type.Name;
        _formDescription = type.Description;
        _formMandatory = type.IsMandatory;
    }

    private void CancelEdit() => _editing = null;

    private async Task SaveEditAsync()
    {
        if (_editing is null) return;

        var name = _formName.Trim();
        if (name.Length == 0)
        {
            Toasts.ShowError("A document needs a name.");
            return;
        }

        _saving = true;
        try
        {
            var description = string.IsNullOrWhiteSpace(_formDescription) ? null : _formDescription.Trim();
            var adding = _editing.Id == 0;

            if (adding)
            {
                // Last in the list rather than wherever the name sorts: a new document is the one
                // thing on the screen whose place the person who added it already has in mind.
                var sortOrder = (_types?.Count > 0 ? _types.Max(t => t.SortOrder) : 0) + 10;

                var type = new DocumentType
                {
                    Name = name,
                    Description = description,
                    IsMandatory = _formMandatory,
                    IsActive = true,
                    SortOrder = sortOrder,
                };

                int newId;
                try
                {
                    newId = await DocumentTypeWriter.InsertAsync(type);
                }
                catch (SqlException ex) when (ex.Number is 2601 or 2627)
                {
                    Toasts.ShowError("Another document already has that name.");
                    return;
                }

                await LogAsync(AuditAction.Create, newId,
                    $"Added document type '{name}' ({(_formMandatory ? "Required" : "Optional")}).");

                _editing = null;
                Toasts.ShowSuccess($"'{name}' added. People will see it on My Workspace and on their declarations.");
                await LoadAsync();
                return;
            }

            var previousName = _editing.Name;
            _editing.Name = name;
            _editing.Description = description;
            _editing.IsMandatory = _formMandatory;

            try
            {
                await DocumentTypeWriter.UpdateAsync(_editing);
            }
            catch (SqlException ex)
            {
                // The unique index on Name is the one a person can trip from here, by renaming a
                // document to one that already exists. Say which rule was broken rather than
                // letting a constraint name reach the screen, and put the list back the way the
                // database still has it.
                Toasts.ShowError(ex.Number is 2601 or 2627
                    ? "Another document already has that name."
                    : ex.Message);
                await LoadAsync();
                return;
            }

            await LogAsync(AuditAction.Update, _editing.Id, previousName == name
                ? $"Updated document type '{name}' ({(_formMandatory ? "Required" : "Optional")})."
                : $"Renamed document type '{previousName}' to '{name}' ({(_formMandatory ? "Required" : "Optional")}).");

            _editing = null;
            Toasts.ShowSuccess($"'{name}' saved.");
            await LoadAsync();
        }
        finally
        {
            _saving = false;
        }
    }

    // ---------- active / inactive ----------

    /// <summary>Justified like deactivating a job title. Switching a document off changes what
    /// everyone in the organisation is asked for on their next declaration, which is the kind of
    /// change the audit log should be able to answer "why" about.</summary>
    private async Task ToggleActiveAsync(string justification)
    {
        if (_pendingJustify is null) return;

        var type = _pendingJustify;
        _pendingJustify = null;

        type.IsActive = !type.IsActive;

        try
        {
            await DocumentTypeWriter.UpdateAsync(type);
        }
        catch (SqlException ex)
        {
            Toasts.ShowError(ex.Message);
            await LoadAsync();
            return;
        }

        await LogAsync(type.IsActive ? AuditAction.Reactivate : AuditAction.Deactivate, type.Id,
            $"'{type.Name}' is now {(type.IsActive ? "Active" : "Inactive")}.",
            justification);

        Toasts.ShowSuccess($"'{type.Name}' is now {(type.IsActive ? "Active" : "Inactive")}.");
    }

    // ---------- remove ----------

    private async Task DeleteAsync()
    {
        if (_pendingDelete is null) return;

        _saving = true;
        try
        {
            var type = _pendingDelete;
            try
            {
                await DocumentTypeWriter.DeleteAsync(type.Id);
            }
            catch (SqlException ex)
            {
                // usp_DocumentType_Delete throws with a message written to be read, so pass it on
                // as it stands: it says both why this was refused and what to do instead.
                Toasts.ShowError(ex.Message);
                return;
            }

            await LogAsync(AuditAction.Delete, type.Id, $"Removed document type '{type.Name}'.");

            _pendingDelete = null;
            Toasts.ShowSuccess($"'{type.Name}' removed.");
            await LoadAsync();
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task LogAsync(AuditAction action, int id, string details, string? justification = null)
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(
            state.User.Identity?.Name ?? "unknown",
            action, nameof(DocumentType), id.ToString(), details,
            justification: justification);
    }
}
