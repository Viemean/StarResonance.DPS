namespace StarResonance.DPS.Services;

/// <summary>
///     定义一个用于在应用程序中显示通知的服务。
/// </summary>
public interface INotificationService
{
    /// <summary>
    ///     显示一条通知消息。
    /// </summary>
    /// <param name="message">要显示的消息文本。</param>
    void ShowNotification(string message);
}