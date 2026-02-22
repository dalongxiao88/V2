using System.Windows;

namespace v2rayN.Manager;

/// <summary>
/// 语言管理器 - 负责运行时动态切换语言资源
/// </summary>
public sealed class LanguageManager
{
    private static readonly Lazy<LanguageManager> _instance = new(() => new());
    public static LanguageManager Instance => _instance.Value;

    private const string DefaultLanguage = "zh-CN";
    private const string ResourcePathTemplate = "Resources/Languages/Lang.{0}.xaml";
    
    private string _currentLanguage = DefaultLanguage;
    private ResourceDictionary? _currentLanguageDict;

    /// <summary>
    /// 语言切换事件 - 当语言改变时触发
    /// </summary>
    public static event Action<string>? LanguageChanged;

    /// <summary>
    /// 当前语言代码
    /// </summary>
    public string CurrentLanguage => _currentLanguage;

    /// <summary>
    /// 支持的语言列表
    /// </summary>
    public static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        { "zh-CN", "简体中文" },
        { "en-US", "English" }
    };

    private LanguageManager()
    {
    }

    /// <summary>
    /// 初始化语言管理器
    /// 从配置文件读取语言设置并加载对应的资源字典
    /// </summary>
    /// <param name="languageCode">语言代码，如 zh-CN 或 en-US</param>
    public void Initialize(string? languageCode = null)
    {
        // 如果没有指定语言，尝试从配置读取
        if (languageCode.IsNullOrEmpty())
        {
            languageCode = AppManager.Instance.Config?.UiItem?.CurrentLanguage;
        }

        // 如果配置中也没有，使用默认语言
        if (languageCode.IsNullOrEmpty() || !SupportedLanguages.ContainsKey(languageCode))
        {
            languageCode = DefaultLanguage;
        }

        // 加载语言资源
        ChangeLanguage(languageCode);
    }

    /// <summary>
    /// 切换语言
    /// 动态替换应用程序的资源字典，实现无需重启的语言切换
    /// </summary>
    /// <param name="languageCode">目标语言代码</param>
    /// <returns>是否切换成功</returns>
    public bool ChangeLanguage(string languageCode)
    {
        if (!SupportedLanguages.ContainsKey(languageCode))
        {
            Logging.SaveLog($"Unsupported language: {languageCode}");
            return false;
        }

        if (_currentLanguage == languageCode && _currentLanguageDict != null)
        {
            return true; // 已经是当前语言，无需切换
        }

        try
        {
            // 构建资源字典路径
            var resourcePath = string.Format(ResourcePathTemplate, languageCode);
            var uri = new Uri(resourcePath, UriKind.Relative);

            // 加载新的语言资源字典
            var newDict = new ResourceDictionary { Source = uri };

            // 获取应用程序资源字典集合
            var appResources = Application.Current.Resources.MergedDictionaries;

            // 🔑 关键步骤 1：移除旧的语言资源字典
            // 使用更强健的查找方式，确保移除所有语言相关的资源字典
            var oldDicts = appResources
                .Where(d => d.Source != null && d.Source.OriginalString.Contains("Languages/"))
                .ToList();
            
            foreach (var oldDict in oldDicts)
            {
                appResources.Remove(oldDict);
            }

            // 🔑 关键步骤 2：添加新的语言资源字典
            // 这一步会触发所有使用 DynamicResource 的 UI 元素更新
            appResources.Add(newDict);

            // 🔑 关键步骤 3：强制刷新 UI 树
            // 确保所有窗口和控件都能接收到资源更新通知
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (Window window in Application.Current.Windows)
                {
                    // 刷新窗口
                    window.InvalidateVisual();
                    window.UpdateLayout();
                    
                    // 特殊处理：刷新所有 DataGrid 的列头
                    RefreshDataGridHeaders(window);
                }
            }, System.Windows.Threading.DispatcherPriority.Render);

            // 更新当前语言状态
            _currentLanguageDict = newDict;
            _currentLanguage = languageCode;

            // 保存到配置文件
            SaveLanguageToConfig(languageCode);

            // 触发语言切换事件（UI 层）
            LanguageChanged?.Invoke(languageCode);

            // 通知 ServiceLib 层语言已切换
            Logging.NotifyLanguageChanged(languageCode);

            Logging.SaveLog($"Language changed to: {languageCode} ({SupportedLanguages[languageCode]})");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Failed to change language to {languageCode}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取指定 Key 的本地化字符串（实例方法）
    /// </summary>
    /// <param name="key">资源 Key</param>
    /// <param name="defaultValue">如果找不到资源时的默认值</param>
    /// <returns>本地化字符串</returns>
    public string GetString(string key, string? defaultValue = null)
    {
        try
        {
            if (Application.Current.TryFindResource(key) is string value)
            {
                return value;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Failed to get resource string for key '{key}': {ex.Message}");
        }

        return defaultValue ?? key;
    }

    /// <summary>
    /// 获取指定 Key 的本地化字符串（静态方法，供 ServiceLib 后台代码使用）
    /// 这个方法可以在没有 WPF UI 上下文的后台线程中安全调用
    /// </summary>
    /// <param name="key">资源 Key（格式：String_xxx）</param>
    /// <param name="defaultValue">如果找不到资源时的默认值</param>
    /// <returns>本地化字符串</returns>
    /// <example>
    /// // 在 ServiceLib 中使用：
    /// var message = LanguageManager.GetLocalizedString("String_MsgGetSubscriptionSuccessfully", "获取订阅内容成功");
    /// NoticeHandler.Instance.Enqueue(message);
    /// </example>
    public static string GetLocalizedString(string key, string? defaultValue = null)
    {
        try
        {
            // 优先从应用程序资源中查找（这会自动查找所有合并的资源字典）
            if (Application.Current != null)
            {
                try
                {
                    var resource = Application.Current.TryFindResource(key);
                    if (resource is string appValue && !string.IsNullOrEmpty(appValue))
                    {
                        return appValue;
                    }
                }
                catch
                {
                    // 忽略跨线程访问异常，继续尝试其他方法
                }
            }

            // 备用方案：从当前实例的资源字典中获取
            var instance = Instance;
            if (instance._currentLanguageDict != null && instance._currentLanguageDict.Contains(key))
            {
                if (instance._currentLanguageDict[key] is string value && !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }
        catch (Exception ex)
        {
            // 不记录日志，避免递归调用
            System.Diagnostics.Debug.WriteLine($"Failed to get localized string for key '{key}': {ex.Message}");
        }

        return defaultValue ?? key;
    }

    /// <summary>
    /// 保存语言设置到配置文件
    /// </summary>
    /// <param name="languageCode">语言代码</param>
    private void SaveLanguageToConfig(string languageCode)
    {
        try
        {
            var config = AppManager.Instance.Config;
            if (config?.UiItem != null)
            {
                config.UiItem.CurrentLanguage = languageCode;
                _ = ConfigHandler.SaveConfig(config);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Failed to save language to config: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取语言显示名称
    /// </summary>
    /// <param name="languageCode">语言代码</param>
    /// <returns>语言显示名称</returns>
    public string GetLanguageDisplayName(string languageCode)
    {
        return SupportedLanguages.TryGetValue(languageCode, out var displayName) 
            ? displayName 
            : languageCode;
    }

    /// <summary>
    /// 获取所有支持的语言列表
    /// </summary>
    /// <returns>语言代码和显示名称的键值对列表</returns>
    public List<KeyValuePair<string, string>> GetAllLanguages()
    {
        return SupportedLanguages.ToList();
    }

    /// <summary>
    /// 递归刷新窗口中所有 DataGrid 的列头
    /// </summary>
    /// <param name="parent">父容器</param>
    private static void RefreshDataGridHeaders(DependencyObject? parent)
    {
        if (parent == null) return;

        // 检查当前元素是否是 DataGrid
        if (parent is System.Windows.Controls.DataGrid dataGrid)
        {
            // DataGrid 使用 DynamicResource 绑定的列头会自动更新
            // 但我们需要强制刷新以确保立即生效
            dataGrid.Items.Refresh();
            dataGrid.UpdateLayout();
            
            // 强制重新渲染列头
            if (dataGrid.Columns.Count > 0)
            {
                // 触发列头重新渲染的技巧：临时修改列宽
                foreach (System.Windows.Controls.DataGridColumn column in dataGrid.Columns)
                {
                    double originalWidth = column.ActualWidth;
                    column.Width = new System.Windows.Controls.DataGridLength(originalWidth + 0.1);
                    column.Width = new System.Windows.Controls.DataGridLength(originalWidth);
                }
            }
        }

        // 递归处理子元素
        int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            RefreshDataGridHeaders(child);
        }
    }
}