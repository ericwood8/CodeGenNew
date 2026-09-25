using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeGenNew.App.ViewModels;

/// <summary> Base for a dialog ViewModel that shows a single status/error line bound to an InfoBar's
/// IsOpen (via HasStatusMessage) and Message (Docs/specs.md sections 9.1-9.3) -- shared by
/// ConnectionDialogViewModel, LocationDialogViewModel and TemplateManagementViewModel, which used to
/// each declare this identically. MainViewModel does NOT derive from this: its own StatusMessage change
/// handler computes StatusBrush instead of HasStatusMessage, and CommunityToolkit.Mvvm's
/// [ObservableProperty] partial method needs to live in the same class as the field it's generated
/// from, so the two behaviors can't share one _statusMessage field. </summary>
public abstract partial class StatusMessageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "";

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));
}
