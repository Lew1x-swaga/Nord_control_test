using System;
using System.Collections.Generic;

namespace NordControl.Core.Policies;

public interface IAppBlocker : IDisposable
{
    void SetBlockList(IEnumerable<string> exeNames);
    void Clear();
    IReadOnlyCollection<string> GetBlockList();
    bool IsBlocked(string exeName);
}
