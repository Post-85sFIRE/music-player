namespace MusicPlayer.Core.Interfaces;

public interface INotificationService
{
    void Notify(string message, string? title = null);
}
