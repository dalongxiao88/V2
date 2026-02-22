using ServiceLib.Models;

namespace ServiceLib.ViewModels;

public class MsgViewModel : MyReactiveObject
{
    private readonly ConcurrentQueue<string> _queueMsg = new();
    private readonly List<LogMessage> _logMessages = new(); // 存储日志消息对象
    private readonly object _logMessagesLock = new();
    private volatile bool _lastMsgFilterNotAvailable;
    private int _showLock = 0; // 0 = unlocked, 1 = locked
    public int NumMaxMsg { get; } = 500;
    public int MaxLogMessages { get; } = 1000; // 最多保留的日志消息数

    [Reactive]
    public string MsgFilter { get; set; }

    [Reactive]
    public bool AutoRefresh { get; set; }

    [Reactive]
    public bool ShowTechnicalDetails { get; set; }

    public MsgViewModel(Func<EViewAction, object?, Task<bool>>? updateView)
    {
        _config = AppManager.Instance.Config;
        _updateView = updateView;
        MsgFilter = _config.MsgUIItem.MainMsgFilter ?? string.Empty;
        AutoRefresh = _config.MsgUIItem.AutoRefresh ?? true;
        ShowTechnicalDetails = _config.MsgUIItem.ShowTechnicalDetails ?? false;

        this.WhenAnyValue(
           x => x.MsgFilter)
                .Subscribe(c => DoMsgFilter());

        this.WhenAnyValue(
          x => x.AutoRefresh,
          y => y == true)
              .Subscribe(c => _config.MsgUIItem.AutoRefresh = AutoRefresh);

        this.WhenAnyValue(
          x => x.ShowTechnicalDetails)
              .Subscribe(c =>
              {
                  _config.MsgUIItem.ShowTechnicalDetails = ShowTechnicalDetails;
                  Logging.ToggleProfessionalMode(ShowTechnicalDetails);
              });

        AppEvents.SendMsgViewRequested
         .AsObservable()
         //.ObserveOn(RxApp.MainThreadScheduler)
         .Subscribe(content => _ = AppendQueueMsg(content));

        // 订阅日志消息事件
        Logging.LogMessageReceived += OnLogMessageReceived;

        // 订阅语言切换事件
        Logging.LanguageChanged += OnLanguageChanged;
    }

    private async Task AppendQueueMsg(string msg)
    {
        if (AutoRefresh == false)
        {
            return;
        }

        EnqueueQueueMsg(msg);

        if (!AppManager.Instance.ShowInTaskbar)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _showLock, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await Task.Delay(500).ConfigureAwait(false);

            var sb = new StringBuilder();
            while (_queueMsg.TryDequeue(out var line))
            {
                sb.Append(line);
            }

            await _updateView?.Invoke(EViewAction.DispatcherShowMsg, sb.ToString());
        }
        finally
        {
            Interlocked.Exchange(ref _showLock, 0);
        }
    }

    private void EnqueueQueueMsg(string msg)
    {
        //filter msg
        if (MsgFilter.IsNotEmpty() && !_lastMsgFilterNotAvailable)
        {
            try
            {
                if (!Regex.IsMatch(msg, MsgFilter))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                _queueMsg.Enqueue(ex.Message);
                _lastMsgFilterNotAvailable = true;
            }
        }

        _queueMsg.Enqueue(msg);
        if (!msg.EndsWith(Environment.NewLine))
        {
            _queueMsg.Enqueue(Environment.NewLine);
        }
    }

    //public void ClearMsg()
    //{
    //    _queueMsg.Clear();
    //}

    private void DoMsgFilter()
    {
        _config.MsgUIItem.MainMsgFilter = MsgFilter;
        _lastMsgFilterNotAvailable = false;
    }

    /// <summary>
    /// 处理日志消息接收事件
    /// </summary>
    private void OnLogMessageReceived(LogMessage logMessage)
    {
        if (logMessage == null) return;

        // 存储日志消息对象（用于语言切换时重译）
        lock (_logMessagesLock)
        {
            _logMessages.Add(logMessage);
            
            // 限制日志消息数量，避免内存溢出
            if (_logMessages.Count > MaxLogMessages)
            {
                _logMessages.RemoveRange(0, _logMessages.Count - MaxLogMessages);
            }
        }

        // 根据专业模式格式化消息
        var formattedMessage = ShowTechnicalDetails
            ? logMessage.FormatProfessional()
            : logMessage.FormatDefault();

        // 发送到现有的消息队列
        _ = AppendQueueMsg(formattedMessage);
    }

    /// <summary>
    /// 处理语言切换事件 - 重译所有日志
    /// </summary>
    private void OnLanguageChanged(string newLanguage)
    {
        try
        {
            List<LogMessage> messagesToRetranslate;
            
            // 复制日志消息列表（避免长时间锁定）
            lock (_logMessagesLock)
            {
                messagesToRetranslate = new List<LogMessage>(_logMessages);
            }

            if (messagesToRetranslate.Count == 0)
            {
                return;
            }

            // 清空当前显示的消息队列
            while (_queueMsg.TryDequeue(out _)) { }

            // 重新格式化所有日志消息
            var sb = new StringBuilder();
            sb.AppendLine($"----- Language changed to {newLanguage}, retranslating {messagesToRetranslate.Count} log messages -----");
            
            foreach (var logMessage in messagesToRetranslate)
            {
                var formattedMessage = ShowTechnicalDetails
                    ? logMessage.FormatProfessional()
                    : logMessage.FormatDefault();
                
                sb.AppendLine(formattedMessage);
            }

            // 通过 UI 清空并重新显示（使用新的 action）
            _ = _updateView?.Invoke(EViewAction.DispatcherClearAndShowMsg, sb.ToString());
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Failed to retranslate logs on language change: {ex.Message}");
        }
    }
}
