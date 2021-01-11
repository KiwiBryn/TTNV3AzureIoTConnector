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
         DeviceClient azureIoTHubClient;
         Timer MessageSender;

         try
         {
            // Open up the connection
            azureIoTHubClient = DeviceClient.CreateFromConnectionString(options.AzureIoTHubconnectionString, TransportType.Amqp_Tcp_Only);

            await azureIoTHubClient.OpenAsync();
            await azureIoTHubClient.SetReceiveMessageHandlerAsync(ReceiveMessageHandler, azureIoTHubClient);
            await azureIoTHubClient.SetMethodHandlerAsync("Named", MethodCallbackNamedHandler, null);
            await azureIoTHubClient.SetMethodDefaultHandlerAsync(MethodCallbackDefaultHandler, null);

            MessageSender = new Timer(TimerCallbackAsync, azureIoTHubClient, new TimeSpan(0, 0, 10), new TimeSpan(0, 2, 0));

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

      private static async void TimerCallbackAsync(object state)
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
         float altitude = (float)(random.NextDouble() *100.0);
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
            // I know having the payload as a global is a bit nasty but this is a demo..
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

      private async static Task ReceiveMessageHandler(Message message, object userContext)
      {
         DeviceClient azureIoTHubClient = (DeviceClient)userContext;

         Console.WriteLine($"ReceiveMessageHandler handler method was called.");

         Console.WriteLine($" Message ID:{message.MessageId}");
         Console.WriteLine($" Message Schema:{message.MessageSchema}");
         Console.WriteLine($" Correlation ID:{message.CorrelationId}");
         Console.WriteLine($" Component name:{message.ComponentName}");
         Console.WriteLine($" To:{message.To}");
         Console.WriteLine($" Module ID:{message.ConnectionModuleId}");
         Console.WriteLine($" Device ID:{message.ConnectionDeviceId}");
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
         Console.WriteLine($" Content:{Encoding.UTF8.GetString(message.GetBytes())}");
         Console.WriteLine();

         //await azureIoTHubClient.AbandonAsync(message); // message retries
         await azureIoTHubClient.CompleteAsync(message);
         //await azureIoTHubClient.RejectAsync(message); // message gone no retry
      }

      private static async Task<MethodResponse> MethodCallbackNamedHandler(MethodRequest methodRequest, object userContext)
      {
         Console.WriteLine($"Named handler method was called.");

         Console.WriteLine($"JSON:{methodRequest.DataAsJson}");
         Console.WriteLine();

         return new MethodResponse(200);
      }

      private static async Task<MethodResponse> MethodCallbackDefaultHandler(MethodRequest methodRequest, object userContext)
      {
         Console.WriteLine($"Default handler method {methodRequest.Name} was called.");

         Console.WriteLine($"JSON:{methodRequest.DataAsJson}");
         Console.WriteLine();

         return new MethodResponse(400);
         //return new MethodResponse(404);
         //return new MethodResponse(200);
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
