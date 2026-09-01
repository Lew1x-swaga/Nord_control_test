namespace NordControl.Tests;

internal static class WaitUntil
{
    public static async Task True(Func<bool> pred, int attempts = 60, int delayMs = 50)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (pred())
            {
                return;
            }

            await Task.Delay(delayMs);
        }
    }
}
