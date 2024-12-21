using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class Build : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        var logic = Resources.Load<MainWindowLogic>("MainWindowLogic");
        logic.ResetBeforeBuild();
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        var targetDir = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), MainWindowLogic.SettingsDir);
        Directory.CreateDirectory(targetDir);
        foreach (var path in Directory.GetFiles(MainWindowLogic.SettingsDir))
        {
            File.Copy(path, Path.Combine(targetDir, Path.GetFileName(path)), true);
        }
    }
}
