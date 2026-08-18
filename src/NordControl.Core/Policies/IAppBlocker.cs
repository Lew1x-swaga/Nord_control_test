using System;
using System.Collections.Generic;

namespace NordControl.Core.Policies;

public interface IAppBlocker : IDisposable
{
    event Action<string>? ProcessKilled;
    void SetBlockList(IEnumerable<string> exeNames);
    void Clear();
    IReadOnlyCollection<string> GetBlockList();
    bool IsBlocked(string exeName);
}
