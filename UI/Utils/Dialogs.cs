// GeoscientistToolkit/Util/Dialogs.cs
// This is the definitive version, rewritten based on the compiler error evidence.
// It uses simple return types and direct arguments, not tuples or FileFilter objects.

// This is the correct namespace.

namespace GeoscientistToolkit.Util;

public static class Dialogs
{
    public static string SelectFolderDialog(string title)
    {
        // The compiler proves this method returns a single string.
        // A null or empty string likely indicates cancellation.
        // The deconstruction syntax was incorrect.
        return TinyDialogsNet.Dialogs.SelectFolderDialog(title);
    }

    public static string OpenFileDialog(string title, string[] filters, string filterDescription)
    {
        // The compiler proves this method does not take a FileFilter object
        // and does not return a tuple. It takes the filter info directly
        // and returns a collection of strings.
        var paths = TinyDialogsNet.Dialogs.OpenFileDialog(
            title,
            "",
            filters,
            filterDescription // allowMultipleSelections
        );

        // The dialog returns a collection. We want the first file, or null if canceled.
        // FirstOrDefault() safely handles both cases.
        return paths?.FirstOrDefault();
    }
}