using Microsoft.AspNetCore.Components;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Components.Pages.Admin;

/// <summary>The list of documents people are asked for, and whether they have to supply one.
///
/// It replaces a static array in My Register and a run of hardcoded Required/Optional pills in the
/// declaration -- two lists that could disagree with each other and neither changeable without a
/// redeploy. Both now render from this table.
///
/// Edits save as they are made, like the nav menu in Settings, rather than collecting into a Save
/// button. There are a handful of rows and each change is one field; a dirty-state machine over
/// that would be more to get wrong than it saves.</summary>
public partial class DocumentCenter
{
    private List<DocumentType>? _types;

    /// <summary>How many documents exist of each type, for the "On file" column and to explain why
    /// Remove is refused. Only added types can be counted this way -- the built-ins' documents are
    /// identified by Kind and carry no DocumentTypeId, so they are counted separately.</summary>
    private Dictionary<int, int> _uploadCounts = [];

    private bool _adding;
    private bool _saving;
    private string _newName = string.Empty;
    private string? _newDescription;
    private bool _newMandatory;
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
        // come the other way round. Without this the three best-known rows would all read "0".
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
        _uploadCounts.TryGetValue(typeId, out var count) && count > 0 ? $"{count} on file" : "none";

    private async Task RenameAsync(DocumentType type, string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            Toasts.ShowError("A document needs a name.");
            await LoadAsync();
            return;
        }

        if (name == type.Name) return;

        var previous = type.Name;
        type.Name = name;
        await SaveAsync(type, AuditAction.Update, $"Renamed document type '{previous}' to '{name}'.");
    }

    private async Task RedescribeAsync(DocumentType type, string? value)
    {
        var description = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (description == type.Description) return;

        type.Description = description;
        await SaveAsync(type, AuditAction.Update, $"Changed what is asked for under '{type.Name}'.");
    }

    /// <summary>Takes the raw select value rather than a bool because the comparison cannot live
    /// in the markup: a string literal inside a Razor attribute ends the attribute.</summary>
    private async Task SetMandatoryAsync(DocumentType type, object? value)
    {
        var mandatory = value?.ToString() == "required";
        if (mandatory == type.IsMandatory) return;

        type.IsMandatory = mandatory;
        await SaveAsync(type, AuditAction.Update,
            $"'{type.Name}' is now {(mandatory ? "Required" : "Optional")}.");
    }

    private async Task SetActiveAsync(DocumentType type, object? value)
    {
        var active = value?.ToString() == "active";
        if (active == type.IsActive) return;

        type.IsActive = active;
        await SaveAsync(type, active ? AuditAction.Reactivate : AuditAction.Deactivate,
            $"'{type.Name}' is now {(active ? "Active" : "Inactive")}.");
    }

    private async Task SaveAsync(DocumentType type, AuditAction action, string message)
    {
        try
        {
            await DocumentTypeWriter.UpdateAsync(type);
        }
        catch (SqlException ex)
        {
            // The unique index on Name is the one a person can trip from here, by renaming a
            // document to one that already exists. Say which rule was broken rather than letting a
            // constraint name reach the screen.
            Toasts.ShowError(ex.Number is 2601 or 2627
                ? "Another document already has that name."
                : ex.Message);
            await LoadAsync();
            return;
        }

        var state = await AuthState.GetAuthenticationStateAsync();
        await AuditLog.LogAsync(
            state.User.Identity?.Name ?? "unknown",
            action, nameof(DocumentType), type.Id.ToString(), message);

        Toasts.ShowSuccess(message);
    }

    private void StartAdd()
    {
        _newName = string.Empty;
        _newDescription = null;
        _newMandatory = false;
        _adding = true;
    }

    private void CancelAdd() => _adding = false;

    private async Task AddAsync()
    {
        var name = _newName.Trim();
        if (name.Length == 0)
        {
            Toasts.ShowError("A document needs a name.");
            return;
        }

        _saving = true;
        try
        {
            // Last in the list rather than wherever the name sorts: a new document is the one thing
            // on the screen whose place the person who added it already has in mind.
            var sortOrder = (_types?.Count > 0 ? _types.Max(t => t.SortOrder) : 0) + 10;

            var type = new DocumentType
            {
                Name = name,
                Description = string.IsNullOrWhiteSpace(_newDescription) ? null : _newDescription.Trim(),
                IsMandatory = _newMandatory,
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

            var state = await AuthState.GetAuthenticationStateAsync();
            await AuditLog.LogAsync(
                state.User.Identity?.Name ?? "unknown",
                AuditAction.Create, nameof(DocumentType), newId.ToString(),
                $"Added document type '{name}' ({(_newMandatory ? "Required" : "Optional")}).");

            _adding = false;
            Toasts.ShowSuccess($"'{name}' added. People will see it on My Register and on their declarations.");
            await LoadAsync();
        }
        finally
        {
            _saving = false;
        }
    }

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

            var state = await AuthState.GetAuthenticationStateAsync();
            await AuditLog.LogAsync(
                state.User.Identity?.Name ?? "unknown",
                AuditAction.Delete, nameof(DocumentType), type.Id.ToString(),
                $"Removed document type '{type.Name}'.");

            _pendingDelete = null;
            Toasts.ShowSuccess($"'{type.Name}' removed.");
            await LoadAsync();
        }
        finally
        {
            _saving = false;
        }
    }
}
