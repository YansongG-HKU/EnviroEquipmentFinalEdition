using System.Threading.Tasks;

namespace SiemensS7Demo.Wpf.Smoke;

public sealed class HeadlessSmokeRunner
{
    public Task<int> RunAsync()
    {
        // Real implementation lands in Task 6 (M1.6).
        return Task.FromResult(0);
    }
}
