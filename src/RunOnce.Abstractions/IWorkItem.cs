using System.Threading;
using System.Threading.Tasks;

namespace RunOnce.Abstractions;

public interface IWorkItem
{
    Task UpAsync(CancellationToken cancellationToken = default);
    Task DownAsync(CancellationToken cancellationToken = default);
}
