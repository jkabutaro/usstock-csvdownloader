using System.Collections.Concurrent;

namespace USStockDownloader.Services;

public class ProgressManager
{
    private readonly int _totalSymbols;
    private readonly ConcurrentDictionary<string, bool> _completedSymbols = new();
    private readonly ConcurrentDictionary<string, bool> _failedSymbols = new();
    private readonly ConcurrentDictionary<string, DateTime> _startTimes = new();
    private readonly ConcurrentDictionary<string, TimeSpan> _completionTimes = new();
    private readonly object _lockObject = new();
    private readonly int _progressBarWidth = 50;
    private DateTime _startTime;

    public ProgressManager(int totalSymbols)
    {
        _totalSymbols = totalSymbols;
        _startTime = DateTime.Now;
    }

    public void StartSymbol(string symbol)
    {
        _startTimes.TryAdd(symbol, DateTime.Now);
    }

    public void MarkAsCompleted(string symbol)
    {
        if (_startTimes.TryGetValue(symbol, out var startTime))
        {
            var duration = DateTime.Now - startTime;
            _completionTimes.TryAdd(symbol, duration);
        }
        _completedSymbols.TryAdd(symbol, true);
        UpdateProgress();
    }

    public void MarkAsFailed(string symbol)
    {
        if (_startTimes.TryGetValue(symbol, out var startTime))
        {
            var duration = DateTime.Now - startTime;
            _completionTimes.TryAdd(symbol, duration);
        }
        _failedSymbols.TryAdd(symbol, true);
        UpdateProgress();
    }

    private TimeSpan? CalculateEstimatedTimeRemaining()
    {
        var completed = _completedSymbols.Count + _failedSymbols.Count;
        if (completed == 0) return null;

        // 完了したタスクの平均時間を計算
        var averageTime = _completionTimes.Values.Average(t => t.TotalSeconds);
        
        // 残りのタスク数
        var remaining = _totalSymbols - completed;
        
        // 並列実行を考慮した推定時間（完了タスクの平均時間 × 残りタスク数 ÷ 現在までの平均並列度）
        var elapsedTime = (DateTime.Now - _startTime).TotalSeconds;
        var averageConcurrency = Math.Max(1, completed / Math.Max(1, elapsedTime / averageTime));
        
        var estimatedSeconds = (averageTime * remaining) / averageConcurrency;
        return TimeSpan.FromSeconds(estimatedSeconds);
    }

    private void UpdateProgress()
    {
        lock (_lockObject)
        {
            int completed = _completedSymbols.Count;
            int failed = _failedSymbols.Count;
            int total = completed + failed;
            double percentage = (double)total / _totalSymbols;

            // カーソルを行の先頭に移動
            Console.Write("\r");

            // プログレスバーを描画
            Console.Write("[");
            int filledWidth = (int)(_progressBarWidth * percentage);
            for (int i = 0; i < _progressBarWidth; i++)
            {
                if (i < filledWidth)
                    Console.Write("=");
                else if (i == filledWidth)
                    Console.Write(">");
                else
                    Console.Write(" ");
            }
            Console.Write("] ");

            // パーセンテージと完了数を表示
            Console.Write($"{percentage:P0} ({total}/{_totalSymbols}) ");

            // 経過時間を表示
            var elapsed = DateTime.Now - _startTime;
            Console.Write($"Elapsed: {elapsed.ToString(@"hh\:mm\:ss")} ");

            // 推定残り時間を表示
            var remaining = CalculateEstimatedTimeRemaining();
            if (remaining.HasValue)
            {
                Console.Write($"ETA: {remaining.Value.ToString(@"hh\:mm\:ss")} ");
            }
            else
            {
                Console.Write("ETA: Calculating... ");
            }

            // 成功・失敗数を表示
            Console.Write($"[Success: {completed}, Failed: {failed}]");

            // 残りの文字をクリア
            Console.Write(new string(' ', Console.WindowWidth - Console.CursorLeft - 1));
        }
    }

    public void Complete()
    {
        UpdateProgress();
        Console.WriteLine(); // 最後に改行を入れる
    }
}
