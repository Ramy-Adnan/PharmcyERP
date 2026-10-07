// WPF command-notification plumbing only. No GUI/input/printing is simulated by this shim.
// POS tests exercise linked production view-model sources and real services over EF InMemory.
namespace System.Windows.Input;
internal static class CommandManager
{
    public static event EventHandler RequerySuggested { add { } remove { } }
    public static void InvalidateRequerySuggested() { }
}
