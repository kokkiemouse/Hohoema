﻿using Mntone.Nico2.Live.Watch;
using Newtonsoft.Json.Linq;
using NicoPlayerHohoema.Models.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace NicoPlayerHohoema.Models.Live
{
    public delegate void LiveStreamRecieveServerTimeHandler(DateTime serverTime);
    public delegate void LiveStreamRecievePermitHandler(string parmit);
    public delegate void LiveStreamRecieveCurrentStreamHandler(Live2CurrentStreamEventArgs e);
    public delegate void LiveStreamRecieveCurrentRoomHandler(Live2CurrentRoomEventArgs e);
    public delegate void LiveStreamRecieveStatisticsHandler(Live2StatisticsEventArgs e);
    public delegate void LiveStreamRecieveWatchIntervalHandler(TimeSpan intervalTime);
    public delegate void LiveStreamRecieveScheduleHandler(Live2ScheduleEventArgs e);
    public delegate void LiveStreamRecieveDisconnectHandler();


    public class Live2CurrentStreamEventArgs
    {
        public string Uri { get; set; }
        public string Name { get; set; }
        public string Quality { get; set; }
        public string[] QualityTypes { get; set; }
        public string MediaServerType { get; set; }
        public string MediaServerAuth { get; set; }
        public string StreamingProtocol { get; set; }
    }

    public class Live2CurrentRoomEventArgs
    {
        public string MessageServerUrl { get; set; }
        public string MessageServerType { get; set; }
        public string RoomName { get; set; }
        public string ThreadId { get; set; }
        public int[] Forks { get; set; }
        public int[] ImportedForks { get; set; }
    }

    public class Live2StatisticsEventArgs
    {
        public long ViewCount { get; set; }
        public long CommentCount { get; set; }
        public long Count_3 { get; set; }
        public long Count_4 { get; set; }
    }

    public class Live2ScheduleEventArgs
    {
        public DateTime BeginTime { get; set; }
        public DateTime EndTime { get; set; }
    }

    public sealed class Live2WebSocket : IDisposable
    {
        MessageWebSocket MessageWebSocket { get; }

        Mntone.Nico2.Live.Watch.Crescendo.CrescendoLeoProps Props { get; }

        AsyncLock _WebSocketLock = new AsyncLock();
        DataWriter _DataWriter;

        private bool _IsWatchWithTimeshift => Props?.Program.Status == "ENDED";

        public event LiveStreamRecieveServerTimeHandler RecieveServerTime;
        public event LiveStreamRecievePermitHandler RecievePermit;
        public event LiveStreamRecieveCurrentStreamHandler RecieveCurrentStream;
        public event LiveStreamRecieveCurrentRoomHandler RecieveCurrentRoom;
        public event LiveStreamRecieveStatisticsHandler RecieveStatistics;
        public event LiveStreamRecieveWatchIntervalHandler RecieveWatchInterval;
        public event LiveStreamRecieveScheduleHandler RecieveSchedule;
        public event LiveStreamRecieveDisconnectHandler RecieveDisconnect;

        public Live2WebSocket(Mntone.Nico2.Live.Watch.Crescendo.CrescendoLeoProps props)
        {
            Props = props;
            MessageWebSocket = new MessageWebSocket();
            MessageWebSocket.Control.MessageType = SocketMessageType.Utf8;
            MessageWebSocket.MessageReceived += MessageWebSocket_MessageReceived;
            MessageWebSocket.ServerCustomValidationRequested += MessageWebSocket_ServerCustomValidationRequested;
            MessageWebSocket.Closed += MessageWebSocket_Closed;
        }

        Timer WatchingHeartbaetTimer;


        public async Task StartAsync(string requestQuality = "", bool isLowLatency = true)
        {
            var broadcastId = Props.Program.BroadcastId;
            var audienceToken = Props.Player.AudienceToken;
            using (var releaser = await _WebSocketLock.LockAsync())
            {
                Debug.WriteLine($"providerType: {Props.Program.ProviderType}");
                Debug.WriteLine($"comment websocket url: {Props.Site.Relive.WebSocketUrl}");

                await MessageWebSocket.ConnectAsync(new Uri(Props.Site.Relive.WebSocketUrl));
                _DataWriter = new DataWriter(MessageWebSocket.OutputStream);
            }

            if (string.IsNullOrEmpty(requestQuality))
            {
                requestQuality = "high";
            }


            var getpermitCommandText = $@"{{""type"":""watch"",""body"":{{""command"":""getpermit"",""requirement"":{{""broadcastId"":""{broadcastId}"",""route"":"""",""stream"":{{""protocol"":""hls"",""requireNewStream"":true,""priorStreamQuality"":""{requestQuality}"", ""isLowLatency"":{isLowLatency.ToString().ToLower()}}},""room"":{{""isCommentable"":true,""protocol"":""webSocket""}}}}}}}}";
            //var getpermitCommandText = $"{{\"type\":\"watch\",\"body\":{{\"params\":[\"{Props.BroadcastId}\",\"\",\"true\",\"hls\",\"\"],\"command\":\"getpermit\"}}}}";
            await SendMessageAsync(getpermitCommandText);
        }

        private async Task SendMessageAsync(string message)
        {
            using (var releaser = await _WebSocketLock.LockAsync())
            {
                if (_DataWriter != null)
                {
                    try
                    {
                        _DataWriter.WriteString(message);
                        await _DataWriter.StoreAsync();
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.ToString());
                    }
                }
            }
        }


        public Task SendChangeQualityMessageAsync(string quality, bool isLowLatency = true)
        {
            // qualityは
            // abr = 自動
            // normal = 1M
            // low = 384k
            // super_low = 192k
            // の4種類
            var message = $"{{\"type\":\"watch\",\"body\":{{\"command\":\"getstream\",\"requirement\":{{\"protocol\":\"hls\",\"quality\":\"{quality}\",\"isLowLatency\":{isLowLatency.ToString().ToLower()}}}}}}}";
            return SendMessageAsync(message);
        }


        public async void Close()
        {
            using (var releaser = await _WebSocketLock.LockAsync())
            {
                MessageWebSocket.Close(0x8, "");
                WatchingHeartbaetTimer?.Dispose();
            }
        }

        public void Dispose()
        {
            MessageWebSocket.Dispose();
            WatchingHeartbaetTimer?.Dispose();
        }

        private async void MessageWebSocket_MessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            string recievedText = string.Empty;
            using (var releaser = await _WebSocketLock.LockAsync())
            {
                using (var reader = new StreamReader(args.GetDataStream().AsStreamForRead()))
                {
                    recievedText = reader.ReadToEnd();
                    Debug.WriteLine($"<WebSocket Message> {args.MessageType}: {recievedText}");
                }
            }

            var param = (JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(recievedText);

            var type = (string)param["type"];
            if (type == "watch")
            {
                var body = (JObject)param["body"];
                var command = (string)body["command"];
                switch (command)
                {
                    case "servertime":
                        
                        var paramItems = (JArray)body["params"];
                        var serverTimeString = paramItems[0].ToString();
                        var serverTimeTick = long.Parse(serverTimeString);
                        var serverTime = DateTime.FromBinary(serverTimeTick);
                        RecieveServerTime?.Invoke(serverTime);
                        break;
                    case "permit":
                        var permit = (string)(((JArray)body["params"])[0]);
                        RecievePermit?.Invoke(permit);
                        break;
                    case "currentstream":

                        var current_stream = (JObject)body["currentStream"];
                        var currentStreamArgs = new Live2CurrentStreamEventArgs()
                        {
                            Uri = (string)current_stream["uri"],
                            Quality = (string)current_stream["quality"],
                            QualityTypes = ((JArray)current_stream["qualityTypes"]).Select(x => x.ToString()).ToArray(),
                            MediaServerType = (string)current_stream["mediaServerType"],
                            //MediaServerAuth = (string)current_stream["mediaServerAuth"],
                            StreamingProtocol = (string)current_stream["streamingProtocol"]
                        };
                        if (current_stream.TryGetValue("name", out var name))
                        {
                            currentStreamArgs.Name = (string)name;
                        }
                        RecieveCurrentStream?.Invoke(currentStreamArgs);
                        break;
                    case "currentroom":
                        var room = (JObject)body["room"];
                        var currentRoomArgs = new Live2CurrentRoomEventArgs()
                        {
                            MessageServerUrl = (string)room["messageServerUri"],
                            MessageServerType = (string)room["messageServerType"],
                            RoomName = (string)room["roomName"],
                            ThreadId = (string)room["threadId"],
                            Forks = ((JArray)room["forks"]).Select(x => (int)x).ToArray(),
                            ImportedForks = ((JArray)room["importedForks"]).Select(x => (int)x).ToArray()
                        };
                        RecieveCurrentRoom?.Invoke(currentRoomArgs);
                        break;
                    case "statistics":
                        var countItems = ((JArray)body["params"])
                            .Select(x => x.ToString())
                            .Select(x => long.TryParse(x, out var result) ? result : 0)
                            .ToArray();
                        var statisticsArgs = new Live2StatisticsEventArgs()
                        {
                            ViewCount = countItems[0],
                            CommentCount = countItems[1],
                            Count_3 = countItems[2],
                            Count_4 = countItems[3],
                        };
                        RecieveStatistics?.Invoke(statisticsArgs);
                        break;
                    case "watchinginterval":
                        var timeString = ((JArray)body["params"]).Select(x => x.ToString()).ToArray()[0];
                        var time = TimeSpan.FromSeconds(long.Parse(timeString));
                        RecieveWatchInterval?.Invoke(time);

                        WatchingHeartbaetTimer = new Timer((state) => 
                        {
                            SendMessageAsync($"{{\"type\":\"watch\",\"body\":{{\"command\":\"watching\",\"params\":[\"{Props.Program.BroadcastId}\",\"-1\",\"0\"]}}}}").ConfigureAwait(false);
                        }
                        , null, time, time
                        );
                        break;
                    case "schedule":
                        var updateParam = (JObject)body["update"];
                        var scheduleArgs = new Live2ScheduleEventArgs()
                        {
                            BeginTime = DateTime.FromBinary((long)updateParam["begintime"]),
                            EndTime = DateTime.FromBinary((long)updateParam["endtime"]),
                        };
                        RecieveSchedule?.Invoke(scheduleArgs);
                        break;
                    case "disconnect":
                        var disconnectParams = ((JArray)body["params"]).Select(x => x.ToString()).ToArray();
                        var endtimeString = disconnectParams[0];
                        var endTime = DateTime.FromBinary(long.Parse(endtimeString));
                        var endReason = disconnectParams[1];
                        RecieveDisconnect?.Invoke();
                        break;
                    case "postkey":
                        var postkeyParams = ((JArray)body["params"]).Select(x => x.ToString()).ToArray();
                        var postKey = postkeyParams[0];

                        _Postkey = postKey;
                        break;
                }
            }
            else if (type == "ping")
            {
                await SendMessageAsync("{\"type\":\"pong\",\"body\":{}}");
            }
        }

        private async void MessageWebSocket_Closed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            using (var releaser = await _WebSocketLock.LockAsync())
            {
                Debug.WriteLine($"<WebScoket Closed> {args.Code}: {args.Reason}");
            }

            WatchingHeartbaetTimer?.Dispose();
            WatchingHeartbaetTimer = null;
        }

        private async void MessageWebSocket_ServerCustomValidationRequested(MessageWebSocket sender, WebSocketServerCustomValidationRequestedEventArgs args)
        {
            using (var releaser = await _WebSocketLock.LockAsync())
            {
                Debug.WriteLine(args.ToString());
            }
        }


        string _Postkey;
        public async Task<string> GetPostkeyAsync(string threadId)
        {
            _Postkey = null;
            await SendMessageAsync(
                $"{{\"type\":\"watch\",\"body\":{{\"command\":\"getpostkey\",\"params\":[\"{threadId}\"]}}}}"
                );

            using (var cancelToken = new CancellationTokenSource(1000))
            {
                while (_Postkey == null)
                {
                    await Task.Delay(1);
                }
            }

            return _Postkey;
        }
       
    }
}
