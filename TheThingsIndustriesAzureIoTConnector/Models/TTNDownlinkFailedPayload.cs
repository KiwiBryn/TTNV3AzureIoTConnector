//---------------------------------------------------------------------------------
// Copyright (c) February 2021, devMobile Software
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
namespace devMobile.TheThingsIndustries.TheThingsIndustriesAzureIoTConnector.Models
{
   using System.Collections.Generic;

   using Newtonsoft.Json;
   using Newtonsoft.Json.Converters;

   public class DownlinkMessage
   {
      [JsonProperty("f_port")]
      public int f_port { get; set; }

      [JsonProperty("f_port")]
      public string frm_payload { get; set; }

      [JsonProperty("priority")]
      [JsonConverter(typeof(StringEnumConverter))]
      public string priority { get; set; }

      [JsonProperty("correlation_ids")]
      public List<string> correlation_ids { get; set; }
   }

   public class DownlinkFailedError
   {
      [JsonProperty("namespace")]
      public string Namespace { get; set; }

      [JsonProperty("name")]
      public string Name { get; set; }

      [JsonProperty("message_format")]
      public string MessageFormat { get; set; }

      [JsonProperty("code")]
      public int Code { get; set; }
   }

   public class DownlinkFailed
   {
      [JsonProperty("downlink")]
      public DownlinkMessage Downlink { get; set; }

      [JsonProperty("error")]
      public DownlinkFailedError Error { get; set; }
   }

   public class DownlinkFailedPayload
   {
      [JsonProperty("end_device_ids")]
      public EndDeviceIds EndDeviceIds { get; set; }

      [JsonProperty("correlation_ids")]
      public List<string> CorrelationIds { get; set; }

      [JsonProperty("downlink_failed")]
      public DownlinkFailed DownlinkFailed { get; set; }
   }
}