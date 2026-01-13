using System;
using Microsoft.AspNetCore.SignalR.Client;

namespace Shokofin.SignalR;

public class SignalrRetryPolicy(TimeSpan[] delays) : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        if (delays.Length == 0) return null;
        var count = retryContext.PreviousRetryCount;
        var delayInSeconds = count >= delays.Length ? delays[^1] : delays[count];
        return delayInSeconds;
    }
}
