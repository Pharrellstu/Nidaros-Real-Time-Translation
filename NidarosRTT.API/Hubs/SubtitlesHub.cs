using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace NidarosRTT.API.Hubs
{
    /// <summary>
    /// SignalR hub to broadcast transcriptions to connected clients.
    /// </summary>
    public class SubtitlesHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            // Optional: Log when a new client connects
            System.Console.WriteLine($"--> Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(System.Exception? exception)
        {
            // Optional: Log when a client disconnects
            System.Console.WriteLine($"--> Client disconnected: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(string message)
        {
            System.Console.WriteLine($"--> Received message from client: {message}");
            await Clients.Caller.SendAsync("ReceiveMessage", $"Server received: {message}");
        }
    }
}
