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
namespace MQTTnetAzureFunction
{
   using System;
   using System.Text;
   using Microsoft.Azure.WebJobs;
   using Microsoft.Extensions.Logging;

   using CaseOnline.Azure.WebJobs.Extensions.Mqtt;
   using CaseOnline.Azure.WebJobs.Extensions.Mqtt.Messaging;
   using CaseOnline.Azure.WebJobs.Extensions.Mqtt.Config;
   using CaseOnline.Azure.WebJobs.Extensions.Mqtt.Bindings;

   using MQTTnet.Client.Options;
   using MQTTnet.Extensions.ManagedClient;

   public static class Subscribe
   {
      [FunctionName("UplinkMessageProcessor")]
      public static void UplinkMessageProcessor(
            [MqttTrigger(typeof(ExampleMqttConfigProvider), "v3/%TopicName%/devices/+/up")] IMqttMessage message,
            ILogger log)
      {
         var body = Encoding.UTF8.GetString(message.GetMessage());

         log.LogInformation($"Advanced: message from topic {message.Topic} \nbody: {body}");
      }
   }

   public class MqttConfigExample : CustomMqttConfig
   {
      public override IManagedMqttClientOptions Options { get; }

      public override string Name { get; }

      public MqttConfigExample(string name, IManagedMqttClientOptions options)
      {
         Options = options;
         Name = name;
      }
   }

   public class ExampleMqttConfigProvider : ICreateMqttConfig
   {
      public CustomMqttConfig Create(INameResolver nameResolver, ILogger logger)
      {
         var connectionString = new MqttConnectionString(nameResolver.Resolve("TTNMQTTConnectionString"), "CustomConfiguration");

         var options = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                .WithClientOptions(new MqttClientOptionsBuilder()
                     .WithClientId(connectionString.ClientId.ToString())
                     .WithTcpServer(connectionString.Server, connectionString.Port)
                     .WithCredentials(connectionString.Username, connectionString.Password)
                     .Build())
                .Build();

         return new MqttConfigExample("CustomConnection", options);
      }
   }
}
