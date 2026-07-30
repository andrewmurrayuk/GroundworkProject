using Microsoft.AspNetCore.SignalR;

namespace Groundwork.Hubs;

public class PipelineHub : Hub
{
    public Task JoinJob(string jobId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, jobId);
}
