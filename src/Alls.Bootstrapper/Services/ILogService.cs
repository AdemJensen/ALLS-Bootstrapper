namespace Alls.Bootstrapper.Services;

internal interface ILogService
{
    void Info(string message);

    void Error(string message, Exception? exception = null);
}
