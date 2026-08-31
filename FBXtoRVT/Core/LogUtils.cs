using System;
using System.IO;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 파일 로그 유틸.
    /// %AppData%\FBXtoRVT\FBXtoRVTLogs 에 날짜별 텍스트 파일로 남긴다.
    ///
    /// [로깅 정책] — 모든 기능이 아래 세 가지만 쓴다.
    ///
    ///  1) <see cref="Log"/>       : 기능 시작 / 종료 요약처럼 <b>실행당 몇 줄</b> 뿐인 기록. 항상 남는다.
    ///  2) <see cref="LogDetail"/> : 객체 하나하나를 따라가는 <b>상세 진단</b> 기록.
    ///                               <see cref="DetailEnabled"/> 가 켜져 있을 때만 남는다.
    ///  3) <see cref="LogError"/>  : 예외 / 실패 기록. 항상 남는다.
    ///
    /// 반복문 안에서 LogDetail 을 부를 때는 <b>반드시</b> 아래처럼 감싼다.
    /// 그렇지 않으면 로그를 꺼 두어도 문자열을 만드는 비용이 그대로 든다.
    ///
    /// <code>
    /// if (LogUtils.DetailEnabled)
    ///     LogUtils.LogDetail($"후보 FLANGE(Id={id}) ...");
    /// </code>
    ///
    /// [상세 로그 켜는 법]
    /// %AppData%\FBXtoRVT\debug.on 이라는 <b>빈 파일</b>을 만들어 두고 Revit 을 다시 켜면 된다.
    /// (파일만 지우면 다시 꺼진다. 다시 빌드할 필요가 없다)
    /// </summary>
    public static class LogUtils
    {
        private static readonly string _appDir =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FBXtoRVT");

        private static readonly string _logDir = Path.Combine(_appDir, "FBXtoRVTLogs");

        // 이 파일이 있으면 상세 로그를 켠다.
        private const string DetailSwitchFileName = "debug.on";

        /// <summary>
        /// 상세 로그(LogDetail) 를 남길지 여부.
        /// Revit 시작 시 %AppData%\FBXtoRVT\debug.on 파일이 있는지로 정해지며, 코드로 바꿀 수도 있다.
        /// </summary>
        public static bool DetailEnabled { get; set; }

        static LogUtils()
        {
            try
            {
                DetailEnabled = File.Exists(Path.Combine(_appDir, DetailSwitchFileName));
            }
            catch
            {
                DetailEnabled = false;
            }
        }

        /// <summary>
        /// 요약 기록. (기능 시작 / 종료처럼 실행당 몇 줄뿐인 내용만)
        /// </summary>
        public static void Log(
            string message,
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            Write(message, callerFilePath, callerMemberName);
        }

        /// <summary>
        /// 상세 진단 기록. DetailEnabled 가 켜져 있을 때만 남는다.
        /// 반복문 안에서 부를 때는 호출하는 쪽을 if (LogUtils.DetailEnabled) 로 감쌀 것.
        /// </summary>
        public static void LogDetail(
            string message,
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            if (!DetailEnabled) return;

            Write(message, callerFilePath, callerMemberName);
        }

        /// <summary>
        /// 예외 / 실패 기록. 항상 남는다.
        /// </summary>
        public static void LogError(
            Exception ex,
            string message = "",
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            string detail = (ex == null) ? "(예외 없음)" : $"{ex.Message}\n{ex.StackTrace}";
            Write($"[ERROR] {message} {detail}", callerFilePath, callerMemberName);
        }

        /// <summary>
        /// 좌표를 로그용 문자열로. (여러 기능이 같은 모양으로 남기도록 여기 모아 둔다)
        /// </summary>
        public static string FormatXyz(XYZ p)
        {
            return p == null ? "null" : $"({p.X:F3}, {p.Y:F3}, {p.Z:F3})";
        }

        /// <summary>
        /// 실제 파일 기록. 로그 때문에 애드인이 멈추면 안 되므로 실패는 무시한다.
        /// </summary>
        private static void Write(string message, string callerFilePath, string callerMemberName)
        {
            try
            {
                string className = Path.GetFileNameWithoutExtension(callerFilePath);
                string prefix = $"[{className}.{callerMemberName}] ";

                string fileName = $"log_{DateTime.Now:yyyy-MM-dd}.txt";
                string fullPath = Path.Combine(_logDir, fileName);

                Directory.CreateDirectory(_logDir);

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {prefix}{message}";

                File.AppendAllText(fullPath, line + Environment.NewLine);

                System.Diagnostics.Debug.WriteLine(line);
            }
            catch
            {
                // 로그 실패로 애드인이 멈추면 안 되므로 무시
            }
        }
    }
}
