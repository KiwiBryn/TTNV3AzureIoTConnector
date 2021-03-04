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
namespace devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector
{
   using System;
   using System.Collections.Generic;

   public class AzureDeviceProvisiongServiceSettings
   {
      public string IdScope { get; set; }
      public string GroupEnrollmentKey { get; set; }
   }

   public class AzureSettings
   {
      public string IoTHubConnectionString { get; set; }
      public AzureDeviceProvisiongServiceSettings DeviceProvisioningServiceSettings { get; set; }
   }

   public class ApplicationSetting
   {
      public AzureSettings AzureSettings { get; set; }

      public string MQTTAccessKey { get; set; }

      public bool? DeviceIntegrationDefault { get; set; }
      public byte? DevicePageSize { get; set; }
   }

   public class TheThingsIndustries
   {
      public string MqttServerName { get; set; }
      public string MqttClientId { get; set; }
      public TimeSpan MqttAutoReconnectDelay { get; set; }

      public string Tenant { get; set; }
      public string Collaborator { get; set; }
      public string ApiBaseUrl { get; set; }
      public string ApiKey { get; set; }

      public bool DeviceIntegrationDefault { get; set; }
      public byte DevicePageSize { get; set; }
   }

   public class ProgramSettings
   {
      public TheThingsIndustries TheThingsIndustries { get; set; }

      public AzureSettings AzureSettingsDefault { get; set; }

      public Dictionary<string, ApplicationSetting> Applications { get; set; }

      public bool AzureConnectionStringResolve(string applicationId, out string connectionString)
      {
         connectionString = string.Empty;

         if (this.Applications.ContainsKey(applicationId))
         {
            if (this.Applications[applicationId].AzureSettings != null)
            {
               if (!string.IsNullOrWhiteSpace(this.Applications[applicationId].AzureSettings.IoTHubConnectionString))
               {
                  connectionString = this.Applications[applicationId].AzureSettings.IoTHubConnectionString;

                  return true;
               }
            }
         }

         if (this.AzureSettingsDefault != null)
         {
            if (!string.IsNullOrWhiteSpace(this.AzureSettingsDefault.IoTHubConnectionString))
            {
               connectionString = this.AzureSettingsDefault.IoTHubConnectionString;

               return true;
            }
         }

         return false;
      }

      public string ApplicationIdResolve(string applicationId)
      {
         if (string.IsNullOrEmpty(this.TheThingsIndustries.Tenant))
         {
            return $"{applicationId}";
         }
         else
         {
            return $"{applicationId}@{this.TheThingsIndustries.Tenant}";
         }
      }

      public string MqttAccessKeyResolve( string applicationId )
      {
         return this.Applications[applicationId].MQTTAccessKey;
      }
   }
}
