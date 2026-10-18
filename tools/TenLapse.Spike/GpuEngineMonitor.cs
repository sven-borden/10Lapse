using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Performance;

namespace TenLapse.Spike;

/// <summary>
/// Samples this process's GPU engine utilisation (the "GPU Engine" performance
/// counters Task Manager shows) four times a second. A hardware H.264 encode
/// shows up on the VideoEncode engine; the Microsoft software encoder leaves it at 0.
/// </summary>
internal sealed unsafe class GpuEngineMonitor : IDisposable
{
    public static readonly string[] Engines = ["VideoEncode", "VideoDecode", "3D", "Copy"];

    private readonly PDH_HQUERY _query;
    private readonly PDH_HCOUNTER[] _counters;
    private readonly double[] _max = new double[Engines.Length];
    private readonly double[] _sum = new double[Engines.Length];
    private readonly HashSet<string> _instances = [];
    private readonly Thread _thread;
    private volatile bool _stopping;
    private int _samples;

    public GpuEngineMonitor(int processId)
    {
        PDH_HQUERY query;
        Check(PInvoke.PdhOpenQuery(default(PCWSTR), 0, &query), "PdhOpenQuery");
        _query = query;
        _counters = new PDH_HCOUNTER[Engines.Length];
        for (var i = 0; i < Engines.Length; i++)
        {
            var path = $@"\GPU Engine(pid_{processId}_*engtype_{Engines[i]})\Utilization Percentage";
            fixed (char* text = path)
            {
                PDH_HCOUNTER counter;
                Check(PInvoke.PdhAddEnglishCounter(_query, text, 0, &counter), "PdhAddEnglishCounter");
                _counters[i] = counter;
            }
        }

        PInvoke.PdhCollectQueryData(_query);
        _thread = new Thread(Run) { IsBackground = true, Name = "GPU engine sampler" };
        _thread.Start();
    }

    /// <summary>Per engine: the highest and the mean utilisation in percent, and how many samples were taken.</summary>
    public (string Engine, double Max, double Mean)[] Results()
    {
        var samples = Math.Max(1, _samples);
        return [.. Engines.Select((engine, i) => (engine, _max[i], _sum[i] / samples))];
    }

    /// <summary>The counter instance names seen, to show which adapter and engine did the work.</summary>
    public IReadOnlyCollection<string> Instances => _instances;

    public void Stop()
    {
        _stopping = true;
        _thread.Join();
    }

    public void Dispose()
    {
        if (!_stopping)
        {
            Stop();
        }

        PInvoke.PdhCloseQuery(_query);
    }

    private void Run()
    {
        while (!_stopping)
        {
            Thread.Sleep(250);
            if (PInvoke.PdhCollectQueryData(_query) != 0)
            {
                continue;
            }

            for (var i = 0; i < Engines.Length; i++)
            {
                var total = Read(_counters[i]);
                _max[i] = Math.Max(_max[i], total);
                _sum[i] += total;
            }

            _samples++;
        }
    }

    private double Read(PDH_HCOUNTER counter)
    {
        uint size = 0;
        uint count = 0;
        var status = PInvoke.PdhGetFormattedCounterArray(counter, PDH_FMT.PDH_FMT_DOUBLE, &size, &count, null);
        if (status != PInvoke.PDH_MORE_DATA || size == 0)
        {
            return 0;
        }

        var buffer = new byte[size];
        fixed (byte* bytes = buffer)
        {
            var items = (PDH_FMT_COUNTERVALUE_ITEM_W*)bytes;
            if (PInvoke.PdhGetFormattedCounterArray(counter, PDH_FMT.PDH_FMT_DOUBLE, &size, &count, items) != 0)
            {
                return 0;
            }

            var total = 0.0;
            for (var i = 0; i < count; i++)
            {
                if (items[i].FmtValue.CStatus == PInvoke.PDH_CSTATUS_VALID_DATA)
                {
                    total += items[i].FmtValue.Anonymous.doubleValue;
                    if (items[i].FmtValue.Anonymous.doubleValue > 0)
                    {
                        lock (_instances)
                        {
                            _instances.Add(items[i].szName.ToString());
                        }
                    }
                }
            }

            return total;
        }
    }

    private static void Check(uint status, string call)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"{call} failed with PDH status 0x{status:X8}.");
        }
    }
}
