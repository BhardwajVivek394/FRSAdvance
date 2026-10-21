using Energy7In.DataEdgeX.WebSocket;
using System;
using System.Configuration;
using System.Threading.Tasks;

namespace EdgeXWebsocket
{
    public static class Websocket
    {
        private static E7EdgeXWSBroadcaster _healthWsBroadcaster;
            

        public static void BroadcastMaintenanceMode(string topic, Domain.MaintenanceMode json)
        {
            try
            {
                if (_healthWsBroadcaster != null && _healthWsBroadcaster.IsConnected)
                {
                    Task.Run(async () => await _healthWsBroadcaster.BroadcastAsync(topic, json))
         .Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"WebSocket broadcast error: {ex.Message}", ex);
            }
          
        }
        public static void ConnectWsBroadcaster(string topic)
        {
            //if (_healthWsBroadcaster != null && !_healthWsBroadcaster.IsConnected)
            //{
            //    try { _healthWsBroadcaster.DisconnectAsync().GetAwaiter().GetResult(); _healthWsBroadcaster.Dispose(); } catch { }
            //}

            try
            {
                string baseUrl = ConfigurationManager.AppSettings["WebSocketBroadcastBaseUrl"];
                _healthWsBroadcaster = new E7EdgeXWSBroadcaster(baseUrl, topic);
                _healthWsBroadcaster.AutoReconnect = true;

                _healthWsBroadcaster.OnConnectedAsync += async () =>
                {
                    Console.WriteLine("[MM_WS_CONNECTED]");
                    await Task.CompletedTask;
                };

                _healthWsBroadcaster.OnErrorAsync += async (args) =>
                {
                    Console.WriteLine($"[MM_WS_ERROR] {args.Message}");
                    await Task.CompletedTask;
                };

                Task.Run(async () => await _healthWsBroadcaster.ConnectAsync()).Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket connection error: {ex.Message}");

            }
        }
    }
}