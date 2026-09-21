using System.Runtime.InteropServices;
using System.Text;

namespace CodeGenNew.App.Services;

/// <summary>
/// Wraps the native Win32 SHBrowseForFolder folder-picker (with BIF_NEWDIALOGSTYLE, so it's the normal
/// modern resizable dialog, not the old tiny fixed one). Used instead of
/// Windows.Storage.Pickers.FolderPicker, which has no way to open at a specific starting directory (only
/// a fixed PickerLocationId enum) -- Bugs1.txt item 1. System.Windows.Forms.FolderBrowserDialog would be
/// simpler but UseWindowsForms conflicts with the Windows App SDK's own XAML "Page" build items in this
/// project (MC6000 "must include PresentationCore, PresentationFramework"), so this goes straight to the
/// Win32 API instead.
/// </summary>
internal static class NativeFolderPicker
{
    public static string? PickFolder(IntPtr ownerHwnd, string title, string? initialDirectory)
    {
        IntPtr displayNameBuffer = Marshal.AllocHGlobal(520); // MAX_PATH (260) wide chars
        IntPtr initialDirectoryPtr = IntPtr.Zero;
        try
        {
            initialDirectoryPtr = Marshal.StringToHGlobalUni(initialDirectory ?? "");

            var info = new BROWSEINFO
            {
                hwndOwner = ownerHwnd,
                pidlRoot = IntPtr.Zero,
                pszDisplayName = displayNameBuffer,
                lpszTitle = title,
                ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
                lpfn = InitDialogCallback,
                lParam = initialDirectoryPtr,
                iImage = 0
            };

            IntPtr pidl = SHBrowseForFolder(ref info);
            if (pidl == IntPtr.Zero)
                return null;

            try
            {
                var path = new StringBuilder(260);
                return SHGetPathFromIDList(pidl, path) ? path.ToString() : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(displayNameBuffer);
            if (initialDirectoryPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(initialDirectoryPtr);
        }
    }

    // Fires once, right after the dialog's HWND exists, so we can seed the initial selection --
    // BROWSEINFO itself has no "starting directory" field, only this callback-based mechanism.
    private static int InitDialogCallback(IntPtr hwnd, uint msg, IntPtr lParam, IntPtr lpData)
    {
        if (msg == BFFM_INITIALIZED && lpData != IntPtr.Zero)
            SendMessage(hwnd, BFFM_SETSELECTIONW, new IntPtr(1), lpData);
        return 0;
    }

    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;
    private const uint BFFM_INITIALIZED = 1;
    private const uint BFFM_SETSELECTIONW = 0x0467;

    private delegate int BrowseCallbackProc(IntPtr hwnd, uint msg, IntPtr lParam, IntPtr lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public BrowseCallbackProc lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
