using NLog;
using NLog.Config;
using NLog.Targets;
using ServiceLib.Models;
using ServiceLib.Services;

namespace ServiceLib.Common;

public class Logging
{
    private static readonly Logger _logger1 = LogManager.GetLogger("Log1");
    private static readonly Logger _logger2 = LogManager.GetLogger("Log2");
    private static bool _enableTranslation = true;
    private static bool _showTechnicalDetails = false;

    /// <summary>
    /// 日志消息接收事件（用于通知 UI）
    /// </summary>
    public static event Action<LogMessage>? LogMessageReceived;

    /// <summary>
    /// 语言切换事件（由 UI 层触发，通知 ServiceLib 层）
    /// </summary>
    public static event Action<string>? LanguageChanged;

    /// <summary>
    /// 本地化字符串获取委托（由 UI 层注入）
    /// </summary>
    public static Func<string, string, string>? GetLocalizedStringFunc { get; set; }

    /// <summary>
    /// 获取本地化字符串的辅助方法
    /// </summary>
    public static string GetLocalizedString(string key, string defaultValue)
    {
        try
        {
            // 优先使用注入的函数
            if (GetLocalizedStringFunc != null)
            {
                var result = GetLocalizedStringFunc.Invoke(key, defaultValue);
                System.Diagnostics.Debug.WriteLine($"[Logging.GetLocalizedString] Key: '{key}', Result: '{result}', Default: '{defaultValue}'");
                if (!string.IsNullOrEmpty(result) && result != key)
                {
                    return result;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Logging.GetLocalizedString] GetLocalizedStringFunc is NULL for key: '{key}'");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Logging.GetLocalizedString] Exception for key '{key}': {ex.Message}");
            _logger2?.Error($"Failed to get localized string for key '{key}': {ex.Message}");
        }
        
        System.Diagnostics.Debug.WriteLine($"[Logging.GetLocalizedString] Returning default for key '{key}': '{defaultValue}'");
        return defaultValue;
    }

    public static void Setup()
    {
        LoggingConfiguration config = new();
        FileTarget fileTarget = new();
        config.AddTarget("file", fileTarget);
        fileTarget.Layout = "${longdate}-${level:uppercase=true} ${message}";
        fileTarget.FileName = Utils.GetLogPath("${shortdate}.txt");
        config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, fileTarget));
        LogManager.Configuration = config;
    }

    public static void LoggingEnabled(bool enable)
    {
        if (!enable)
        {
            LogManager.SuspendLogging();
        }
    }

    /// <summary>
    /// 启用或禁用日志翻译
    /// </summary>
    public static void EnableTranslation(bool enable)
    {
        _enableTranslation = enable;
    }

    /// <summary>
    /// 切换专业模式（显示/隐藏技术细节）
    /// </summary>
    public static void ToggleProfessionalMode(bool enable)
    {
        _showTechnicalDetails = enable;
    }

    /// <summary>
    /// 保存日志（带翻译支持）
    /// </summary>
    public static void SaveLog(string strContent)
    {
        if (!LogManager.IsLoggingEnabled())
        {
            return;
        }

        // 如果启用翻译，先翻译日志
        var logMessage = _enableTranslation
            ? LogTranslationService.TranslateLog(strContent, _showTechnicalDetails)
            : new LogMessage
            {
                StatusTagResourceKey = "String_Log_Info",
                UserFriendlyText = strContent,
                TechnicalDetails = strContent,
                Level = LogMessageLevel.Info
            };

        // 记录到文件（使用专业模式格式，保留完整信息）
        _logger1.Info(logMessage.FormatProfessional());
        
        // 发送到 UI（通过事件）
        NotifyUI(logMessage);
    }

    public static void SaveLog(string strTitle, Exception ex)
    {
        if (!LogManager.IsLoggingEnabled())
        {
            return;
        }

        _logger2.Debug($"{strTitle},{ex.Message}");
        _logger2.Debug(ex.StackTrace);
        if (ex?.InnerException != null)
        {
            _logger2.Error(ex.InnerException);
        }

        // 也通知 UI 显示错误日志
        var errorMessage = new LogMessage
        {
            StatusTagResourceKey = "String_Log_Error",
            UserFriendlyText = $"{strTitle}: {ex.Message}",
            TechnicalDetails = ex.StackTrace ?? string.Empty,
            Level = LogMessageLevel.Error
        };
        NotifyUI(errorMessage);
    }

    /// <summary>
    /// 通知 UI 显示日志
    /// </summary>
    private static void NotifyUI(LogMessage logMessage)
    {
        try
        {
            LogMessageReceived?.Invoke(logMessage);
        }
        catch (Exception ex)
        {
            // 避免 UI 通知失败影响日志记录
            _logger2.Error($"Failed to notify UI: {ex.Message}");
        }
    }

    /// <summary>
    /// 触发语言切换事件（由 UI 层调用）
    /// </summary>
    public static void NotifyLanguageChanged(string newLanguage)
    {
        try
        {
            LanguageChanged?.Invoke(newLanguage);
        }
        catch (Exception ex)
        {
            _logger2.Error($"Failed to notify language change: {ex.Message}");
        }
    }
}
