using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;

public class Build
{
    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        var targetDir = Path.Combine(Path.GetDirectoryName(pathToBuiltProject), MainWindowLogic.SettingsDir);
        Directory.CreateDirectory(targetDir);
        foreach (var path in Directory.GetFiles(MainWindowLogic.SettingsDir))
        {
            File.Copy(path, Path.Combine(targetDir, Path.GetFileName(path)), true);
        }
    }
}
