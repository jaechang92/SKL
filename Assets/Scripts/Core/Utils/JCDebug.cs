using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace CustomDebug
{
    /// <summary>
    /// 유니티 6용 확장된 디버그 시스템
    /// </summary>
    public static class JCDebug
    {
        public enum LogLevel
        {
            Info,
            Warning,
            Error,
            Success,
            Custom
        }

        private static readonly Dictionary<LogLevel, string> LogColors = new Dictionary<LogLevel, string>
        {
            { LogLevel.Info, "#FFFFFF" },      // 흰색
            { LogLevel.Warning, "#FFFF00" },   // 노란색
            { LogLevel.Error, "#FF0000" },     // 빨간색
            { LogLevel.Success, "#00FF00" },   // 초록색
            { LogLevel.Custom, "#FF00FF" }     // 마젠타
        };

        private static bool _enableFileLogging = false;
        private static string _logFilePath = "";
        private static bool _enableStackTrace = true;
        private static bool _enableTimeStamp = true;

        // 설정 메서드들
        public static void EnableFileLogging(string fileName = "game_log.txt")
        {
            _enableFileLogging = true;
            _logFilePath = Path.Combine(Application.persistentDataPath, fileName);
        }

        public static void DisableFileLogging() => _enableFileLogging = false;
        public static void EnableStackTrace() => _enableStackTrace = true;
        public static void DisableStackTrace() => _enableStackTrace = false;
        public static void EnableTimeStamp() => _enableTimeStamp = true;
        public static void DisableTimeStamp() => _enableTimeStamp = false;

        // 기본 로그 메서드들
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Log(object message, LogLevel level = LogLevel.Info, bool HideLog = false)
        {
            if (HideLog) return;

            string formattedMessage = FormatMessage(message.ToString(), level);
            LogToConsole(formattedMessage, level);
            LogToFile(formattedMessage, level);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Log(object message, UnityEngine.Object context, LogLevel level = LogLevel.Info, bool HideLog = false)
        {
            if (HideLog) return;

            string formattedMessage = FormatMessage(message.ToString(), level);
            LogToConsole(formattedMessage, level, context);
            LogToFile(formattedMessage, level);
        }

        // 편의 메서드들
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(object message) => Log(message, LogLevel.Info);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warning(object message) => Log(message, LogLevel.Warning);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Error(object message) => Log(message, LogLevel.Error);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Success(object message) => Log(message, LogLevel.Success);

        // 색상 지정 로그
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void LogWithColor(object message, string hexColor)
        {
            string coloredMessage = $"<color={hexColor}>{message}</color>";
            UnityEngine.Debug.Log(FormatMessage(coloredMessage, LogLevel.Custom));
        }

        // 조건부 로그
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void LogIf(bool condition, object message, LogLevel level = LogLevel.Info)
        {
            if (condition) Log(message, level);
        }

        // 성능 측정용 로그
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void LogPerformance(string operationName, System.Action operation)
        {
            var stopwatch = Stopwatch.StartNew();
            operation?.Invoke();
            stopwatch.Stop();
            Log($"[PERFORMANCE] {operationName}: {stopwatch.ElapsedMilliseconds}ms", LogLevel.Info);
        }

        // 배열/리스트 로그
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void LogArray<T>(T[] array, string arrayName = "Array")
        {
            if (array == null)
            {
                Log($"{arrayName} is null", LogLevel.Warning);
                return;
            }

            Log($"{arrayName} ({array.Length} elements):", LogLevel.Info);
            for (int i = 0; i < array.Length; i++)
            {
                Log($"  [{i}]: {array[i]}", LogLevel.Info);
            }
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void LogList<T>(List<T> list, string listName = "List")
        {
            if (list == null)
            {
                Log($"{listName} is null", LogLevel.Warning);
                return;
            }

            Log($"{listName} ({list.Count} elements):", LogLevel.Info);
            for (int i = 0; i < list.Count; i++)
            {
                Log($"  [{i}]: {list[i]}", LogLevel.Info);
            }
        }

        // 객체 정보 로그
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void LogObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                Log("Object is null", LogLevel.Warning);
                return;
            }

            string info = $"Object Info:\n" +
                         $"  Name: {obj.name}\n" +
                         $"  Type: {obj.GetType().Name}\n" +
                         $"  Instance ID: {obj.GetInstanceID()}";

            if (obj is Component component)
            {
                info += $"\n  GameObject: {component.gameObject.name}";
                info += $"\n  Transform: {component.transform.position}";
            }

            Log(info, LogLevel.Info);
        }

        // 메시지 포맷팅
        private static string FormatMessage(string message, LogLevel level)
        {
            string formatted = "";

            // 시간 스탬프 추가
            if (_enableTimeStamp)
            {
                formatted += $"[{DateTime.Now:HH:mm:ss.fff}] ";
            }

            // 로그 레벨 추가
            formatted += $"[{level.ToString().ToUpper()}] ";

            // 색상 적용
            if (LogColors.ContainsKey(level))
            {
                formatted += $"<color={LogColors[level]}>{message}</color>";
            }
            else
            {
                formatted += message;
            }

            // 스택 트레이스 추가 (에러인 경우)
            if (_enableStackTrace && level == LogLevel.Error)
            {
                var stackTrace = new StackTrace(2, true);
                var frame = stackTrace.GetFrame(0);
                if (frame != null)
                {
                    formatted += $"\n  at {frame.GetMethod().DeclaringType?.Name}.{frame.GetMethod().Name}";
                    if (frame.GetFileName() != null)
                    {
                        formatted += $" in {Path.GetFileName(frame.GetFileName())}:line {frame.GetFileLineNumber()}";
                    }
                }
            }

            return formatted;
        }

        // 콘솔 출력
        private static void LogToConsole(string message, LogLevel level, UnityEngine.Object context = null)
        {
            switch (level)
            {
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(message, context);
                    break;
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(message, context);
                    break;
                default:
                    UnityEngine.Debug.Log(message, context);
                    break;
            }
        }

        // 파일 출력
        private static void LogToFile(string message, LogLevel level)
        {
            if (!_enableFileLogging || string.IsNullOrEmpty(_logFilePath)) return;

            try
            {
                // HTML 태그 제거 (파일용)
                string cleanMessage = System.Text.RegularExpressions.Regex.Replace(message, "<.*?>", "");
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {level} | {cleanMessage}\n";

                File.AppendAllText(_logFilePath, logEntry);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"파일 로그 쓰기 실패: {ex.Message}");
            }
        }

        // 로그 파일 정리
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void ClearLogFile()
        {
            if (_enableFileLogging && File.Exists(_logFilePath))
            {
                File.Delete(_logFilePath);
                Log("로그 파일이 정리되었습니다.", LogLevel.Info);
            }
        }

        // 현재 설정 출력
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void PrintCurrentSettings()
        {
            string settings = "MyDebug 현재 설정:\n" +
                             $"  파일 로깅: {(_enableFileLogging ? "활성화" : "비활성화")}\n" +
                             $"  파일 경로: {_logFilePath}\n" +
                             $"  스택 트레이스: {(_enableStackTrace ? "활성화" : "비활성화")}\n" +
                             $"  시간 스탬프: {(_enableTimeStamp ? "활성화" : "비활성화")}";

            Log(settings, LogLevel.Info);
        }
    }
}