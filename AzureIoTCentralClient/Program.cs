//---------------------------------------------------------------------------------
// Copyright (c) December 2020, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//---------------------------------------------------------------------------------
namespace devMobile.TheThingsNetwork.AzureIoTCentralClient
{
   using System;
   using System.Collections.Generic;
   using System.Text;
   using System.Threading;
   using System.Threading.Tasks;

   using Microsoft.Azure.Devices.Client;

   using CommandLine;
   using Newtonsoft.Json;
   using Newtonsoft.Json.Linq;
#if DEVICE_PROPERTIES
   using Microsoft.Azure.Devices.Shared;
#endif

   using devMobile.TheThingsNetwork.Models;

   class Program
   {
      public static async Task Main(string[] args)
      {
         Console.WriteLine("devMobile.TheThingsNetwork.AzureIoTCentralClient starting");
         Console.WriteLine();

         await Parser.Default.ParseArguments<CommandLineOptions>(args)
            .WithNotParsed(HandleParseError)
            .WithParsedAsync(ApplicationCore);
      }

      private static void HandleParseError(IEnumerable<Error> errors)
      {
         if (errors.IsVersion())
         {
            Console.WriteLine("Version Request");
            return;
         }

         if (errors.IsHelp())
         {
            Console.WriteLine("Help Request");
            return;
         }
         Console.WriteLine("Parser Fail");
      }

