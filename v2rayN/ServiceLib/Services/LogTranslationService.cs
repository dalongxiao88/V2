using ServiceLib.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ServiceLib.Services;

/// <summary>
/// 日志翻译服务 - 将英文日志翻译为用户友好的本地化文本
/// </summary>
public class LogTranslationService
{
    private static readonly Dictionary<string, string> _logPatterns = new()
    {
        // 连接相关
        { "accepted", "String_Log_Accepted" },
        { "rejected", "String_Log_Rejected" },
        { "closed", "String_Log_Closed" },
        
        // 超时相关
        { "deadline exceeded", "String_Log_Timeout" },
        { "timeout", "String_Log_Timeout" },
        { "timed out", "String_Log_Timeout" },
        
        // 错误相关
        { "failed", "String_Log_Failed" },
        { "error", "String_Log_Error" },
        { "denied", "String_Log_Denied" },
        
        // 成功相关
        { "success", "String_Log_Success" },
        { "connected", "String_Log_Connected" },
        { "established", "String_Log_Established" },
        
        // DNS 相关
        { "dns query", "String_Log_DnsQuery" },
        { "dns response", "String_Log_DnsResponse" },
        
        // 代理相关
        { "proxy", "String_Log_Proxy" },
        { "direct", "String_Log_Direct" },
        { "blocked", "String_Log_Blocked" }
    };

    // 翻译缓存（提升性能）
    private static readonly ConcurrentDictionary<string, LogMessage> _translationCache = new();
    private const int MaxCacheSize = 1000;

