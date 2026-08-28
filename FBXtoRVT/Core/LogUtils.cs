using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 디버그용 파일 로그 유틸.
    /// %AppData%\FBXtoRVT\FBXtoRVTLogs 에 날짜별 텍스트 파일로 남긴다.
    /// </summary>
    public static class LogUtils
    {
        private static readonly string _baseDir =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FBXtoRVT",
                "FBXtoRVTLogs");

        public static void Log(
            string message,
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            try
            {
                string className = Path.GetFileNameWithoutExtension(callerFilePath);
                string prefix = $"[{className}.{callerMemberName}] ";

                string fileName = $"log_{DateTime.Now:yyyy-MM-dd}.txt";
                string fullPath = Path.Combine(_baseDir, fileName);

                Directory.CreateDirectory(_baseDir);

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {prefix}{message}";

                File.AppendAllText(fullPath, line + Environment.NewLine);

                System.Diagnostics.Debug.WriteLine(line);
            }
            catch
            {
                // 로그 실패로 애드인이 멈추면 안 되므로 무시
            }
        }

        public static void LogError(
            Exception ex,
            string message = "",
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            Log($"[ERROR] {ex.Message}\n{ex.StackTrace}. " + message, callerFilePath, callerMemberName);
        }
    }
}
