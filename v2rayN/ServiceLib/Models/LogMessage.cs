namespace ServiceLib.Models;

/// <summary>
/// 日志消息结构（支持实时多语言切换）
/// </summary>
public class LogMessage
{
    /// <summary>
    /// 状态标签资源键（用于动态获取翻译）
    /// </summary>
    public string StatusTagResourceKey { get; set; } = string.Empty;
    
    /// <summary>
    /// 用户友好文本（如 IP 地址、域名等）
    /// </summary>
    public string UserFriendlyText { get; set; } = string.Empty;
    
    /// <summary>
    /// 技术细节（原始英文日志，仅专业模式显示）
    /// </summary>
    public string TechnicalDetails { get; set; } = string.Empty;
    
    /// <summary>
    /// 日志级别（用于确定标签颜色）
    /// </summary>
    public LogMessageLevel Level { get; set; }
    
    /// <summary>
    /// 获取翻译后的状态标签
    /// </summary>
    private string GetTranslatedStatusTag()
    {
        if (string.IsNullOrEmpty(StatusTagResourceKey))
            return string.Empty;
        
        var translated = ServiceLib.Common.Logging.GetLocalizedString(StatusTagResourceKey, string.Empty);
        
        // 调试：记录翻译结果
        System.Diagnostics.Debug.WriteLine($"[LogMessage] Key: '{StatusTagResourceKey}', Translated: '{translated}'");
        
        // 如果翻译失败，返回一个默认的标签
        if (string.IsNullOrEmpty(translated) || translated == StatusTagResourceKey)
        {
            // 从资源键中提取简单的标签名（去掉 String_Log_ 前缀）
            var simpleName = StatusTagResourceKey.Replace("String_Log_", "");
            System.Diagnostics.Debug.WriteLine($"[LogMessage] Translation failed, using fallback: '[{simpleName}]'");
            return $"[{simpleName}]";
        }
        
        return translated;
    }
    
    /// <summary>
    /// 格式化显示（默认模式：仅人话）
    /// </summary>
    public string FormatDefault()
    {
        var statusTag = GetTranslatedStatusTag();
        return $"{statusTag} {UserFriendlyText}".Trim();
    }
    
    /// <summary>
    /// 格式化显示（专业模式：人话 + 技术细节）
    /// </summary>
    public string FormatProfessional()
    {
        if (string.IsNullOrWhiteSpace(TechnicalDetails))
        {
            return FormatDefault();
        }
        var statusTag = GetTranslatedStatusTag();
        return $"{statusTag} {UserFriendlyText}\n  └─ {TechnicalDetails}";
    }
}

public enum LogMessageLevel
{
    Info,
    Success,
    Warning,
    Error
}