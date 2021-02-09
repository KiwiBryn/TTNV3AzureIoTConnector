//---------------------------------------------------------------------------------
// Copyright (c) January 2021, devMobile Software
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
namespace devMobile.TheThingsNetwork.WorkerService
{
   using System;
   using System.Collections.Generic;
   using System.Threading;
   using System.Threading.Tasks;

   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;

   public class Worker : BackgroundService
   {
      private readonly ILogger<Worker> _logger;
      private readonly ProgramSettings _applicationSettings;

      public Worker(ILogger<Worker> logger, IOptions<ProgramSettings> programSettings)
      {
         _logger = logger;
         _applicationSettings = programSettings.Value;
      }

      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
         while (!stoppingToken.IsCancellationRequested)
         {
            _logger.LogDebug("Debug worker running at: {time}", DateTimeOffset.Now);
            _logger.LogInformation("Info worker running at: {time}", DateTimeOffset.Now);
            _logger.LogWarning("Warning worker running at: {time}", DateTimeOffset.Now);
            _logger.LogError("Error running at: {time}", DateTimeOffset.Now);

            using (_logger.BeginScope("TheThingsIndustries configuration"))
            {
               _logger.LogInformation("Tennant: {0}", _applicationSettings.TheThingsIndustries.Tennant);
               _logger.LogInformation("ApiBaseUrl: {0}", _applicationSettings.TheThingsIndustries.ApiBaseUrl);
               _logger.LogInformation("ApiKey: {0}", _applicationSettings.TheThingsIndustries.ApiKey);

               _logger.LogInformation("ApplicationPageSize: {0}", _applicationSettings.TheThingsIndustries.ApplicationPageSize);
               _logger.LogInformation("DevicePageSize: {0}", _applicationSettings.TheThingsIndustries.DevicePageSize);

               _logger.LogInformation("ApplicationIntegrationDefault: {0}", _applicationSettings.TheThingsIndustries.ApplicationIntegrationDefault);
               _logger.LogInformation("DeviceIntegrationDefault: {0}", _applicationSettings.TheThingsIndustries.DeviceIntegrationDefault);

               _logger.LogInformation("MQTTServerName: {0}", _applicationSettings.TheThingsIndustries.MqttServerName);
               _logger.LogInformation("MQTTClientName: {0}", _applicationSettings.TheThingsIndustries.MqttClientName);
            }

            using (_logger.BeginScope("Azure default configuration"))
            {
               if (_applicationSettings.AzureSettingsDefault.IoTHubConnectionString != null)
               {
                  _logger.LogInformation("AzureSettingsDefault.IoTHubConnectionString: {0}", _applicationSettings.AzureSettingsDefault.IoTHubConnectionString);
               }

               if (_applicationSettings.AzureSettingsDefault.DeviceProvisioningServiceSettings != null)
               {
                  _logger.LogInformation("AzureSettings.DeviceProvisioningServiceSettings.IdScope: {0}", _applicationSettings.AzureSettingsDefault.DeviceProvisioningServiceSettings.IdScope);
                  _logger.LogInformation("AzureSettings.DeviceProvisioningServiceSettings.GroupEnrollmentKey: {0}", _applicationSettings.AzureSettingsDefault.DeviceProvisioningServiceSettings.GroupEnrollmentKey);
               }
            }
    
            foreach (var application in _applicationSettings.Applications)
            {
               using (_logger.BeginScope(new[] { new KeyValuePair<string, object>("Application", application.Key)}))
               {
                  _logger.LogInformation("MQTTAccessKey: {0} ", application.Value.MQTTAccessKey);

                  if (application.Value.ApplicationPageSize.HasValue)
                  {
                     _logger.LogInformation("ApplicationPageSize: {0} ", application.Value.ApplicationPageSize.Value);
                  }

                  if (application.Value.DeviceIntegrationDefault.HasValue)
                  {
                     _logger.LogInformation("DeviceIntegation: {0} ", application.Value.DeviceIntegrationDefault.Value);
                  }

                  if (application.Value.DevicePageSize.HasValue)
                  {
                     _logger.LogInformation("DevicePageSize: {0} ", application.Value.DevicePageSize.Value);
                  }

                  if (application.Value.AzureSettings.IoTHubConnectionString != null)
                  {
                     _logger.LogInformation("AzureSettings.IoTHubConnectionString: {0} ", application.Value.AzureSettings.IoTHubConnectionString);
                  }

                  if (application.Value.AzureSettings.DeviceProvisioningServiceSettings != null)
                  {
                     _logger.LogInformation("AzureSettings.DeviceProvisioningServiceSettings.IdScope: {0} ", application.Value.AzureSettings.DeviceProvisioningServiceSettings.IdScope);
                     _logger.LogInformation("AzureSettings.DeviceProvisioningServiceSettings.GroupEnrollmentKey: {0} ", application.Value.AzureSettings.DeviceProvisioningServiceSettings.GroupEnrollmentKey);
                  }
               }
            }

            await Task.Delay(300000, stoppingToken);
         }
      }
   }
}