    /// <summary>
    /// 翻译日志消息（人话优先模式）
    /// </summary>
    /// <param name="rawMessage">原始日志消息</param>
    /// <param name="showTechnicalDetails">是否显示技术细节（专业模式）</param>
    /// <returns>翻译后的消息结构</returns>
    public static LogMessage TranslateLog(string rawMessage, bool showTechnicalDetails = false)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return new LogMessage
            {
                StatusTagResourceKey = "String_Log_Info",
                UserFriendlyText = rawMessage,
                TechnicalDetails = rawMessage,
                Level = LogMessageLevel.Info
            };
        }

        // 生成缓存键
        var cacheKey = $"{rawMessage}_{showTechnicalDetails}";
        
        // 检查缓存
        if (_translationCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // 执行翻译
        var translated = PerformTranslation(rawMessage, showTechnicalDetails);
        
        // 缓存结果（限制缓存大小）
        if (_translationCache.Count < MaxCacheSize)
        {
            _translationCache.TryAdd(cacheKey, translated);
        }
        
        return translated;
    }

    /// <summary>
    /// 执行实际的翻译逻辑
    /// </summary>
    private static LogMessage PerformTranslation(string rawMessage, bool showTechnicalDetails)
    {
        var lowerMessage = rawMessage.ToLower();
        
        // 先尝试匹配内核日志前缀（如 [Info], [Warning] 等）
        var kernelPrefixMatch = Regex.Match(rawMessage, @"^\[(\w+)\]\s*(.*)$");
        if (kernelPrefixMatch.Success)
        {
            var levelStr = kernelPrefixMatch.Groups[1].Value.ToLower();
            var content = kernelPrefixMatch.Groups[2].Value;
            
            var resourceKey = levelStr switch
            {
                "info" => "String_Log_Info",
                "warning" => "String_Log_Warning",
                "error" => "String_Log_Error",
                "debug" => "String_Log_Debug",
                _ => "String_Log_Info"
            };
            
            var level = levelStr switch
            {
                "error" => LogMessageLevel.Error,
                "warning" => LogMessageLevel.Warning,
                _ => LogMessageLevel.Info
            };
            
            return new LogMessage
            {
                StatusTagResourceKey = resourceKey,
                UserFriendlyText = content,
                TechnicalDetails = showTechnicalDetails ? rawMessage : string.Empty,
                Level = level
            };
        }
        
        // 遍历所有模式，查找匹配
        foreach (var pattern in _logPatterns)
        {
            if (lowerMessage.Contains(pattern.Key))
            {
                // 提取关键信息（如 IP 地址、域名等）
                var details = ExtractDetails(rawMessage, pattern.Key);
                
                // 如果没有提取到详细信息，使用资源键对应的默认文本
                var userFriendlyText = string.IsNullOrWhiteSpace(details)
                    ? string.Empty  // 将由 StatusTag 提供文本
                    : details;
                
                // 确定日志级别
                var level = GetLogLevel(rawMessage);
                
                return new LogMessage
                {
                    StatusTagResourceKey = pattern.Value,  // 使用资源键，支持实时翻译
                    UserFriendlyText = userFriendlyText,
                    TechnicalDetails = showTechnicalDetails ? rawMessage : string.Empty,
                    Level = level
                };
            }
        }
        
        // 如果没有匹配到任何模式，返回原始消息
        return new LogMessage
        {
            StatusTagResourceKey = "String_Log_Info",  // 使用资源键
            UserFriendlyText = rawMessage,
            TechnicalDetails = showTechnicalDetails ? rawMessage : string.Empty,
            Level = LogMessageLevel.Info
        };
    }

    /// <summary>
    /// 从原始消息中提取关键信息（IP、域名等）
    /// </summary>
    private static string ExtractDetails(string rawMessage, string keyword)
    {
        // 先尝试提取域名（优先显示域名而不是 IP）
        var domainMatch = Regex.Match(
            rawMessage,
            @"\b[a-zA-Z0-9][-a-zA-Z0-9]{0,62}(\.[a-zA-Z0-9][-a-zA-Z0-9]{0,62})+(?::\d+)?\b"
        );
        if (domainMatch.Success)
        {
            var domain = domainMatch.Value;
            // 过滤掉一些不需要显示的域名
            if (!domain.Contains("localhost") && !domain.Contains("127.0.0.1"))
            {
                return domain;
            }
        }
        
        // 提取所有 IP 地址（包括端口）
        var ipMatches = Regex.Matches(
            rawMessage,
            @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(?::\d+)?\b"
        );
        
        // 过滤掉本地 IP（127.0.0.1），只显示外部 IP
        foreach (Match match in ipMatches)
        {
            var ip = match.Value;
            if (!ip.StartsWith("127.0.0.1") && !ip.StartsWith("0.0.0.0") && !ip.StartsWith("::1"))
            {
                return ip;
            }
        }
        
        // 如果只有本地 IP，返回简化的连接信息
        if (keyword.Equals("accepted", StringComparison.OrdinalIgnoreCase))
        {
            return "新连接";
        }
        else if (keyword.Equals("timeout", StringComparison.OrdinalIgnoreCase) ||
                 keyword.Equals("deadline exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return "连接超时";
        }
        else if (keyword.Equals("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return "连接被拒绝";
        }
        else if (keyword.Equals("closed", StringComparison.OrdinalIgnoreCase))
        {
            return "连接已关闭";
        }
        
        return string.Empty;
    }

    /// <summary>
    /// 获取日志级别（用于高亮显示）
    /// </summary>
    public static LogMessageLevel GetLogLevel(string message)
    {
        var lowerMessage = message.ToLower();
        
        if (lowerMessage.Contains("error") || lowerMessage.Contains("failed") ||
            lowerMessage.Contains("denied") || lowerMessage.Contains("rejected"))
        {
            return LogMessageLevel.Error;
        }
        
        if (lowerMessage.Contains("warning") || lowerMessage.Contains("timeout") ||
            lowerMessage.Contains("deadline exceeded"))
        {
            return LogMessageLevel.Warning;
        }
        
        if (lowerMessage.Contains("success") || lowerMessage.Contains("connected") ||
            lowerMessage.Contains("established") || lowerMessage.Contains("accepted"))
        {
            return LogMessageLevel.Success;
        }
        
        return LogMessageLevel.Info;
    }

    /// <summary>
    /// 清空翻译缓存
    /// </summary>
    public static void ClearCache()
    {
        _translationCache.Clear();
    }
}