namespace CGTOOL.Web.Data.Governance;

public enum ToastLevel { Info, Success, Warning, Error }

public record ToastMessage(Guid Id, ToastLevel Level, string Text);

/// <summary>Scoped per circuit: pub/sub for toast notifications, consumed by ToastContainer.</summary>
public class ToastService
{
    public event Action<ToastMessage>? OnShow;

    public void Show(string text, ToastLevel level = ToastLevel.Info) => OnShow?.Invoke(new ToastMessage(Guid.NewGuid(), level, text));

    public void ShowError(string text) => Show(text, ToastLevel.Error);

    public void ShowSuccess(string text) => Show(text, ToastLevel.Success);

    public void ShowWarning(string text) => Show(text, ToastLevel.Warning);
}
