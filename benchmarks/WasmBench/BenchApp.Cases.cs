using System;

namespace NetWasmBench;

/// <summary>What the page's buttons and row links do, for the console benchmark to call.</summary>
public partial class BenchApp
{
    internal void Run(int count) => data.Value = Data.Build(count);

    internal void Append() => Add();

    internal void UpdateEveryTenth() => Update();

    internal void SwapRows() => Swap();

    internal void Clear() => data.Value = Array.Empty<Row>();

    internal void SelectAt(int index) => selected.Value = data.Value[index].Id;

    internal void RemoveAt(int index) => Remove(data.Value[index].Id);
}
