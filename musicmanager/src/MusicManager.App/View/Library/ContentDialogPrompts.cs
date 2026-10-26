using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// The real dialogs of a Process run (Java: ProcessPrompts.SWING with JOptionPane), as stock Fluent ContentDialogs: a
/// title, the message (selectable, scrolls when it is long), and OK / Cancel for a question or a single Close button
/// for information, warnings and errors. The parent is whatever the window handed out as its frame: the window, an
/// element on a page, or a XamlRoot; without a usable parent the main window is used.
///
/// A ContentDialog is asynchronous, so the Async methods are the way to use it from a page. The IProcessPrompts methods
/// keep the contract of the interface: Confirm returns the answer (on the UI thread it keeps the message loop running
/// until the dialog is closed, from another thread it waits for the UI thread to do that); Info, Warn and Error have no
/// answer to wait for (on the UI thread they show the dialog and return, from another thread they return when the dialog
/// is closed). Only one ContentDialog can be open at a time, so every dialog of this class goes through one queue.
/// </summary>
public sealed class ContentDialogPrompts : IProcessPrompts
{
    // ------------------------------------------------------------------ IProcessPrompts (blocking contract)

    /// <summary>A question with OK and Cancel; true for OK.</summary>
    public bool Confirm(object parent, string title, string message)
    {
        return RunBlocking(() => ConfirmAsync(parent, title, message));
    }

    public void Info(object parent, string title, string message)
    {
        Tell(() => InfoAsync(parent, title, message));
    }

    public void Warn(object parent, string title, string message)
    {
        Tell(() => WarnAsync(parent, title, message));
    }

    public void Error(object parent, string title, string message)
    {
        Tell(() => ErrorAsync(parent, title, message));
    }

    // ------------------------------------------------------------------ asynchronous

    /// <summary>A question with OK and Cancel (or the given button texts); true for the primary button.</summary>
    public async Task<bool> ConfirmAsync(object parent, string title, string message, string primaryText = "OK",
            string closeText = "Cancel")
    {
        ContentDialog dialog = Build(parent, title, message, primaryText, closeText);
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public Task InfoAsync(object parent, string title, string message)
    {
        return ShowAsync(Build(parent, title, message, null, "OK"));
    }

    public Task WarnAsync(object parent, string title, string message)
    {
        return ShowAsync(Build(parent, title, message, null, "OK"));
    }

    public Task ErrorAsync(object parent, string title, string message)
    {
        return ShowAsync(Build(parent, title, message, null, "Close"));
    }

    // ------------------------------------------------------------------ building

    private static ContentDialog Build(object parent, string title, string message, string primary, string close)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootOf(parent),
            Title = title,
            // the dialog's own template scrolls a long message
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            },
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Close,
        };
        if (primary != null)
        {
            dialog.PrimaryButtonText = primary;
            dialog.DefaultButton = ContentDialogButton.Primary; // the accent button; Enter presses it
        }
        return dialog;
    }

    /// <summary>The XamlRoot the dialogs of this parent belong to.</summary>
    internal static XamlRoot RootOf(object parent)
    {
        XamlRoot root = parent switch
        {
            XamlRoot r => r,
            FrameworkElement element => element.XamlRoot,
            Window window => window.Content?.XamlRoot,
            _ => null,
        };
        return root ?? App.MusicManagerFrame?.Content?.XamlRoot;
    }

    // ------------------------------------------------------------------ one dialog at a time

    private static Task _last = Task.CompletedTask; // UI thread only; used only when there is no MusicManagerFrame

    /// <summary>
    /// Shows a ContentDialog (UI thread). A dialog that is asked for while another one is open waits for it instead of
    /// failing: the window has one queue for all dialogs of the app, this class falls back to its own without a window.
    /// The dialog takes the theme of the window, which a ContentDialog made in code does not do by itself.
    /// </summary>
    public static Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (dialog.XamlRoot == null)
        {
            dialog.XamlRoot = App.MusicManagerFrame?.Content?.XamlRoot;
        }
        if (dialog.XamlRoot == null)
        {
            return Task.FromException<ContentDialogResult>(
                    new InvalidOperationException("There is no window to show the dialog in."));
        }
        if (dialog.XamlRoot.Content is FrameworkElement root)
        {
            dialog.RequestedTheme = root.RequestedTheme;
        }
        if (App.MusicManagerFrame != null)
        {
            return App.MusicManagerFrame.ShowDialogAsync(dialog);
        }
        Task before = _last;
        Task<ContentDialogResult> shown = ShowAfter(before, dialog);
        _last = shown;
        return shown;
    }

    private static async Task<ContentDialogResult> ShowAfter(Task before, ContentDialog dialog)
    {
        try
        {
            await before;
        }
        catch (Exception)
        {
            // the dialog before this one failed; that is its caller's business
        }
        // a dialog of someone else may still be open: wait a moment and ask again
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await dialog.ShowAsync();
            }
            catch (Exception e) when ((e is COMException || e is InvalidOperationException) && attempt < 100)
            {
                await Task.Delay(100);
            }
        }
    }

    // ------------------------------------------------------------------ the blocking contract

    private const uint PmRemove = 0x0001;
    private const uint WmQuit = 0x0012;
    private const uint QsAllInput = 0x04FF;
    private const uint MwmoInputAvailable = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint milliseconds, uint wakeMask,
            uint flags);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    /// <summary>A dialog with nothing to answer: shown at once on the UI thread, waited for from any other thread.</summary>
    private static void Tell(Func<Task> show)
    {
        DispatcherQueue queue = App.MusicManagerFrame?.DispatcherQueue;
        if (queue != null && queue.HasThreadAccess)
        {
            _ = ShowAndForget(show);
            return;
        }
        RunBlocking(async () =>
        {
            await show();
            return true;
        });
    }

    private static async Task ShowAndForget(Func<Task> show)
    {
        try
        {
            await show();
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine("A dialog could not be shown: " + e);
        }
    }

    /// <summary>
    /// Runs the dialog and waits for its answer. On the UI thread the message loop keeps running meanwhile (a modal
    /// dialog of Swing does the same); from any other thread the UI thread is asked to run it.
    /// </summary>
    private static T RunBlocking<T>(Func<Task<T>> show)
    {
        DispatcherQueue queue = App.MusicManagerFrame?.DispatcherQueue;
        if (queue == null || queue.HasThreadAccess)
        {
            Task<T> task = show();
            PumpUntil(() => task.IsCompleted);
            return task.GetAwaiter().GetResult();
        }
        var done = new TaskCompletionSource<T>();
        if (!queue.TryEnqueue(async () =>
        {
            try
            {
                done.SetResult(await show());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }))
        {
            throw new InvalidOperationException("The window is gone: the dialog cannot be shown.");
        }
        return done.Task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> done)
    {
        while (!done())
        {
            while (PeekMessageW(out Msg msg, IntPtr.Zero, 0, 0, PmRemove))
            {
                if (msg.Message == WmQuit)
                {
                    PostQuitMessage((int)(uint)msg.WParam); // the window is closing: pass it on to the real loop
                    return;
                }
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
                if (done())
                {
                    return;
                }
            }
            if (!done())
            {
                MsgWaitForMultipleObjectsEx(0, null, 50, QsAllInput, MwmoInputAvailable);
            }
        }
    }
}