      private static async Task ApplicationCore(CommandLineOptions options)
      {
         string tennantId = "MyTenantID";
         string applicationId = "MyApplicationId";
         string deviceId = "MyDeviceId";
         DeviceClient azureIoTHubClient;
         Timer MessageSender;

         try
         {
            // Open up the connection
            azureIoTHubClient = DeviceClient.CreateFromConnectionString(options.AzureIoTHubconnectionString, TransportType.Amqp_Tcp_Only);

            await azureIoTHubClient.OpenAsync();
#if AZURE_IOT_CENTRAL
            await azureIoTHubClient.SetReceiveMessageHandlerAsync(ReceiveMessageHandler, 
               new AzureIoTMessageHandlerContext()
               {
                  AzureIoTHubClient = azureIoTHubClient,
                  TenantId = tennantId,
                  ApplicationId = applicationId,
                  DeviceId = deviceId
               });
            await azureIoTHubClient.SetMethodDefaultHandlerAsync(MethodCallbackDefaultHandler, 
               new AzureIoTMethodHandlerContext()
               {
                  AzureIoTHubClient = azureIoTHubClient,
                  TenantId = tennantId,
                  ApplicationId = applicationId,
                  DeviceId = deviceId
               });
#endif

#if AZURE_IOT_HUB
            await azureIoTHubClient.SetReceiveMessageHandlerAsync(ReceiveMessageHandler,
               new AzureIoTMessageHandlerContext()
               {
                  AzureIoTHubClient = azureIoTHubClient,
                  TenantId = tennantId,
                  ApplicationId = applicationId,
                  DeviceId = deviceId
               });
            await azureIoTHubClient.SetMethodDefaultHandlerAsync(MethodCallbackDefaultHandler,
               new AzureIoTMethodHandlerContext()
               {
                  AzureIoTHubClient = azureIoTHubClient,
                  TenantId = tennantId,
                  ApplicationId = applicationId,
                  DeviceId = deviceId
               });
#endif

#if DEVICE_PROPERTIES
            await azureIoTHubClient.SetDesiredPropertyUpdateCallbackAsync(DesiredPropertyUpdateCallback, azureIoTHubClient);

            TwinCollection reportedProperties = new TwinCollection();

            reportedProperties["Analog_Output_150"] = 1.4f;
            reportedProperties["Analog_Output_160"] = 1.5f;
            reportedProperties["Analog_Output_170"] = new TimeSpan(0, 10, 0).TotalMinutes;

            azureIoTHubClient.UpdateReportedPropertiesAsync(reportedProperties).Wait();
#endif

            MessageSender = new Timer(TelemetryTimerCallbackAsync, azureIoTHubClient, new TimeSpan(0, 0, 10), new TimeSpan(0, 2, 0));

            Console.WriteLine("Press any key to exit");
            while (!Console.KeyAvailable)
            {
               await Task.Delay(100);
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Main {ex.Message}");
            Console.WriteLine("Press <enter> to exit");
            Console.ReadLine();
         }
      }

      private static async void TelemetryTimerCallbackAsync(object state)
      {
         Random random = new Random();
         DeviceClient azureIoTHubClient = (DeviceClient)state;

         float analogInput = (float)random.NextDouble();
         Console.WriteLine($"Analog Input:{analogInput}");

         bool digitalInput = (random.NextDouble() >= 0.5);
         Console.WriteLine($"Digital Input:{digitalInput}");

         // Christchurch cathedral with some randomness
         float latitude = (float)(-43.5309 + (random.NextDouble() - 0.5) * 0.1);
         float longitude = (float)(172.6371 + (random.NextDouble() - 0.5) * 0.1);
         float altitude = (float)(random.NextDouble() * 100.0);
         Console.WriteLine($"Latitude:{latitude} Longitude:{longitude} Altitude:{altitude}");

         DigitialTelemetryPayload payload = new DigitialTelemetryPayload()
         {
            AnalogInput = analogInput,
            DigitalInput = digitalInput,
            GPSPosition = new GPSPosition()
            {
               Latitude = latitude,
               Longitude = longitude,
               Altitude = altitude,
            }
         };

         try
         {
            using (Message message = new Message(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(payload))))
            {
               Console.WriteLine(" {0:HH:mm:ss} AzureIoTHubDeviceClient SendEventAsync start", DateTime.UtcNow);
               await azureIoTHubClient.SendEventAsync(message);
               Console.WriteLine(" {0:HH:mm:ss} AzureIoTHubDeviceClient SendEventAsync finish", DateTime.UtcNow);
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine($"TimerCallbackAsync {ex.Message}");
         }
         Console.WriteLine();
      }

#if AZURE_IOT_HUB
      private async static Task ReceiveMessageHandler(Message message, object userContext)
      {
         AzureIoTMessageHandlerContext context = (AzureIoTMessageHandlerContext)userContext;

         Console.WriteLine($"ReceiveMessageHandler handler method was called.");

         Console.WriteLine($" Message ID:{message.MessageId}");
         Console.WriteLine($" Message Schema:{message.MessageSchema}");
         Console.WriteLine($" Correlation ID:{message.CorrelationId}");
         Console.WriteLine($" Lock Token:{message.LockToken}");
         Console.WriteLine($" Component name:{message.ComponentName}");
         Console.WriteLine($" To:{message.To}");
         Console.WriteLine($" Module ID:{message.ConnectionModuleId}");
         Console.WriteLine($" Device ID:{message.ConnectionDeviceId}");
         Console.WriteLine($" User ID:{message.UserId}");
         Console.WriteLine($" CreatedAt:{message.CreationTimeUtc}");
         Console.WriteLine($" EnqueuedAt:{message.EnqueuedTimeUtc}");
         Console.WriteLine($" ExpiresAt:{message.ExpiryTimeUtc}");
         Console.WriteLine($" Delivery count:{message.DeliveryCount}");
         Console.WriteLine($" InputName:{message.InputName}");
         Console.WriteLine($" SequenceNumber:{message.SequenceNumber}");

         foreach (var property in message.Properties)
         {
            Console.WriteLine($"   Key:{property.Key} Value:{property.Value}");
         }

         Console.WriteLine($" Content encoding:{message.ContentEncoding}");
         Console.WriteLine($" Content type:{message.ContentType}");
         string payloadText = Encoding.UTF8.GetString(message.GetBytes());
         Console.WriteLine($" Content:{payloadText}");

         if (string.IsNullOrWhiteSpace(payloadText))
         {
            await context.AzureIoTHubClient.RejectAsync(message);
            Console.WriteLine($"   Payload null or white space");
            return;
         }

         JObject payload;

         if (IsValidJSON(payloadText))
         {
            payload = JObject.Parse(payloadText);
         }
         else
         {
            /*
            payload = new JObject
            {
               { methodName, payloadText }
            };
            */
         }

         string downlinktopic = $"v3/{context.ApplicationId}@{context.TenantId}/devices/{context.DeviceId}/down/push";

         DownlinkPayload downlinkPayload = new DownlinkPayload()
         {
            Downlinks = new List<Downlink>()
            {
               new Downlink()
               {
                  Confirmed = false,
                  //PayloadRaw = messageBody,
                  //PayloadDecoded = payload,
                  Priority = DownlinkPriority.Normal,
                  Port = 10,
                  CorrelationIds = new List<string>()
                  {
                     message.LockToken
                  }
               }
            }
         };

         Console.WriteLine($"TTN Topic :{downlinktopic}");
         Console.WriteLine($"TTN downlink JSON :{JsonConvert.SerializeObject(downlinkPayload, Formatting.Indented)}");

         //await receiveMessageHandlerConext.AzureIoTHubClient.AbandonAsync(message); // message retries
         //await receiveMessageHandlerConext.AzureIoTHubClient.CompleteAsync(message);
         await context.AzureIoTHubClient.CompleteAsync(message.LockToken);
         //await receiveMessageHandlerConext.AzureIoTHubClient.RejectAsync(message); // message gone no re
      }
#endif

#if AZURE_IOT_CENTRAL
      private async static Task ReceiveMessageHandler(Message message, object userContext)
      {
         AzureIoTMessageHandlerContext receiveMessageHandlerConext = (AzureIoTMessageHandlerContext)userContext;

         Console.WriteLine($"ReceiveMessageHandler handler method was called.");

         Console.WriteLine($" Message ID:{message.MessageId}");
         Console.WriteLine($" Message Schema:{message.MessageSchema}");
         Console.WriteLine($" Correlation ID:{message.CorrelationId}");
         Console.WriteLine($" Lock Token:{message.LockToken}");
         Console.WriteLine($" Component name:{message.ComponentName}");
         Console.WriteLine($" To:{message.To}");
         Console.WriteLine($" Module ID:{message.ConnectionModuleId}");
         Console.WriteLine($" Device ID:{message.ConnectionDeviceId}");
         Console.WriteLine($" User ID:{message.UserId}");
         Console.WriteLine($" CreatedAt:{message.CreationTimeUtc}");
         Console.WriteLine($" EnqueuedAt:{message.EnqueuedTimeUtc}");
         Console.WriteLine($" ExpiresAt:{message.ExpiryTimeUtc}");
         Console.WriteLine($" Delivery count:{message.DeliveryCount}");
         Console.WriteLine($" InputName:{message.InputName}");
         Console.WriteLine($" SequenceNumber:{message.SequenceNumber}");

         foreach (var property in message.Properties)
         {
            Console.WriteLine($"   Key:{property.Key} Value:{property.Value}");
         }

         Console.WriteLine($" Content encoding:{message.ContentEncoding}");
         Console.WriteLine($" Content type:{message.ContentType}");
         string payloadText = Encoding.UTF8.GetString(message.GetBytes());
         Console.WriteLine($" Content:{payloadText}");
         Console.WriteLine();

         if (!message.Properties.ContainsKey("method-name"))
         {
            await receiveMessageHandlerConext.AzureIoTHubClient.RejectAsync(message);
            Console.WriteLine($"   Property method-name not found");
            return;
         }

         string methodName = message.Properties["method-name"];
         if (string.IsNullOrWhiteSpace( methodName))
         {
            await receiveMessageHandlerConext.AzureIoTHubClient.RejectAsync(message);
            Console.WriteLine($"   Property null or white space");
            return;
         }

         if (string.IsNullOrWhiteSpace(payloadText))
         {
            await receiveMessageHandlerConext.AzureIoTHubClient.RejectAsync(message);
            Console.WriteLine($"   Payload null or white space");
            return;
         }

         JObject payload;

         if (IsValidJSON(payloadText))
         {
            payload = JObject.Parse(payloadText);
         }
         else
         {
            payload = new JObject
            {
               { methodName, payloadText }
            };
         }

         string downlinktopic = $"v3/{receiveMessageHandlerConext.ApplicationId}@{receiveMessageHandlerConext.TenantId}/devices/{receiveMessageHandlerConext.DeviceId}/down/push";

         DownlinkPayload downlinkPayload = new DownlinkPayload()
         {
            Downlinks = new List<Downlink>()
            {
               new Downlink()
               {
                  Confirmed = false,
                  //PayloadRaw = messageBody,
                  PayloadDecoded = payload,
                  Priority = DownlinkPriority.Normal,
                  Port = 10,
                  CorrelationIds = new List<string>()
                  {
                     message.LockToken
                  }
               }
            }
         };

         Console.WriteLine($"TTN Topic :{downlinktopic}");
         Console.WriteLine($"TTN downlink JSON :{JsonConvert.SerializeObject(downlinkPayload, Formatting.Indented)}");

         //await receiveMessageHandlerConext.AzureIoTHubClient.AbandonAsync(message); // message retries
         //await receiveMessageHandlerConext.AzureIoTHubClient.CompleteAsync(message);
         await receiveMessageHandlerConext.AzureIoTHubClient.CompleteAsync(message.LockToken);
         //await receiveMessageHandlerConext.AzureIoTHubClient.RejectAsync(message); // message gone no retry
      }
#endif

#if AZURE_IOT_HUB
      private static async Task<MethodResponse> MethodCallbackDefaultHandler(MethodRequest methodRequest, object userContext)
      {
         Console.WriteLine($"Default handler method {methodRequest.Name} was called.");

         Console.WriteLine($"Payload:{methodRequest.DataAsJson}");
         Console.WriteLine();

         return new MethodResponse(200);
         //return new MethodResponse(UTF8Encoding.UTF8.GetBytes(@"{""Status"":false}"), 200);
         //return new MethodResponse(UTF8Encoding.UTF8.GetBytes(methodRequest.DataAsJson), 200);
         //return new MethodResponse(UTF8Encoding.UTF8.GetBytes("false"), 200); // Response invalid so not unpacked
         //return new MethodResponse(methodRequest.Data, 200);
         //return new MethodResponse(400);
         //return new MethodResponse(404);
      }
#endif

#if AZURE_IOT_CENTRAL
      private static async Task<MethodResponse> MethodCallbackDefaultHandler(MethodRequest methodRequest, object userContext)
      {
         AzureIoTMethodHandlerContext receiveMessageHandlerConext = (AzureIoTMethodHandlerContext)userContext;

         Console.WriteLine($"Default handler method {methodRequest.Name} was called.");

         Console.WriteLine($"Payload:{methodRequest.DataAsJson}");
         Console.WriteLine();

         if (string.IsNullOrWhiteSpace(methodRequest.Name))
         {
            Console.WriteLine($"   Method Request Name null or white space");
            return new MethodResponse(400);
         }

         string payloadText = Encoding.UTF8.GetString(methodRequest.Data);
         if (string.IsNullOrWhiteSpace(payloadText))
         {
            Console.WriteLine($"   Payload null or white space");
            return new MethodResponse(400);
         }

         // At this point would check to see if Azure DeviceClient is in cache
         if ( String.Compare( methodRequest.Name, "Analog_Output_1", true) ==0 )
         {
            Console.WriteLine($"   Device not found");
            return new MethodResponse(UTF8Encoding.UTF8.GetBytes("Device not found"), 404);
         }

         JObject payload;

         if (IsValidJSON(payloadText))
         {
            payload = JObject.Parse(payloadText);
         }
         else
         {
            payload = new JObject
            {
               { methodRequest.Name, payloadText }
            };
         }

         string downlinktopic = $"v3/{receiveMessageHandlerConext.ApplicationId}@{receiveMessageHandlerConext.TenantId}/devices/{receiveMessageHandlerConext.DeviceId}/down/push";

         DownlinkPayload downlinkPayload = new DownlinkPayload()
         {
            Downlinks = new List<Downlink>()
            {
               new Downlink()
               {
                  Confirmed = false,
                  //PayloadRaw = messageBody,
                  PayloadDecoded = payload,
                  Priority = DownlinkPriority.Normal,
                  Port = 10,
                  /*
                  CorrelationIds = new List<string>()
                  {
                     methodRequest.LockToken
                  }
                  */
               }
            }
         };

         Console.WriteLine($"TTN Topic :{downlinktopic}");
         Console.WriteLine($"TTN downlink JSON :{JsonConvert.SerializeObject(downlinkPayload, Formatting.Indented)}");

         return new MethodResponse(200);
      }
#endif

#if DEVICE_PROPERTIES
      private static async Task DesiredPropertyUpdateCallback(TwinCollection desiredProperties, object userContext)
      {
         try
         {
            DeviceClient azureIoTHubClient = (DeviceClient)userContext;

            Console.WriteLine($"Properties:{desiredProperties.ToJson()}");

            TwinCollection reportedProperties = new TwinCollection();

            reportedProperties["Analog_Output_160"] = 22.0f;

            await azureIoTHubClient.UpdateReportedPropertiesAsync(reportedProperties);
         }
         catch( Exception ex)
         {
            Console.WriteLine( ex.Message);
         }
      }
#endif

      private static bool IsValidJSON(string json)
      {
         try
         {
            JToken token = JObject.Parse(json);
            return true;
         }
         catch
         {
            return false;
         }
      }

      public class AzureIoTMessageHandlerContext
      {
         public DeviceClient AzureIoTHubClient { get; set; }
         public string TenantId { get; set; }
         public string ApplicationId { get; set; }
         public string DeviceId { get; set; }
      }

      public class AzureIoTMethodHandlerContext
      {
         public DeviceClient AzureIoTHubClient { get; set; }
         public string TenantId { get; set; }
         public string ApplicationId { get; set; }
         public string DeviceId { get; set; }
      }

      public class GPSPosition
      {
         [JsonProperty("lat")]
         public float Latitude { get; set; }
         [JsonProperty("lon")]
         public float Longitude { get; set; }
         [JsonProperty("alt")]
         public float Altitude { get; set; }
      }

      public class DigitialTelemetryPayload
      {
         [JsonProperty("Digital_Input_0")]
         public bool DigitalInput { get; set; }

         [JsonProperty("Analog_Input_0")]
         public float AnalogInput { get; set; }

         [JsonProperty("GPS_0")]
         public GPSPosition GPSPosition { get; set; }
      }
   }
}
