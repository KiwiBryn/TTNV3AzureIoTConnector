//---------------------------------------------------------------------------------
// Copyright (c) November 2020, devMobile Software
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
namespace devMobile.TheThingsNetwork.Models
{
   using Newtonsoft.Json;

   public class ApplicationIds
   {
      [JsonProperty("application_id")]
      public string ApplicationId { get; set; }
   }

   public class EndDeviceIds
   {
      [JsonProperty("device_id")]
      public string DeviceId { get; set; }
   
      [JsonProperty("application_ids")]
      public ApplicationIds ApplicationIds { get; set; }
      
      [JsonProperty("dev_eui")]
      public string DeviceEui { get; set; }

      [JsonProperty("join_eui")]
      public string JoinEui { get; set; }

      [JsonProperty("dev_addr")]
      public string DeviceAddress { get; set; }
   }
}
