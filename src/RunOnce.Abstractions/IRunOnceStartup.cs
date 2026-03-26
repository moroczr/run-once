using Microsoft.Extensions.DependencyInjection;

namespace RunOnce.Abstractions;

public interface IRunOnceStartup
{
    void ConfigureServices(IServiceCollection services);
}
