using System;

namespace NordControl.Core;

public interface IClock
{
    DateTime UtcNow { get; }
}
